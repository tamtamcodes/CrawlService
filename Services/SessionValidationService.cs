using Microsoft.Playwright;
using SocialCrawler.Models;

namespace SocialCrawler.Services;

public class SessionValidationService
{
    public async Task<(bool Ok, string Message)> ValidateTikTokSessionAsync(List<PlaywrightCookie> cookies)
    {
        var (expired, reason) = CookieService.CheckCookiesExpiration(cookies);
        if (expired) return (false, $"Session đã hết hạn (static check): {reason}");

        var headless = (Environment.GetEnvironmentVariable("HEADLESS") ?? "false").ToLower() != "false";
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await BrowserLauncher.LaunchAsync(playwright, headless: headless);
        await using var context = await browser.NewContextAsync();

        if (cookies.Count > 0)
        {
            var pwCookies = cookies.Select(c => new Microsoft.Playwright.Cookie
            {
                Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path,
                Expires = c.Expires.HasValue ? (float)c.Expires.Value : -1,
                Secure = c.Secure ?? false, HttpOnly = c.HttpOnly ?? false
            }).ToList();
            await context.AddCookiesAsync(pwCookies);
        }

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync("https://www.tiktok.com", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });

            try
            {
                await page.WaitForSelectorAsync(
                    "[data-e2e=\"user-avatar\"], [data-e2e=\"top-login-button\"], [class*=\"DivItemContainer\"]",
                    new PageWaitForSelectorOptions { Timeout = 8000 });
            }
            catch { }

            var isLoggedIn = await page.EvaluateAsync<bool>(@"() => {
                const hasAvatar = !!document.querySelector('[data-e2e=""user-avatar""], [class*=""Avatar""], [class*=""avatar""]');
                const noLoginBtn = !document.querySelector('[data-e2e=""top-login-button""]');
                const hasFeed = document.querySelectorAll('[class*=""DivItemContainer""], [data-e2e=""recommend-list""] > div').length > 0;
                return (hasAvatar || (noLoginBtn && hasFeed)) && !window.location.href.includes('/login');
            }");

            return isLoggedIn
                ? (true, $"✅ Session hợp lệ ({cookies.Count} cookies) — có thể crawl TikTok.")
                : (false, "❌ Session hết hạn hoặc không hợp lệ — vui lòng export lại từ browser.");
        }
        catch (Exception e)
        {
            return (false, $"❌ Lỗi validate: {e.Message}");
        }
    }

    public async Task<(bool Ok, string Message)> ValidateFacebookSessionAsync(List<PlaywrightCookie> cookies)
    {
        var (expired, reason) = CookieService.CheckFacebookCookiesExpiration(cookies);
        if (expired) return (false, $"Session Facebook đã hết hạn (static check): {reason}");

        var headless = (Environment.GetEnvironmentVariable("HEADLESS") ?? "false").ToLower() != "false";
        Console.WriteLine($"[server] Launching browser (headless={headless}) for Facebook session validation...");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await BrowserLauncher.LaunchAsync(playwright, headless: headless);
        await using var context = await browser.NewContextAsync();

        if (cookies.Count > 0)
        {
            var pwCookies = cookies.Select(c => new Microsoft.Playwright.Cookie
            {
                Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path,
                Expires = c.Expires.HasValue ? (float)c.Expires.Value : -1,
                Secure = c.Secure ?? false, HttpOnly = c.HttpOnly ?? false
            }).ToList();
            await context.AddCookiesAsync(pwCookies);
        }

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync("https://www.facebook.com", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });

            try
            {
                await page.WaitForSelectorAsync("input[name=\"email\"], div[role=\"navigation\"]",
                    new PageWaitForSelectorOptions { Timeout = 8000 });
            }
            catch { }

            var currentUrl = page.Url.ToLower();
            if (currentUrl.Contains("/login") || currentUrl.Contains("/checkpoint"))
                return (false, "❌ Session Facebook hết hạn hoặc bị block (bị chuyển hướng sang /login hoặc /checkpoint).");

            var isLoggedIn = await page.EvaluateAsync<bool>(@"() => {
                if (document.querySelector('input[name=""pass""]') || document.querySelector('input[name=""email""]')) return false;
                if (document.querySelector('form[data-testid=""royal_login_form""]')) return false;
                return true;
            }");

            return isLoggedIn
                ? (true, $"✅ Session Facebook hợp lệ ({cookies.Count} cookies).")
                : (false, "❌ Session Facebook hết hạn hoặc bị block (bị đá văng ra màn hình đăng nhập).");
        }
        catch (Exception e)
        {
            return (false, $"❌ Lỗi validate Facebook: {e.Message}");
        }
    }
}
