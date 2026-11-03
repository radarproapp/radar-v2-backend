using MongoDB.Bson.Serialization.Attributes;

namespace RadarV2.Models;

/// <summary>
/// Admin access somebody handed to somebody else.
/// </summary>
/// <remarks>
/// Keyed on an email address rather than on an account, because the person being invited often
/// does not have one yet. The grant sits here until they sign up or sign in, and the role is
/// resolved from their address at that moment - which is also what makes it work for somebody who
/// already has an ordinary Radar account.
///
/// This is the half that can be changed from inside the product. The configured lists in
/// <see cref="Services.AdminRoles"/> are the other half and cannot: they are the way back in if
/// this collection is ever wrong, so nothing in the admin area is allowed to edit them.
/// </remarks>
public class AdminGrant
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The address this was granted to, lower-cased.</summary>
    /// <remarks>
    /// Normalised on the way in, because it is matched against the address somebody types at
    /// sign-in and a unique index does not fold case for you. Two rows differing only in
    /// capitals would be two grants for one person, and revoking one would leave the other.
    /// </remarks>
    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = Services.AdminRoles.PlatformAdmin;

    /// <summary>Who granted it, by address.</summary>
    /// <remarks>
    /// Stored rather than joined, so the record still names them if that account is later
    /// deleted or itself loses access. The question this answers - "who let them in" - is asked
    /// precisely when something has gone wrong, which is the worst moment to depend on another
    /// row still being there.
    /// </remarks>
    public string GrantedByEmail { get; set; } = string.Empty;

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When it was taken back, or null while it stands.
    /// </summary>
    /// <remarks>
    /// Revoking keeps the row rather than deleting it. Who had access and who took it away is
    /// the part worth having later, and a deleted row answers neither.
    /// </remarks>
    [BsonIgnoreIfNull]
    public DateTime? RevokedAt { get; set; }

    [BsonIgnoreIfNull]
    public string? RevokedByEmail { get; set; }

    /// <summary>Whether this grant currently gives anybody anything.</summary>
    [BsonIgnore]
    public bool IsActive => RevokedAt is null;
}
