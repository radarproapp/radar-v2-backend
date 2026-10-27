using System.Text.RegularExpressions;

namespace RadarV2.Tests;

/// <summary>
/// Every paper Radar shows must come from somewhere a reader can check.
///
/// A 353-line file of hand-written papers used to be served ahead of the live OpenAlex results,
/// describing itself as "high-impact, peer-reviewed ... with real DOIs, verified publisher
/// sources". Six of its eight DOIs resolved to nothing at doi.org; the authors, journals,
/// methodologies and statistics were written by hand. It sat behind a citation endpoint, so the
/// product would format an invented paper into a reference list on request.
///
/// This fails the build if a research path grows a hard-coded paper list again. It is a weaker
/// guarantee than verifying provenance at runtime — but the thing that failed here was nobody
/// looking at the file for months, and a test looks every time.
/// </summary>
public class ResearchProvenanceTests
{
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

    private static IEnumerable<FileInfo> SourceFiles() =>
        RepositoryRoot()
            .GetFiles("*.cs", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// A DOI in a string literal is a paper the code is asserting exists. Research results have
    /// to arrive from OpenAlex, which is checkable; a literal cannot be.
    /// </summary>
    [Fact]
    public void NoSourceFileHardcodesADoi()
    {
        var doi = new Regex(@"""10\.\d{4,9}/", RegexOptions.Compiled);
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file.FullName);
            if (doi.IsMatch(text)) offenders.Add(file.Name);
        }

        Assert.Empty(offenders);
    }

    // Named directly, because the deletion is the fix and a reintroduction under the old name is
    // the most likely way it comes back.
    [Fact]
    public void TheHandWrittenPaperCatalogueIsGone()
    {
        var revived = SourceFiles().Any(f =>
            f.Name.Equals("ResearchPaperCatalog.cs", StringComparison.OrdinalIgnoreCase));

        Assert.False(revived, "ResearchPaperCatalog served invented papers with invented DOIs.");
    }

    /// <summary>
    /// A deadline is a fact about the world, so it cannot be computed from the clock.
    ///
    /// Sixteen seeded opportunities carried <c>Deadline = DateTime.UtcNow.AddDays(n)</c>. Eight of
    /// them were real programmes — Chevening, Mastercard Foundation Scholars, Y Combinator — shown
    /// with a closing date that was always a fortnight away, whenever the page was loaded. A
    /// learner planning around that misses the real one.
    /// </summary>
    [Fact]
    public void NoProductionCodeDerivesADeadlineFromTheClock()
    {
        var rolling = new Regex(@"Deadline\s*=\s*DateTime\.UtcNow", RegexOptions.Compiled);

        var offenders = SourceFiles()
            // Tests construct deadlines relative to now on purpose: "closes in four days" is the
            // property under test, not a claim shown to anybody.
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}"))
            .Where(f => rolling.IsMatch(File.ReadAllText(f.FullName)))
            .Select(f => f.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Opportunities come from feeds, never from a literal in the source.
    /// </summary>
    /// <remarks>
    /// Sixteen were hand-written into MongoOpportunityService: a "Google Cybersecurity
    /// Professional Fellowship" from "Google Security Team &amp; Africa Digital Hub" paying USD
    /// 2,500 a month, a "DeepMind Africa AI &amp; Machine Learning Scholarship", and junior
    /// openings invented at Paystack, Cloudflare and Stripe. Radar told students in Africa to
    /// apply for them.
    ///
    /// Deleting them from the source was not the end of it — six were still being served from the
    /// database a day later, which no test can catch. What this does catch is a new one being
    /// written, which is the only part a test can own.
    /// </remarks>
    [Fact]
    public void NoOpportunityIsHardcoded()
    {
        var offenders = SourceFiles()
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}"))
            // A quoted organisation name is the signature. The legitimate ingestion path assigns
            // Organisation from InferOrganisation(title, feed.Name) — derived from the feed it
            // came out of — so it is the literal, not the assignment, that makes a claim nobody
            // can check. My first attempt at this test matched the assignment and flagged the
            // ingestion path, which is the opposite of the point.
            .Where(f => new Regex(@"Organisation\s*=\s*""", RegexOptions.Compiled)
                .IsMatch(File.ReadAllText(f.FullName)))
            .Select(f => f.Name)
            .ToList();

        Assert.Empty(offenders);
    }
}
