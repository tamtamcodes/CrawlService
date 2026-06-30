using System.Text;
using System.Text.Json;
using SocialCrawler.Models;

namespace SocialCrawler.Services;

public static class CrawlLogger
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task LogErrorAsync(string target, string errorMessage)
    {
        // try
        // {
        //     var outputDir = Path.Combine("output", "tiktok", "errors");
        //     Directory.CreateDirectory(outputDir);
        //     var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        //     var safeTarget = SanitizeFileName(target);
        //     await File.WriteAllTextAsync(
        //         Path.Combine(outputDir, $"{timestamp}_{safeTarget}_error.txt"),
        //         errorMessage, Encoding.UTF8);
        // }
        // catch (Exception ex)
        // {
        //     Console.WriteLine($"[ERROR] LogErrorAsync failed: {ex.Message}");
        // }
        await Task.CompletedTask;
    }

    public static async Task LogRawJsonAsync(string platform, string target, string postId, JsonElement storyJson)
    {
        // try
        // {
        //     var safeTarget = SanitizeFileName(target);
        //     var outputDir = Path.Combine("output", platform, "raw", safeTarget);
        //     Directory.CreateDirectory(outputDir);

        //     var formatted = JsonSerializer.Serialize(storyJson, PrettyJsonOptions);
        //     await File.WriteAllTextAsync(
        //         Path.Combine(outputDir, $"{postId}.json"),
        //         formatted, Encoding.UTF8);
        // }
        // catch (Exception ex)
        // {
        //     Console.WriteLine($"[ERROR] LogRawJsonAsync failed: {ex.Message}");
        // }

        await Task.CompletedTask;
    }

    public static Task ClearTargetOutputAsync(string platform, string target)
    {
        // try
        // {
        //     var safeTarget = SanitizeFileName(target);
        //     foreach (var kind in new[] { "raw", "transcripts", "parsed" })
        //     {
        //         var outputDir = Path.Combine("output", platform, kind, safeTarget);
        //         if (Directory.Exists(outputDir))
        //             Directory.Delete(outputDir, recursive: true);
        //         Directory.CreateDirectory(outputDir);
        //     }
        // }
        // catch (Exception ex)
        // {
        //     Console.WriteLine($"[ERROR] ClearTargetOutputAsync failed: {ex.Message}");
        // }

        return Task.CompletedTask;
    }

    public static async Task LogTranscriptAsync(string platform, string target, string postId, object transcript)
    {
        // try
        // {
        //     var safeTarget = SanitizeFileName(target);
        //     var outputDir = Path.Combine("output", platform, "transcripts", safeTarget);
        //     Directory.CreateDirectory(outputDir);

        //     var formatted = JsonSerializer.Serialize(transcript, PrettyJsonOptions);
        //     await File.WriteAllTextAsync(
        //         Path.Combine(outputDir, $"{postId}.json"),
        //         formatted, Encoding.UTF8);
        // }
        // catch (Exception ex)
        // {
        //     Console.WriteLine($"[ERROR] LogTranscriptAsync failed: {ex.Message}");
        // }
        await Task.CompletedTask;
    }

    public static async Task LogParsedPostAsync(string platform, string target, PostData post, string? postId = null)
    {
        // try
        // {
        //     var safeTarget = SanitizeFileName(target);
        //     var outputDir = Path.Combine("output", platform, "parsed", safeTarget);
        //     Directory.CreateDirectory(outputDir);

        //     // Extract a filename-friendly identifier from the post URL, fallback to postId
        //     var fileName = SanitizeFileName(post.PostUrl?.Trim('/').Split('/').LastOrDefault() ?? "");
        //     if (string.IsNullOrEmpty(fileName)) fileName = SanitizeFileName(postId ?? "");
        //     if (string.IsNullOrEmpty(fileName)) fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

        //     var formatted = JsonSerializer.Serialize(post, PrettyJsonOptions);
        //     await File.WriteAllTextAsync(
        //         Path.Combine(outputDir, $"{fileName}.json"),
        //         formatted, Encoding.UTF8);
        // }
        // catch (Exception ex)
        // {
        //     Console.WriteLine($"[ERROR] LogParsedPostAsync failed: {ex.Message}");
        // }
        await Task.CompletedTask;
    }

    private static string SanitizeFileName(string input)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", input.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }
}