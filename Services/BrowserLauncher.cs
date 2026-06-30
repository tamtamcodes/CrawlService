using Microsoft.Playwright;

namespace SocialCrawler.Services;

public static class BrowserLauncher
{
    public static Task<IBrowser> LaunchAsync(IPlaywright playwright, bool headless, string? channel = null, string[]? extraArgs = null)
    {
        var options = new BrowserTypeLaunchOptions
        {
            Headless = headless
        };

        var customPath = Environment.GetEnvironmentVariable("BROWSER_EXECUTABLE_PATH");
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            options.ExecutablePath = customPath;
        }
        else if (!string.IsNullOrWhiteSpace(channel))
        {
            options.Channel = channel;
        }

        var defaultArgs = new List<string>
        {
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-gpu",
            "--disable-dev-shm-usage"
        };

        if (extraArgs != null)
        {
            defaultArgs.AddRange(extraArgs);
        }

        options.Args = defaultArgs.Distinct();

        return playwright.Chromium.LaunchAsync(options);
    }
}
