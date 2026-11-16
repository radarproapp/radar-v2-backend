using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Catching the variable that is set, visible, and never read.
/// </summary>
/// <remarks>
/// ASP.NET Core maps a double underscore to the ":" separator, so Section__Key becomes
/// Section:Key and anything else becomes a flat key nobody looks for. The failure is always the
/// same and always quiet: the dashboard shows the variable set, the feature is off, and nothing
/// anywhere says why.
///
/// The check existed and still missed a real one. It built two spellings - "Section_Key" and
/// "SectionKey" - and looked each up exactly, so "Groq_whisper_ApiKey" went straight past it:
/// an extra underscore inside the section name and different casing. Casing is not cosmetic
/// here either, because Railway runs Linux, where environment variables are case-sensitive.
/// </remarks>
public class ConfigurationCheckTests : IDisposable
{
    private readonly List<string> _set = [];

    private void Env(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (value is not null) _set.Add(name);
    }

    public void Dispose()
    {
        foreach (var name in _set) Environment.SetEnvironmentVariable(name, null);
        GC.SuppressFinalize(this);
    }

    /// <summary>Captures what was logged, so the warning itself can be asserted.</summary>
    private sealed class Captured : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => true;

        public void Log<TState>(
            LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
        {
            if (level >= LogLevel.Warning) Warnings.Add(format(state, error));
        }
    }

    private static Captured Run()
    {
        var log = new Captured();

        // Empty configuration: nothing resolves, which is the state the check runs in.
        ConfigurationCheck.WarnAboutMisnamedKeys(
            new ConfigurationBuilder().Build(), log);

        return log;
    }

    /// <summary>
    /// The spelling that got through, now caught.
    /// </summary>
    /// <remarks>
    /// Neither "GroqWhisper_ApiKey" nor "GroqWhisperApiKey" - it has an underscore inside the
    /// section name and a lowercase w. Obviously the same intent to a person, and invisible to a
    /// check comparing two guessed spellings.
    /// </remarks>
    [Theory]
    [InlineData("Groq_whisper_ApiKey")]
    [InlineData("GroqWhisper_ApiKey")]
    [InlineData("GROQWHISPER__APIKEY")]
    [InlineData("groqwhisperapikey")]
    [InlineData("GroqWhisperApiKey")]
    public void AMisspeltVariableIsNamedInTheWarning(string wrong)
    {
        Env(wrong, "some-secret-value");

        var warning = Assert.Single(Run().Warnings, line => line.Contains("GroqWhisper:ApiKey"));

        Assert.Contains(wrong, warning);
        Assert.Contains("GroqWhisper__ApiKey", warning);

        // The value is the one thing that must never reach a log.
        Assert.DoesNotContain("some-secret-value", warning);
    }

    [Fact]
    public void TheCorrectlySpeltVariableIsNotReportedAsAMistake()
    {
        Env("GroqWhisper__ApiKey", "a-value");

        Assert.DoesNotContain(Run().Warnings, line => line.Contains("GroqWhisper"));
    }

    [Fact]
    public void AnUnrelatedVariableIsNotMistakenForOne()
    {
        Env("GroqWhisperSomethingElse", "a-value");
        Env("PATH_TO_NOWHERE", "a-value");

        Assert.DoesNotContain(Run().Warnings, line => line.Contains("GroqWhisper:ApiKey"));
    }

    /// <summary>Nothing set is nothing to say, rather than a warning per key.</summary>
    [Fact]
    public void NothingSetProducesNoWarnings() => Assert.Empty(Run().Warnings);

    /// <summary>
    /// Every credential the code reads is covered.
    /// </summary>
    /// <remarks>
    /// The list had three of them. The check is only worth having if a new integration lands on
    /// it, and TalentApply and GroqWhisper both shipped without - each one a key that could be
    /// set wrong on the host with nothing to say so.
    /// </remarks>
    [Theory]
    [InlineData("TalentApply:ApiKey")]
    [InlineData("GroqWhisper:ApiKey")]
    [InlineData("Taddy:ApiKey")]
    [InlineData("PodcastIndex:ApiKey")]
    [InlineData("PodcastIndex:ApiSecret")]
    [InlineData("Mediastack:ApiKey")]
    [InlineData("SendByte:ApiKey")]
    [InlineData("YouTube:ApiKey")]
    [InlineData("Jobs:CronKey")]
    [InlineData("MongoDB:ConnectionString")]
    public void EveryCredentialTheCodeReadsIsWatched(string key)
    {
        Env(key.Replace(":", "_"), "a-value");

        Assert.Contains(Run().Warnings, line => line.Contains(key));
    }
}
