namespace RadarV2.Services.Interfaces;

public class AuthResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string Role { get; set; } = "User";

    /// <summary>The version the issued token should carry. See UserDocument.TokenVersion.</summary>
    public int TokenVersion { get; set; }
}

public interface IAuthService
{
    /// <summary>
    /// Creates an account, attributing it to a referral code when one arrived with it.
    /// </summary>
    /// <remarks>
    /// Optional, so every existing caller is unchanged and an account created without one is
    /// simply unattributed - which is the honest state for somebody who found Radar on their
    /// own.
    /// </remarks>
    Task<AuthResult> RegisterAsync(string name, string email, string password, string? referralCode = null);
    /// <param name="adminAccount">
    /// Whether to sign in to the separate admin login rather than the ordinary account. The two
    /// are different rows on the same address and must not reach each other.
    /// </param>
    Task<AuthResult> LoginAsync(string email, string password, bool adminAccount = false);
}
