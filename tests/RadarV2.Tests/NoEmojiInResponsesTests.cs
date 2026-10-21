using System.Text.RegularExpressions;

namespace RadarV2.Tests;

/// <summary>
/// The API must not put emoji in strings the interface renders.
///
/// Opportunity labels used to arrive as "🔴 Urgent" and "🟢 Open". That is a presentation decision
/// taken on the server: it picks a glyph that renders differently on every platform, cannot be
/// recoloured for a theme, and is read aloud as "large red circle" before the word that carries
/// the meaning. Worse, no amount of work in the frontend can remove it — the client would have to
/// strip characters out of data to undo a choice the server made.
///
/// The server says what the state is; the client draws it. This fails the build if that slips.
/// </summary>
public class NoEmojiInResponsesTests
{
    private static readonly Regex Emoji = new(
        @"[\uD83C-􏰀-\uDFFF]|[☀-⛿]|[✀-➿]|[⬀-⯿]",
        RegexOptions.Compiled);

    /// <summary>
    /// Walks up from the test binary to the repository root, which is wherever the project file
    /// sits. Avoids hardcoding a path that breaks the moment the layout changes or CI checks out
    /// somewhere else.
    /// </summary>
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
    public void No_source_file_puts_an_emoji_in_a_string_literal()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (var file in root.GetFiles("*.cs", SearchOption.AllDirectories))
        {
            var path = file.FullName.Replace('\\', '/');
            if (path.Contains("/bin/") || path.Contains("/obj/") || path.Contains("/tests/")) continue;

            var lines = File.ReadAllLines(file.FullName);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Comments explain what was removed and legitimately name the characters.
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*")) continue;

                if (Emoji.IsMatch(line))
                {
                    offenders.Add($"{Path.GetFileName(file.FullName)}:{i + 1}  {line.Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Return a value the client can draw, not a glyph. Offending lines:\n" + string.Join("\n", offenders));
    }
}
