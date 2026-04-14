using UglyToad.PdfPig;

namespace AudioBookBot.Services;

public static class PdfService
{
    private const int PagesPerChunk = 5;

    /// Barcha sahifalardan matn olish
    public static string ExtractText(string pdfPath)
    {
        var sb = new System.Text.StringBuilder();
        using var document = PdfDocument.Open(pdfPath);

        foreach (var page in document.GetPages())
        {
            var pageText = page.Text;
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                sb.AppendLine(pageText);
                sb.AppendLine();
            }
        }

        return sb.ToString().Trim();
    }

    /// Sahifalar sonini olish
    public static int GetPageCount(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        return document.NumberOfPages;
    }

    /// PDF ni 50 sahifalik bo'laklarga bo'lib matn qaytarish
    /// [(chunkIndex, fromPage, toPage, text), ...]
    public static List<(int index, int from, int to, string text)> ExtractChunks(string pdfPath)
    {
        var result = new List<(int, int, int, string)>();
        using var document = PdfDocument.Open(pdfPath);

        var totalPages = document.NumberOfPages;
        var pages = document.GetPages().ToList();

        int chunkIndex = 1;
        for (int i = 0; i < totalPages; i += PagesPerChunk)
        {
            var fromPage = i + 1;
            var toPage   = Math.Min(i + PagesPerChunk, totalPages);

            var sb = new System.Text.StringBuilder();
            for (int p = i; p < toPage; p++)
            {
                var pageText = pages[p].Text;
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    sb.AppendLine(pageText);
                    sb.AppendLine();
                }
            }

            var text = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
                result.Add((chunkIndex, fromPage, toPage, text));

            chunkIndex++;
        }

        return result;
    }
}
