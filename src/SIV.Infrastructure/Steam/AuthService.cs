using Microsoft.Extensions.Logging;
using SIV.Application.DTOs;
using SIV.Application.Interfaces;
using SIV.Domain.Enums;
using SteamKit2;
using SteamKit2.Authentication;

namespace SIV.Infrastructure.Steam;

public sealed class AuthService : IAuthService
{
    private readonly SteamConnectionService _connection;
    private readonly IAccountSessionStorage _accountStorage;
    private readonly ILogger<AuthService> _logger;
    private TaskCompletionSource<SteamUser.LoggedOnCallback>? _logonTcs;

    public bool IsLoggedIn => _connection.IsLoggedIn;
    public string? CurrentSteamId => _connection.SteamId?.Render();

    public AuthService(SteamConnectionService connection, IAccountSessionStorage accountStorage, ILogger<AuthService> logger)
    {
        _connection = connection;
        _accountStorage = accountStorage;
        _logger = logger;
        _connection.LoggedOn += OnLoggedOn;
    }

    public async Task<AuthResult> LoginAsync(string username, string password, string? twoFactorCode, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Login attempt for user {Username}", username);

            if (!_connection.IsConnected)
            {
                var connTask = WaitForConnectionAsync(ct);
                _connection.Start();
                await connTask;
            }

            var authSession = await _connection.Client.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
            {
                Username = username,
                Password = password,
                GuardData = twoFactorCode,
                Authenticator = new UserFormAuthenticator(twoFactorCode),
                IsPersistentSession = true
            });

            var pollResult = await authSession.PollingWaitForResultAsync(ct);

            _logonTcs = new TaskCompletionSource<SteamUser.LoggedOnCallback>();

            _connection.User.LogOn(CreateLogOnDetails(pollResult.AccountName, pollResult.RefreshToken));

            var logonResult = await _logonTcs.Task.WaitAsync(ct);

            if (logonResult.Result == EResult.OK)
            {
                _logger.LogInformation("Login successful for {Username}, SteamID: {SteamId}", pollResult.AccountName, _connection.SteamId);
                await SaveAccountSessionAsync(pollResult.AccountName, pollResult.RefreshToken);
                return new AuthResult(AuthResultType.Success);
            }

