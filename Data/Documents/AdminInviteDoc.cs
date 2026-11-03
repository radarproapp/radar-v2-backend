using MongoDB.Bson.Serialization.Attributes;

namespace RadarV2.Data.Documents;

/// <summary>
/// An outstanding invitation to create an admin account.
/// </summary>
/// <remarks>
/// Shaped like <see cref="PasswordResetDoc"/> on purpose: only the hash of the token is kept, it
/// works once, and it expires. Anything that creates an account and sets a password is worth
/// treating as carefully as the flow that resets one.
/// </remarks>
public class AdminInviteDoc
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The address the invitation was sent to, lower-cased.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The role the account will hold once it exists.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Who invited them, for the record and for the message.</summary>
    public string InvitedByEmail { get; set; } = string.Empty;

    /// <summary>
    /// The hash of the token, never the token.
    /// </summary>
    /// <remarks>
    /// The same reasoning as a password: a leaked backup of this collection must not be a list of
    /// working invitations. The token exists only in the email.
    /// </remarks>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    /// <summary>When it was redeemed, or null while it is still open.</summary>
    /// <remarks>
    /// Kept rather than deleted, so the team screen can tell an invitation nobody has accepted
    /// from one that was accepted a month ago - two states that look identical if the row goes.
    /// </remarks>
    [BsonIgnoreIfNull]
    public DateTime? AcceptedAt { get; set; }
}
