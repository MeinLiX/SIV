using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;
using SIV.Application.Interfaces;
using SIV.Domain.Enums;

namespace SIV.UI.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private readonly Action _onLoginSuccess;
    private readonly Action? _onCancel;
    private CancellationTokenSource? _qrCts;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _twoFactorCode = string.Empty;

    [ObservableProperty]
    private bool _isTwoFactorRequired;

    [ObservableProperty]
    private bool _isQrMode;

    [ObservableProperty]
    private byte[]? _qrCodePngBytes;

    [ObservableProperty]
    private bool _isReAuth;

    [ObservableProperty]
    private bool _canCancel;

    public override string Title => "Login";

    public LoginViewModel(IAuthService auth, Action onLoginSuccess, Action? onCancel = null, string? prefillUsername = null)
    {
        _auth = auth;
        _onLoginSuccess = onLoginSuccess;
        _onCancel = onCancel;

        if (prefillUsername is not null)
        {
            Username = prefillUsername;
            IsReAuth = true;
        }

        CanCancel = onCancel is not null;
    }

    [RelayCommand]
    private async Task LoginAsync(CancellationToken ct)
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Username is required";
            return;
        }
        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Password is required";
            return;
        }

        IsBusy = true;
        StatusText = "Connecting to Steam...";

        try
        {
            var code = string.IsNullOrWhiteSpace(TwoFactorCode) ? null : TwoFactorCode;
            var result = await _auth.LoginAsync(Username, Password, code, ct);

            switch (result.Type)
            {
                case AuthResultType.Success:
                    StatusText = "Logged in successfully";
                    _onLoginSuccess();
                    break;
                case AuthResultType.SteamGuardRequired:
                case AuthResultType.MobileAuthRequired:
                    IsTwoFactorRequired = true;
                    StatusText = result.Message;
                    break;
                default:
                    ErrorMessage = result.Message ?? "Login failed";
                    StatusText = null;
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Operation cancelled";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusText = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoginViaQrAsync(CancellationToken ct)
    {
        ErrorMessage = null;
        _qrCts?.Cancel();
        _qrCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedCt = _qrCts.Token;

        IsQrMode = true;
        IsBusy = true;
        StatusText = "Scan QR code with Steam Mobile App...";

        try
        {
            var result = await _auth.LoginViaQrAsync(url =>
            {
                QrCodePngBytes = GenerateQrPng(url);
            }, linkedCt);

            if (result.Type == AuthResultType.Success)
            {
                StatusText = "Logged in successfully";
                _onLoginSuccess();
            }
            else
            {
                ErrorMessage = result.Message ?? "QR login failed";
                StatusText = null;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusText = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelQr()
    {
        _qrCts?.Cancel();
        IsQrMode = false;
        QrCodePngBytes = null;
        StatusText = null;
    }

    [RelayCommand]
    private void CancelLogin()
    {
        _onCancel?.Invoke();
    }

    private static byte[] GenerateQrPng(string url)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(8);
    }
}
