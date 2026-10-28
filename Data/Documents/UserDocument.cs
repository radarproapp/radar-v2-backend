namespace RadarV2.Data.Documents;

public class UserDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string Role { get; set; } = "User";

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
