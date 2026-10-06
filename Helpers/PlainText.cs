using System.Text.RegularExpressions;

namespace RadarV2.Helpers;

/// <summary>
/// Strips markdown from model output so stored/returned answers are clean prose. The model doesn't
/// reliably obey "no markdown" instructions, so formatting is normalised here as well.
/// </summary>
public static class PlainText
{
    public static string Clean(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var text = input.Replace("\r", "");
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");                          // [label](url)
        text = Regex.Replace(text, @"\*\*(.*?)\*\*", "$1", RegexOptions.Singleline);         // **bold**
        text = Regex.Replace(text, @"__([^_]+)__", "$1");                                    // __bold__
        text = Regex.Replace(text, @"`{1,3}([^`]*?)`{1,3}", "$1", RegexOptions.Singleline);  // `code`
        text = Regex.Replace(text, @"^[ \t]{0,3}#{1,6}[ \t]*", "", RegexOptions.Multiline);  // ### Heading
        text = Regex.Replace(text, @"^[ \t]*[-*+][ \t]+", "• ", RegexOptions.Multiline);     // - item -> • item
        text = Regex.Replace(text, @"[*#_`]", "");                                           // stray symbols
        text = Regex.Replace(text, @"[ \t]+$", "", RegexOptions.Multiline);                  // trailing spaces
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}
