using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using UglyToad.PdfPig;

namespace RadarV2.Helpers;

public static class DocumentTextExtractor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".md", ".markdown", ".csv", ".tsv", ".json",
        ".html", ".htm", ".xml", ".log", ".rtf", ".pdf", ".docx"
    };

    public static bool IsSupported(string fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext))
            return true;

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
                contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
                contentType.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<string> ExtractTextAsync(IFormFile file, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (ext == ".pdf")
        {
            return ExtractFromPdf(file);
        }

        if (ext == ".docx")
        {
            return await ExtractFromDocxAsync(file, ct);
        }

        if (ext is ".html" or ".htm")
        {
            using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
            var html = await reader.ReadToEndAsync(ct);
            return StripHtml(html);
        }

        // Standard text, markdown, csv, json, log, xml
        using var textReader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await textReader.ReadToEndAsync(ct);
    }

    private static string ExtractFromPdf(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var document = PdfDocument.Open(stream);
        var sb = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text.Trim());
                sb.AppendLine();
            }
        }

        return sb.ToString().Trim();
    }

    private static async Task<string> ExtractFromDocxAsync(IFormFile file, CancellationToken ct)
    {
        using var stream = file.OpenReadStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml");
        if (entry is null) return string.Empty;

        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        var xml = await reader.ReadToEndAsync(ct);

        // Replace paragraph closures with line breaks to retain structure
        xml = Regex.Replace(xml, @"</w:p>", "\n");
        // Strip XML tags
        var plain = Regex.Replace(xml, @"<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(plain).Trim();
    }

    public static string StripHtml(string html)
    {
        html = Regex.Replace(html, "<script[^>]*>.*?</script>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<style[^>]*>.*?</style>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<[^>]+>", " ");
        html = System.Net.WebUtility.HtmlDecode(html);
        return Regex.Replace(html, @"\s+", " ").Trim();
    }
}
