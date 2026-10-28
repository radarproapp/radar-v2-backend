namespace RadarV2.Data.Documents;

/// <summary>
/// An outstanding password reset.
/// </summary>
/// <remarks>
/// Only the hash of the token is kept. A database that leaks should not also hand over a set of
/// working reset links, and nothing in the product ever needs to read the token back — it arrives
/// from the user, gets hashed, and is compared.
/// </remarks>
public class PasswordResetDoc
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
