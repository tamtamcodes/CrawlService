using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SocialCrawler.Models;

namespace SocialCrawler.Services;

public class FacebookCrawlerService
{
    private static readonly Regex GraphQlFilter = new(@"/(api/graphql|graphql)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Random Rng = new();

    public async IAsyncEnumerable<CrawlEvent> ScrapeAsync(
        List<string> targets,
        string? startDate,
        string? endDate,
        int maxPosts,
        List<PlaywrightCookie>? cookies,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        HashSet<string>? stopUrls = null,
        ScrollConfig? scrollConfig = null)
    {
        var cfg = scrollConfig ?? new ScrollConfig();
        var startDt = ParseIsoDate(startDate);
        var endDt = ParseIsoDate(endDate);
        var headless = (Environment.GetEnvironmentVariable("HEADLESS") ?? "true").ToLower() != "false";

        using var playwright = await Playwright.CreateAsync();
        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = headless,
            Args = new[]
            {
                "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu",
                "--disable-dev-shm-usage", "--disable-extensions", "--disable-logging",
                "--log-level=3", "--js-flags=--max-old-space-size=512",
                "--disable-background-networking", "--disable-renderer-backgrounding",
                "--disable-backgrounding-occluded-windows", "--disable-ipc-flooding-protection",
                "--disable-background-timer-throttling", "--disable-software-rasterizer",
                "--disable-features=TranslateUI,BlinkGenPropertyTrees"
            }
        };

        await using var browser = await playwright.Chromium.LaunchAsync(launchOptions);
        var contextOptions = new BrowserNewContextOptions();
        await using var context = await browser.NewContextAsync(contextOptions);

        if (cookies != null && cookies.Count > 0)
        {
            var pwCookies = cookies.Select(c => new Microsoft.Playwright.Cookie
            {
                Name = c.Name,
                Value = c.Value,
                Domain = c.Domain,
                Path = c.Path,
                Expires = c.Expires.HasValue ? (float)c.Expires.Value : -1,
                Secure = c.Secure ?? false,
                HttpOnly = c.HttpOnly ?? false,
                SameSite = c.SameSite switch
                {
                    "Lax" => SameSiteAttribute.Lax,
                    "Strict" => SameSiteAttribute.Strict,
                    "None" => SameSiteAttribute.None,
                    _ => SameSiteAttribute.None
                }
            }).ToList();
            await context.AddCookiesAsync(pwCookies);
        }

        var page = await context.NewPageAsync();
        var abortAll = false;

        foreach (var target in targets)
        {
            if (abortAll || cancellationToken.IsCancellationRequested)
            {
                yield return new CrawlEvent { Type = "log", Message = "🚫 Tiến trình đã bị hủy bởi người dùng." };
                break;
            }

            yield return new CrawlEvent
            {
                Type = "progress",
                Target = target,
                Message = $"Bắt đầu crawl Facebook (GraphQL interception): {target}",
                Page = 1,
                MaxPages = 5,
                Collected = 0
            };

            var collectedPosts = new List<PostData>();
            var seenPostIds = new HashSet<string>();
            var storyBuffer = new System.Collections.Concurrent.ConcurrentQueue<JsonElement>();

            void OnResponse(object? sender, IResponse response)
            {
                try
                {
                    if (!GraphQlFilter.IsMatch(response.Url)) return;
                    var body = response.TextAsync().GetAwaiter().GetResult();
                    var stories = FacebookParser.ParseGraphQlResponse(body);
                    foreach (var s in stories) storyBuffer.Enqueue(s);
                    if (stories.Count > 0)
                        Console.WriteLine($"[NET] GraphQL matched: {response.Url} | Stories found: {stories.Count}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERR][response] {ex.GetType().Name}: {ex.Message}");
                }
            }

            page.Response += OnResponse;

            CrawlEvent? gotoErrorEvent = null;
            try
            {
                await page.GotoAsync(target, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            }
            catch (Exception ex)
            {
                page.Response -= OnResponse;
                gotoErrorEvent = new CrawlEvent
                {
                    Type = "error",
                    Target = target,
                    Message = $"Lỗi cào Facebook: {ex.Message}"
                };
            }

            if (gotoErrorEvent != null)
            {
                yield return gotoErrorEvent;
                continue;
            }

            await page.WaitForTimeoutAsync(Rng.Next(cfg.InitialDelayMin, cfg.InitialDelayMax));

            var currentUrl = page.Url.ToLower();
            if (currentUrl.Contains("/login") || currentUrl.Contains("/checkpoint"))
            {
                yield return new CrawlEvent
                {
                    Type = "error",
                    Message = "LOGIN_REQUIRED: Bị chuyển hướng sang trang đăng nhập/checkpoint. Facebook đã block cookies hoặc yêu cầu xác minh."
                };
                abortAll = true;
                page.Response -= OnResponse;
                continue;
            }

            try
            {
                await page.WaitForTimeoutAsync(cfg.PopupDismissDelay);
                await page.EvaluateAsync(@"() => {
                    const closeBtns = Array.from(document.querySelectorAll('div[aria-label=""Close""], div[aria-label=""Đóng""]'));
                    closeBtns.forEach(btn => btn.click());
                }");
            }
            catch { }

            var lastCount = 0;
            var staleStreak = 0;

            for (var scrollI = 1; scrollI <= cfg.MaxScrolls; scrollI++)
            {
                if (collectedPosts.Count >= maxPosts) break;
                if (cancellationToken.IsCancellationRequested)
                {
                    yield return new CrawlEvent { Type = "log", Message = "🚫 Tiến trình đã bị hủy giữa chừng." };
                    break;
                }

                await SimulateHumanInteraction(page, cfg);

                try
                {
                    for (var i = 0; i < Rng.Next(cfg.ScrollStepsMin, cfg.ScrollStepsMax); i++)
                    {
                        await page.Keyboard.PressAsync("PageDown");
                        await page.WaitForTimeoutAsync(Rng.Next(cfg.InterStepDelayMin, cfg.InterStepDelayMax));
                    }
                    await page.EvaluateAsync("window.scrollTo({ top: document.body.scrollHeight, behavior: 'smooth' });");
                }
                catch (Exception se)
                {
                    Console.WriteLine($"⚠️ Lỗi cuộn trang: {se.Message}");
                }

                await page.WaitForTimeoutAsync(Rng.Next(cfg.ScrollDelayMin, cfg.ScrollDelayMax));

                try
                {
                    await page.EvaluateAsync(@"() => {
                        const btns = Array.from(document.querySelectorAll(""div[role='button']""));
                        btns.forEach(btn => {
                            const t = btn.innerText;
                            if (t && (t.includes('See more') || t.includes('Xem thêm'))) {
                                try { btn.click(); } catch(e) {}
                            }
                        });
                    }");
                }
                catch { }

                var stopCrawling = false;
                var newStories = new List<JsonElement>();
                while (storyBuffer.TryDequeue(out var s)) newStories.Add(s);

                foreach (var storyJson in newStories)
                {
                    var postId = "";
                    try { postId = storyJson.GetProperty("post_id").GetString() ?? ""; } catch { }
                    if (string.IsNullOrEmpty(postId) || seenPostIds.Contains(postId)) continue;

                    seenPostIds.Add(postId);
                    var post = FacebookParser.ExtractPostFromStoryNode(storyJson);
                    if (post == null) continue;

                    if (stopUrls != null && stopUrls.Contains(post.PostUrl))
                    {
                        stopCrawling = true;
                        Console.WriteLine($"[STOP_URL] Gặp post đã có trong DB: {post.PostUrl}");
                        break;
                    }

                    DateTime? publishedDt = null;
                    if (!string.IsNullOrEmpty(post.PublishedAt))
                        DateTime.TryParse(post.PublishedAt, out var pdt);

                    if (endDt.HasValue && publishedDt.HasValue && publishedDt.Value > endDt.Value) continue;
                    if (startDt.HasValue && publishedDt.HasValue && publishedDt.Value < startDt.Value)
                    {
                        stopCrawling = true;
                        yield return new CrawlEvent { Type = "log", Message = "Đã chạm mốc start_date, dừng lấy bài." };
                        break;
                    }

                    post.Platform = "facebook";
                    collectedPosts.Add(post);

                    // Log raw JSON and parsed post
                    await CrawlLogger.LogRawJsonAsync("facebook", target, postId, storyJson);

                    // Log transcript if available
                    await CrawlLogger.LogTranscriptAsync("facebook", target, postId, storyJson);

                    await CrawlLogger.LogParsedPostAsync("facebook", target, post);

                    Console.WriteLine($"[Facebook Crawl] Found: {post.PostUrl} | Likes: {post.Likes} | Comments: {post.Comments} | Shares: {post.Shares}");
                    yield return new CrawlEvent
                    {
                        Type = "log",
                        Message = $"👉 Found Post: {post.PostUrl} (Likes: {post.Likes}, Comments: {post.Comments}, Shares: {post.Shares}) | Caption: {post.Caption[..Math.Min(40, post.Caption.Length)]}..."
                    };

                    if (collectedPosts.Count >= maxPosts)
                    {
                        stopCrawling = true;
                        break;
                    }
                }

                if (stopCrawling) break;

                var currentCount = collectedPosts.Count;
                if (currentCount == lastCount)
                {
                    staleStreak++;
                    Console.WriteLine($"[SCROLL {scrollI}] Không có post mới ({staleStreak}/{cfg.StaleLimit} lần liên tiếp)");
                }
                else
                {
                    staleStreak = 0;
                    lastCount = currentCount;
                }

                if (staleStreak >= cfg.StaleLimit)
                {
                    Console.WriteLine($"[SCROLL] Dừng sớm sau {scrollI} lần — {cfg.StaleLimit} lần liên tiếp không có post mới");
                    break;
                }

                yield return new CrawlEvent
                {
                    Type = "progress",
                    Target = target,
                    Page = scrollI,
                    MaxPages = cfg.MaxScrolls,
                    Collected = collectedPosts.Count,
                    Message = $"Đang cào (lần cuộn {scrollI})... Thu được {collectedPosts.Count} bài."
                };
            }

            page.Response -= OnResponse;

            if (collectedPosts.Count == 0)
            {
                yield return new CrawlEvent
                {
                    Type = "error",
                    Target = target,
                    Message = $"Không tìm thấy bài viết nào từ: {target}. Cookies có thể đã hết hạn hoặc cấu trúc GraphQL đã thay đổi."
                };
            }

            yield return new CrawlEvent
            {
                Type = "done",
                Target = target,
                Count = collectedPosts.Count,
                Videos = collectedPosts
            };
        }
    }

    private static async Task SimulateHumanInteraction(IPage page, ScrollConfig cfg)
    {
        try
        {
            if (Rng.NextDouble() < cfg.HumanScrollChance)
            {
                await page.Keyboard.PressAsync("PageDown");
                await Task.Delay(TimeSpan.FromSeconds(
                    cfg.HumanScrollDelayMin + Rng.NextDouble() * (cfg.HumanScrollDelayMax - cfg.HumanScrollDelayMin)));
            }

            await page.Mouse.MoveAsync(
                Rng.Next(100, 900),
                Rng.Next(100, 700),
                new MouseMoveOptions { Steps = Rng.Next(cfg.HumanMouseMoveStepsMin, cfg.HumanMouseMoveStepsMax) });

            if (Rng.NextDouble() < cfg.HumanScrollUpChance)
            {
                await Task.Delay(TimeSpan.FromSeconds(
                    cfg.HumanScrollUpDelayMin + Rng.NextDouble() * (cfg.HumanScrollUpDelayMax - cfg.HumanScrollUpDelayMin)));
                await page.Keyboard.PressAsync("PageUp");
                await page.Mouse.MoveAsync(
                    Rng.Next(100, 900),
                    Rng.Next(100, 700),
                    new MouseMoveOptions { Steps = Rng.Next(cfg.HumanMouseMoveStepsMin, cfg.HumanMouseMoveStepsMax) });
            }
        }
        catch { }
    }

    private static DateTime? ParseIsoDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;
        try { return DateTime.Parse(dateStr.Replace("Z", "+00:00")).ToUniversalTime(); }
        catch { return null; }
    }
}
