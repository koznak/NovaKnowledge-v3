using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using NovaKnowledge.Application.Interfaces;
using UglyToad.PdfPig;

namespace NovaKnowledge.Infrastructure.Extraction;

public class TextExtractionService(ILogger<TextExtractionService> logger) : ITextExtractionService
{
    public async Task<string> ExtractTextAsync(Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        var normalizedContentType = contentType.ToLowerInvariant();
        logger.LogDebug("Extracting text for content type {ContentType}", normalizedContentType);

        var text = normalizedContentType switch
        {
            "application/pdf" => ExtractPdf(stream),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ExtractDocx(stream),
            "text/plain" or "text/markdown" => await new StreamReader(stream).ReadToEndAsync(cancellationToken),
            _ => throw new NotSupportedException($"Content type not supported: {contentType}")
        };

        logger.LogDebug("Extracted {CharacterCount} characters for content type {ContentType}", text.Length, normalizedContentType);
        return text;
    }

    private string ExtractPdf(Stream stream)
    {
        using var document = PdfDocument.Open(stream);
        var sb = new System.Text.StringBuilder();
        foreach (var page in document.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    private static string ExtractDocx(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document.Body;
        if (body == null) return string.Empty;
        return string.Join("\n", body.Descendants<Text>().Select(t => t.Text));
    }
}
