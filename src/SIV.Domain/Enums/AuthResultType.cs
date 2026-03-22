namespace SIV.Domain.Enums;

public enum AuthResultType
{
    Success,
    InvalidCredentials,
    SteamGuardRequired,
    MobileAuthRequired,
    RateLimited,
    Failed
}
