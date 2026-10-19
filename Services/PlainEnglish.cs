namespace RadarV2.Services;

/// <summary>
/// One shared instruction appended to every Radar prompt that produces text a user reads.
///
/// The reviewer's first and loudest complaint was "all these are just too much big words... it's
/// not comprehensible". Radar's audience is working professionals and students across Africa, many
/// reading in a second language. Sounding impressive is worth nothing if the reader has to decode
/// it, and "Strategic Decision Action - Six-Part Standard" helped no one.
///
/// Kept in one place so it cannot drift between features.
/// </summary>
public static class PlainEnglish
{
    public const string Rule = """

        HOW TO WRITE:
        Write in very simple English. Assume the reader is smart but busy, and may be reading in a
        second language.
        - Short sentences. One idea per sentence.
        - Use everyday words. Say "use" not "utilise", "help" not "facilitate", "start" not
          "commence", "about" not "regarding", "so" not "thereby".
        - No corporate or academic filler: no "leverage", "synergy", "paradigm", "robust framework",
          "strategic imperative", "holistic", "bespoke", "landscape", "ecosystem" as padding.
        - Explain a technical term the first time you use it, in brackets, in plain words.
        - Prefer the active voice: "you can build this" rather than "this can be built".
        - Never use a long word where a short one does the job.
        - Do not pad to hit a length. If you have said the useful thing, stop.
        """;

    /// <summary>Appends the rule to a system prompt.</summary>
    public static string Apply(string systemPrompt) => systemPrompt + Rule;
}
