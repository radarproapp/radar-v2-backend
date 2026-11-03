namespace RadarV2.Services;

/// <summary>
/// Who is an admin, decided in one place.
/// </summary>
/// <remarks>
/// Two lists of email addresses in configuration, and nothing else. There is no screen that
/// grants a role and no field anybody edits by hand: an account is an admin because its address
/// is in the deployment's configuration, and it stops being one when the address comes out.
///
/// That last half did not used to be true. The resolver ended with
/// <c>existingRole is "SuperAdmin" or "PlatformAdmin" ? existingRole : "User"</c>, so a role,
/// once granted, survived its address being removed from the configuration forever - the only
/// way to take it back was editing MongoDB by hand. A permission you can give and cannot take
/// away is not a permission system.
///
/// Static and shared, because <see cref="AuthService"/> decides what role to stamp on a token
/// and <see cref="SessionVersionGuard"/> decides whether to keep honouring one, and two copies
/// of the same rule drift. That is not hypothetical - the referral normalisers each claimed in a
/// comment to mirror the other and disagreed on query strings for a week.
/// </remarks>
public static class AdminRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string PlatformAdmin = "PlatformAdmin";
    public const string User = "User";

    private const string SuperAdminList = "Auth:SuperAdminEmails";
    private const string AdminList = "Auth:AdminEmails";

    /// <summary>Whether a role is one of the two that can reach the admin area.</summary>
    public static bool IsAdmin(string? role) => role is SuperAdmin or PlatformAdmin;

    /// <summary>
    /// The role this address is entitled to right now.
    /// </summary>
    /// <remarks>
    /// Super admin wins when an address somehow appears in both lists, because the alternative is
    /// that adding somebody to the wrong list quietly demotes them.
    /// </remarks>
    public static string For(IConfiguration config, string? email)
    {
        var normalised = email?.Trim();
        if (string.IsNullOrEmpty(normalised)) return User;

        if (Lists(config, SuperAdminList).Contains(normalised, StringComparer.OrdinalIgnoreCase)) return SuperAdmin;
        if (Lists(config, AdminList).Contains(normalised, StringComparer.OrdinalIgnoreCase)) return PlatformAdmin;

        return User;
    }

    /// <summary>
    /// Every address configured as an admin, and what it is entitled to.
    /// </summary>
    /// <remarks>
    /// For the admin team screen, which had no way to answer "who can get in here" - it listed
    /// four invented people. Read from configuration rather than from the accounts collection so
    /// it names an address that has been granted access but has never signed in, which is
    /// otherwise invisible until somebody uses it.
    /// </remarks>
    public static IEnumerable<(string Email, string Role)> Configured(IConfiguration config)
    {
        foreach (var email in Lists(config, SuperAdminList))
            yield return (email, SuperAdmin);

        // Skipped when the same address is already a super admin, so one person does not appear
        // twice holding two different roles.
        var supers = Lists(config, SuperAdminList).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var email in Lists(config, AdminList).Where(email => !supers.Contains(email)))
            yield return (email, PlatformAdmin);
    }

    private static string[] Lists(IConfiguration config, string key) =>
        [.. (config.GetSection(key).Get<string[]>() ?? [])
            .Select(entry => entry?.Trim() ?? string.Empty)
            .Where(entry => entry.Length > 0)];
}
