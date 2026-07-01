using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Playwright;
using CloakBrowser;
using CloakBrowser.Human;

namespace SocialCrawler.Services;

public static class BrowserLauncher
{
    public static async Task<IBrowser> LaunchAsync(IPlaywright playwright, bool headless, string? channel = null, string[]? extraArgs = null)
    {
        Console.WriteLine($"[BrowserLauncher] Launching CloakBrowser (stealth Chromium) with Headless={headless}");

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

        var launchOpts = new LaunchOptions
        {
            Headless = headless,
            Humanize = true, // Enable the transparent humanize layer!
            HumanPreset = HumanPreset.Default,
            Args = defaultArgs.Distinct().ToList()
        };

        var customPath = Environment.GetEnvironmentVariable("BROWSER_EXECUTABLE_PATH");
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            Environment.SetEnvironmentVariable("CLOAKBROWSER_BINARY_PATH", customPath);
            Console.WriteLine($"[BrowserLauncher] Using custom binary path: {customPath}");
        }

        // Launch the stealth CloakBrowser
        var handle = await CloakLauncher.LaunchAsync(launchOpts);
        
        // Return the wrapped IBrowser instance
        return handle.Browser;
    }
}
