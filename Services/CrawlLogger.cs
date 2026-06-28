using System.Text;

namespace SocialCrawler.Services;

public static class CrawlLogger
{
    public static async Task LogErrorAsync(string target, string errorMessage)
    {
        try
        {
            var outputDir = Path.Combine("output", "tiktok", "errors");
            Directory.CreateDirectory(outputDir);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var safeTarget = SanitizeFileName(target);
            await File.WriteAllTextAsync(
                Path.Combine(outputDir, $"{timestamp}_{safeTarget}_error.txt"),
                errorMessage, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] LogErrorAsync failed: {ex.Message}");
        }
    }

    private static string SanitizeFileName(string input)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", input.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }
}