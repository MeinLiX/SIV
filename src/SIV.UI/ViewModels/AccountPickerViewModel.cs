using CommunityToolkit.Mvvm.Input;
using SIV.Application.DTOs;
using SIV.Application.Interfaces;
using System.Collections.ObjectModel;

namespace SIV.UI.ViewModels;

public partial class AccountPickerViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private readonly Action _onAddAccount;
    private readonly Action<string> _onAccountExpired;
    private readonly Action _onLoginSuccess;

    public ObservableCollection<SavedAccount> Accounts { get; } = [];

    public override string Title => "Select Account";

    public AccountPickerViewModel(
        IAuthService auth,
        Action onAddAccount,
        Action<string> onAccountExpired,
        Action onLoginSuccess)
    {
        _auth = auth;
        _onAddAccount = onAddAccount;
        _onAccountExpired = onAccountExpired;
        _onLoginSuccess = onLoginSuccess;
        _ = LoadAccountsAsync();
    }

    private async Task LoadAccountsAsync()
    {
        var accounts = await _auth.GetSavedAccountsAsync();
        Accounts.Clear();
        foreach (var account in accounts)
            Accounts.Add(account);
    }

    [RelayCommand]
    private async Task SelectAccountAsync(SavedAccount account, CancellationToken ct)
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusText = $"Connecting as {account.DisplayName}...";

        try
        {
            var result = await _auth.RestoreAccountSessionAsync(account.AccountName, ct);

            if (result.Type == Domain.Enums.AuthResultType.Success)
            {
                _onLoginSuccess();
            }
            else
            {
                // Session expired — redirect to login with this account
                StatusText = null;
                _onAccountExpired(account.AccountName);
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
            // Treat any connection error as potential expired session
            _onAccountExpired(account.AccountName);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveAccountAsync(SavedAccount account)
    {
        await _auth.RemoveAccountAsync(account.AccountName);
        Accounts.Remove(account);
    }

    [RelayCommand]
    private void AddAccount()
    {
        _onAddAccount();
    }
}
