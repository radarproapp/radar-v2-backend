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

        Console.WriteLine($"=== batch {batch}: {outcomes.Count - failed.Count} of {outcomes.Count} passed ===");
        foreach (var outcome in outcomes) Console.WriteLine(outcome.Report);

        Assert.NotEmpty(outcomes);
    }
}
