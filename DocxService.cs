using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AudioBookBot.Services;

public static class DocxService
{
    public static string ExtractText(string docxPath)
    {
        var sb = new System.Text.StringBuilder();

        using var doc = WordprocessingDocument.Open(docxPath, false);
        var body = doc.MainDocumentPart?.Document?.Body;

        if (body == null) return string.Empty;

        foreach (var para in body.Elements<Paragraph>())
        {
            var paraText = para.InnerText;
            if (!string.IsNullOrWhiteSpace(paraText))
            {
                sb.AppendLine(paraText);
            }
        }

        return sb.ToString().Trim();
    }
}