            return logonResult.Result switch
            {
                EResult.AccountLoginDeniedNeedTwoFactor => new AuthResult(AuthResultType.MobileAuthRequired, "Mobile authenticator code required"),
                EResult.AccountLogonDenied => new AuthResult(AuthResultType.SteamGuardRequired, "Steam Guard code required"),
                EResult.InvalidPassword => new AuthResult(AuthResultType.InvalidCredentials, "Invalid credentials"),
                EResult.RateLimitExceeded => new AuthResult(AuthResultType.RateLimited, "Rate limited, try later"),
                _ => new AuthResult(AuthResultType.Failed, logonResult.Result.ToString())
            };
        }
        catch (AuthenticationException ex)
        {
            _logger.LogError(ex, "Authentication failed for {Username}", username);
            return new AuthResult(AuthResultType.Failed, ex.Message);
        }
    }

    public async Task<AuthResult> LoginViaQrAsync(Action<string> onChallengeUrlChanged, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting QR code authentication");

            if (!_connection.IsConnected)
            {
                var connTask = WaitForConnectionAsync(ct);
                _connection.Start();
                await connTask;
            }

            var authSession = await _connection.Client.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails
            {
                IsPersistentSession = true
            });

            onChallengeUrlChanged(authSession.ChallengeURL);

            authSession.ChallengeURLChanged += () =>
            {
                onChallengeUrlChanged(authSession.ChallengeURL);
            };

            var pollResult = await authSession.PollingWaitForResultAsync(ct);

            _logonTcs = new TaskCompletionSource<SteamUser.LoggedOnCallback>();

            _connection.User.LogOn(CreateLogOnDetails(pollResult.AccountName, pollResult.RefreshToken));

            var logonResult = await _logonTcs.Task.WaitAsync(ct);

            if (logonResult.Result == EResult.OK)
            {
                _logger.LogInformation("QR login successful for {Username}, SteamID: {SteamId}", pollResult.AccountName, _connection.SteamId);
                await SaveAccountSessionAsync(pollResult.AccountName, pollResult.RefreshToken);
                return new AuthResult(AuthResultType.Success);
            }

            return new AuthResult(AuthResultType.Failed, logonResult.Result.ToString());
        }
        catch (TaskCanceledException)
        {
            return new AuthResult(AuthResultType.Failed, "QR login cancelled");
        }
        catch (AuthenticationException ex)
        {
            _logger.LogError(ex, "QR authentication failed");
            return new AuthResult(AuthResultType.Failed, ex.Message);
        }
    }

    public async Task<AuthResult> RestoreAccountSessionAsync(string accountName, CancellationToken ct = default)
    {
        var refreshToken = await _accountStorage.GetRefreshTokenAsync(accountName);

        if (refreshToken is null)
            return new AuthResult(AuthResultType.Failed, "No saved session for this account");

        if (!_connection.IsConnected)
        {
            var connTask = WaitForConnectionAsync(ct);
            _connection.Start();
            await connTask;
        }

        _logonTcs = new TaskCompletionSource<SteamUser.LoggedOnCallback>();

        _connection.User.LogOn(CreateLogOnDetails(accountName, refreshToken));

        var result = await _logonTcs.Task.WaitAsync(ct);

        if (result.Result == EResult.OK)
        {
            await SaveAccountSessionAsync(accountName, refreshToken);
            return new AuthResult(AuthResultType.Success);
        }

        return new AuthResult(AuthResultType.Failed, result.Result.ToString());
    }

    public Task<IReadOnlyList<SavedAccount>> GetSavedAccountsAsync()
        => _accountStorage.GetSavedAccountsAsync();

    public Task RemoveAccountAsync(string accountName)
        => _accountStorage.RemoveAccountAsync(accountName);

    public Task LogoutAsync()
    {
        _connection.User.LogOff();
        _connection.Stop();
        return Task.CompletedTask;
    }

    private async Task SaveAccountSessionAsync(string accountName, string refreshToken)
    {
        var profile = await FetchProfileAsync();
        var steamId = _connection.SteamId?.Render() ?? string.Empty;
        var account = new SavedAccount(
            accountName,
            steamId,
            profile?.DisplayName ?? accountName,
            profile?.AvatarUrl ?? string.Empty,
            DateTime.UtcNow);
        await _accountStorage.SaveAccountAsync(account, refreshToken);
    }

    public async Task<SteamPlayerProfile?> FetchProfileAsync(CancellationToken ct = default)
    {
        if (_connection.SteamId is null)
            return null;

        for (int i = 0; i < 10 && !ct.IsCancellationRequested; i++)
        {
            if (!string.IsNullOrEmpty(_connection.PersonaName))
                break;
            await Task.Delay(500, ct);
        }

        var name = _connection.PersonaName;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string avatarUrl = string.Empty;
        var hash = _connection.AvatarHash;
        if (hash is { Length: > 0 } && !Array.TrueForAll(hash, b => b == 0))
        {
            var hex = Convert.ToHexString(hash).ToLowerInvariant();
            avatarUrl = $"https://avatars.steamstatic.com/{hex}_full.jpg";
        }

        return new SteamPlayerProfile(name, avatarUrl);
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback cb)
    {
        _logonTcs?.TrySetResult(cb);
    }

    private async Task WaitForConnectionAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        void Handler() => tcs.TrySetResult();
        _connection.Connected += Handler;
        try
        {
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        }
        finally
        {
            _connection.Connected -= Handler;
        }
    }

    private static SteamUser.LogOnDetails CreateLogOnDetails(string accountName, string refreshToken)
        => new()
        {
            Username = accountName,
            AccessToken = refreshToken,
            ShouldRememberPassword = true,
            ClientLanguage = "english",
            MachineName = Environment.MachineName
        };
}

file sealed class UserFormAuthenticator : IAuthenticator
{
    private readonly string? _code;

    public UserFormAuthenticator(string? code) => _code = code;

    public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        => Task.FromResult(_code ?? string.Empty);

    public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        => Task.FromResult(_code ?? string.Empty);

    public Task<bool> AcceptDeviceConfirmationAsync()
        => Task.FromResult(true);
}
