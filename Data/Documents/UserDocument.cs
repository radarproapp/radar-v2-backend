using MongoDB.Bson.Serialization.Attributes;

namespace RadarV2.Data.Documents;

public class UserDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>The profile this account reads Radar as, or empty for an admin account.</summary>
    public string ProfileId { get; set; } = string.Empty;

    public string Role { get; set; } = "User";

    /// <summary>
    /// What kind of account this is: absent for an ordinary one, "Admin" for an admin login.
    /// </summary>
    /// <remarks>
    /// One person can hold both. Running Radar and using Radar are different jobs, and doing the
    /// first should not mean the second is one password away - an admin session that is also a
    /// logged-in reader is a session where the wrong tab can do real damage.
    ///
    /// Absent rather than "User", deliberately. Every row written before this has no such field,
    /// and the unique index is on (Email, Kind): a missing field indexes as null, so an existing
    /// account and a new admin account for the same address are two distinct keys with no
    /// backfill and no migration. Writing "User" into new rows would instead make
    /// <c>{ada@x.com, null}</c> and <c>{ada@x.com, "User"}</c> distinct too, which would quietly
    /// allow two ordinary accounts on one address.
    /// </remarks>
    [BsonIgnoreIfNull]
    public string? Kind { get; set; }

    /// <summary>The value <see cref="Kind"/> carries for an admin login.</summary>
    public const string AdminKind = "Admin";

    /// <summary>Whether this is the separate admin login rather than somebody's ordinary account.</summary>
    [BsonIgnore]
    public bool IsAdminAccount => Kind == AdminKind;

    /// <summary>
    /// Bumped whenever every existing session should stop working.
    /// </summary>
    /// <remarks>
    /// A JWT is valid until it expires, and Radar's last 30 days. So resetting a password did not
    /// remove whoever was already in — which is the one thing somebody resetting a compromised
    /// account is trying to do, and they would reasonably assume it had worked.
    ///
    /// The token carries the version it was issued at; a request whose version is behind the
    /// stored one is refused. Bumping this is therefore "sign out everywhere", and a reset does
    /// it.
    ///
    /// Zero for every account that existed before this, and tokens issued before it carry no
    /// version claim — both read as 0, so nobody is signed out by the deploy itself.
    /// </remarks>
    public int TokenVersion { get; set; }

    /// <summary>
    /// When this address answered, or null if it never has.
    /// </summary>
    /// <remarks>
    /// Null for every account that existed before this, which is honest: nobody asked them, so
    /// nothing was proved. Absent rather than false, so the field says "not established" rather
    /// than "established as untrue".
    ///
    /// It gates nothing about using Radar. Somebody who has not opened the email still reads
    /// their feed - making a product unusable until an email arrives loses the people whose mail
    /// is slow for reasons that are not their fault. What it gates is the referral counts, where
    /// an unanswered address is the whole of how somebody would manufacture a number.
    /// </remarks>
    [BsonIgnoreIfNull]
    public DateTime? EmailVerifiedAt { get; set; }

    /// <summary>Whether this address has answered.</summary>
    [BsonIgnore]
    public bool IsEmailVerified => EmailVerifiedAt is not null;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
