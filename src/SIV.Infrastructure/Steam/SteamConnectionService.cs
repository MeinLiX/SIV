using SteamKit2;

namespace SIV.Infrastructure.Steam;

public sealed class SteamConnectionService : IDisposable
{
    private readonly SteamClient _client;
    private readonly CallbackManager _callbackManager;
    private readonly SteamUser _steamUser;
    private readonly SteamFriends _steamFriends;
    private readonly SteamGameCoordinator _gameCoordinator;
    private CancellationTokenSource? _callbackCts;
    private Task? _callbackTask;

    public SteamClient Client => _client;
    public SteamUser User => _steamUser;
    public SteamGameCoordinator GameCoordinator => _gameCoordinator;
    public CallbackManager CallbackManager => _callbackManager;
    public bool IsConnected { get; private set; }
    public bool IsLoggedIn { get; private set; }
    public SteamID? SteamId { get; private set; }
    public bool IsPlayingBlocked { get; private set; }
    public uint? BlockingAppId { get; private set; }
    public string? PersonaName { get; private set; }
    public byte[]? AvatarHash { get; private set; }

    public event Action? Connected;
    public event Action<EResult>? Disconnected;
    public event Action<SteamUser.LoggedOnCallback>? LoggedOn;
    public event Action? LoggedOff;
    public event Action<SteamUser.PlayingSessionStateCallback>? PlayingSessionStateChanged;

    public SteamConnectionService()
    {
        _client = new SteamClient();
        _callbackManager = new CallbackManager(_client);
        _steamUser = _client.GetHandler<SteamUser>()!;
        _steamFriends = _client.GetHandler<SteamFriends>()!;
        _gameCoordinator = _client.GetHandler<SteamGameCoordinator>()!;

        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
        _callbackManager.Subscribe<SteamUser.PlayingSessionStateCallback>(OnPlayingSessionStateChanged);
        _callbackManager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);
    }

    public void Start()
    {
        _client.Connect();
        _callbackCts = new CancellationTokenSource();
        _callbackTask = Task.Run(() => RunCallbacks(_callbackCts.Token));
    }

    public void Stop()
    {
        _callbackCts?.Cancel();
        _client.Disconnect();

        if (IsConnected || IsLoggedIn)
        {
            IsConnected = false;
            IsLoggedIn = false;
            IsPlayingBlocked = false;
            BlockingAppId = null;
            PersonaName = null;
            AvatarHash = null;
            Disconnected?.Invoke(EResult.OK);
        }
    }

    private void RunCallbacks(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            _callbackManager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
        }
    }

    private void OnConnected(SteamClient.ConnectedCallback cb)
    {
        IsConnected = true;
        Connected?.Invoke();
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback cb)
    {
        if (!IsConnected && !IsLoggedIn) return;

        IsConnected = false;
        IsLoggedIn = false;
        IsPlayingBlocked = false;
        BlockingAppId = null;
        PersonaName = null;
        AvatarHash = null;
        Disconnected?.Invoke(EResult.NoConnection);
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback cb)
    {
        if (cb.Result == EResult.OK)
        {
            IsLoggedIn = true;
            SteamId = cb.ClientSteamID;
            if (cb.ClientSteamID is not null)
                _steamFriends.RequestFriendInfo(cb.ClientSteamID, EClientPersonaStateFlag.PlayerName | EClientPersonaStateFlag.Presence);
        }
        LoggedOn?.Invoke(cb);
    }

    private void OnLoggedOff(SteamUser.LoggedOffCallback cb)
    {
        IsLoggedIn = false;
        LoggedOff?.Invoke();
    }

    private void OnPlayingSessionStateChanged(SteamUser.PlayingSessionStateCallback cb)
    {
        IsPlayingBlocked = cb.PlayingBlocked;
        BlockingAppId = cb.PlayingBlocked ? cb.PlayingAppID : null;
        PlayingSessionStateChanged?.Invoke(cb);
    }

    private void OnPersonaState(SteamFriends.PersonaStateCallback cb)
    {
        if (cb.FriendID == SteamId)
        {
            PersonaName = cb.Name;
            AvatarHash = cb.AvatarHash;
        }
    }

    public void Dispose()
    {
        _callbackCts?.Cancel();
        _callbackCts?.Dispose();
        _client.Disconnect();
    }
}
