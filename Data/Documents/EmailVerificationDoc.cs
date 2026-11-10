namespace RadarV2.Data.Documents;

/// <summary>
/// An outstanding request to prove an address is real.
/// </summary>
/// <remarks>
/// Shaped like <see cref="PasswordResetDoc"/>: only the hash of the token is kept, it works once,
/// and it expires. The token exists in the person's inbox and nowhere else.
///
/// This exists because of a prize. A referral count is only worth what the accounts behind it are
/// worth, and nothing stopped somebody creating thirty accounts on addresses they invented. The
/// one cheap thing that does stop it is asking the address to answer.
/// </remarks>
public class EmailVerificationDoc
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
