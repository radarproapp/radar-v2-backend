using System.Text.RegularExpressions;

namespace RadarV2.Tests;

/// <summary>
/// A static catalogue cannot know what a reader has finished.
///
/// Two entries claimed it did: "recommended because you are currently learning network security
/// and have not yet completed the fundamentals of firewalls" was shown to every user who reached
/// that resource, including ones who had completed it, and ones who had never touched network
/// security at all.
///
/// The distinction that matters: a catalogue may describe the *resource* ("covers firewall
/// fundamentals, which the rest of network security builds on") because that is a fact about the
/// material. It may not describe the *reader*, because it has never met them. Personalisation
/// belongs where the user's record is actually in scope.
/// </summary>
public class CatalogueClaimsTests
{
    /// <summary>Phrases that assert something about the reader's own history or state.</summary>
    private static readonly Regex ClaimsAboutReader = new(
        @"\b(you are currently|you have not yet|you recently|you already|your current level|because you )",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WhyAssignment = new(
        @"(WhyThisResource|WhyAmILearningThis|WhyItMatters|WhyShown)\s*=\s*""([^""]*)""",
        RegexOptions.Compiled);

    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadarV2.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!;
    }

    [Fact]
    public void No_hardcoded_explanation_claims_to_know_what_the_reader_has_done()
    {
        var offenders = new List<string>();

        foreach (var file in RepositoryRoot().GetFiles("*.cs", SearchOption.AllDirectories))
        {
            var path = file.FullName.Replace('\\', '/');
            if (path.Contains("/bin/") || path.Contains("/obj/") || path.Contains("/tests/")) continue;

            var text = File.ReadAllText(file.FullName);
            foreach (Match match in WhyAssignment.Matches(text))
            {
                var sentence = match.Groups[2].Value;

                // An interpolated string is built at runtime from a real profile, so it is allowed
                // to address the reader. A plain literal cannot know anything about them.
                var literalStart = match.Index - 1;
                var isInterpolated = literalStart >= 0 && text[literalStart] == '$';
                if (isInterpolated) continue;

                if (ClaimsAboutReader.IsMatch(sentence))
                {
                    offenders.Add($"{Path.GetFileName(file.FullName)}: \"{sentence[..Math.Min(90, sentence.Length)]}\"");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A fixed string cannot know the reader. Describe the resource instead:\n" + string.Join("\n", offenders));
    }
}
