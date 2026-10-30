namespace RadarV2.Tests.UseCases;

/// <summary>
/// Runs a batch of use cases and prints what each one saw.
/// </summary>
/// <remarks>
/// Not a pass/fail gate on the build. A use case failing is a finding to be read and fixed, not
/// a broken test - and a red suite overnight would stop every other test from being run. The
/// batch number comes from the RADAR_USE_CASE_BATCH environment variable so one can be run on
/// its own; with none set it runs the first.
/// </remarks>
public class BatchRunTests
{
    [Fact]
    public void RunTheBatch()
    {
        var batch = int.TryParse(Environment.GetEnvironmentVariable("RADAR_USE_CASE_BATCH"), out var n) ? n : 1;
        var outcomes = UseCaseRunner.RunAll(UseCaseLibrary.Batch(batch));

        var failed = outcomes.Where(o => !o.Passed).ToList();

        var people = UseCaseLibrary.Batch(batch).Count;

        Console.WriteLine($"=== batch {batch}: {people} people, {outcomes.Count} checks, {outcomes.Count - failed.Count} passed, {failed.Count} failed ===");

        // Failures first and in full, then the roll-up. A report read at three in the morning
        // should put the findings where they are seen.
        foreach (var outcome in failed) Console.WriteLine(outcome.Report);

        Console.WriteLine("--- by feature ---");
        foreach (var byFeature in outcomes.GroupBy(o => o.Feature).OrderBy(g => g.Key.ToString()))
        {
            var bad = byFeature.Count(o => !o.Passed);
            Console.WriteLine($"{byFeature.Key,-18} {byFeature.Count() - bad}/{byFeature.Count()} passed{(bad > 0 ? $"  ({bad} FAILED)" : "")}");
        }

        Assert.NotEmpty(outcomes);
    }
}
