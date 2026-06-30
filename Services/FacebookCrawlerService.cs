using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SocialCrawler.Models;

namespace SocialCrawler.Services;

public class FacebookCrawlerService
{
    private static readonly Regex GraphQlFilter = new(@"/(api/graphql|graphql|api\.graphql)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Random Rng = new();
    private static readonly HttpClient Http = new();

    public async IAsyncEnumerable<CrawlEvent> ScrapeAsync(
        List<string> targets,
        string? startDate,
        string? endDate,
        int maxPosts,
        List<PlaywrightCookie>? cookies,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        HashSet<string>? stopUrls = null,
        ScrollConfig? scrollConfig = null,
        bool includeTranscripts = false)
    {
        var cfg = scrollConfig ?? new ScrollConfig();
        var startDt = ParseIsoDate(startDate);
        var endDt = ParseIsoDate(endDate, isEndDate: true);
        var headless = (Environment.GetEnvironmentVariable("HEADLESS") ?? "true").ToLower() != "false";

        using var playwright = await Playwright.CreateAsync();
        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = true,
            Channel = "msedge",
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

            var collectedPosts = new List<(PostData Post, JsonElement? RawStory, string PostId, string Source)>();
            var seenPostIds = new HashSet<string>();
            var seenPostUrls = new HashSet<string>();
            var storyBuffer = new System.Collections.Concurrent.ConcurrentQueue<JsonElement>();

            bool AddOrMergePost(PostData post, JsonElement? rawStory, string postId, string source)
            {
                var normalizedUrl = NormalizeFacebookUrl(post.PostUrl);
                var existingIndex = collectedPosts.FindIndex(p =>
                    (!string.IsNullOrEmpty(postId) && p.PostId == postId) ||
                    (!string.IsNullOrEmpty(normalizedUrl) && NormalizeFacebookUrl(p.Post.PostUrl) == normalizedUrl));

                if (existingIndex >= 0)
                {
                    var existing = collectedPosts[existingIndex];

                    // Prefer GraphQL/raw-backed records over lightweight DOM fallback records.
                    if (rawStory.HasValue && (!existing.RawStory.HasValue || source == "graphql"))
                    {
                        collectedPosts[existingIndex] = (post, rawStory, postId, source);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(existing.Post.PublishedAt) && !string.IsNullOrEmpty(post.PublishedAt))
                            existing.Post.PublishedAt = post.PublishedAt;
                        if (string.IsNullOrEmpty(existing.Post.Caption) && !string.IsNullOrEmpty(post.Caption))
                            existing.Post.Caption = post.Caption;
                        if (string.IsNullOrEmpty(existing.Post.AuthorName) && !string.IsNullOrEmpty(post.AuthorName))
                            existing.Post.AuthorName = post.AuthorName;
                        if (existing.Post.Likes == 0 && post.Likes > 0) existing.Post.Likes = post.Likes;
                        if (existing.Post.Comments == 0 && post.Comments > 0) existing.Post.Comments = post.Comments;
                        if (existing.Post.Shares == 0 && post.Shares > 0) existing.Post.Shares = post.Shares;
                        collectedPosts[existingIndex] = existing;
                    }

                    if (!string.IsNullOrEmpty(postId)) seenPostIds.Add(postId);
                    if (!string.IsNullOrEmpty(normalizedUrl)) seenPostUrls.Add(normalizedUrl);
                    return false;
                }

                collectedPosts.Add((post, rawStory, postId, source));
                if (!string.IsNullOrEmpty(postId)) seenPostIds.Add(postId);
                if (!string.IsNullOrEmpty(normalizedUrl)) seenPostUrls.Add(normalizedUrl);
                return true;
            }

            async void OnResponse(object? sender, IResponse response)
            {
                try
                {
                    if (!GraphQlFilter.IsMatch(response.Url)) return;
                    var body = await response.TextAsync();
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

            // Facebook often renders the first/top stories in the initial page payload/DOM.
            // Those posts may not appear in later /api/graphql responses, so capture visible
            // feed articles before scrolling to avoid dropping the newest post.
            var domFoundEvents = new List<CrawlEvent>();
            try
            {
                var visiblePosts = await ExtractVisibleDomPostsAsync(page, target);
                foreach (var domPost in visiblePosts)
                {
                    if (string.IsNullOrEmpty(domPost.PostUrl)) continue;

                    var publishedDt = TryParsePublishedAt(domPost.PublishedAt);
                    if (endDt.HasValue && publishedDt.HasValue && publishedDt.Value > endDt.Value) continue;
                    if (startDt.HasValue && publishedDt.HasValue && publishedDt.Value < startDt.Value) continue;

                    domPost.Platform = "facebook";
                    var domPostId = ExtractPostKey(domPost.PostUrl);
                    if (string.IsNullOrEmpty(domPostId)) domPostId = $"dom_{Math.Abs(NormalizeFacebookUrl(domPost.PostUrl).GetHashCode())}";

                    var rawDom = CreateSyntheticDomStory(domPost, domPostId);
                    if (AddOrMergePost(domPost, rawDom, domPostId, "dom"))
                    {
                        Console.WriteLine($"[Facebook Crawl][DOM] Found visible: {domPost.PostUrl} | Time: {domPost.PublishedAt}");
                        domFoundEvents.Add(new CrawlEvent
                        {
                            Type = "log",
                            Message = $"👉 Found Visible Post: {domPost.PostUrl} | Caption: {domPost.Caption[..Math.Min(40, domPost.Caption.Length)]}..."
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN][Facebook DOM fallback] {ex.GetType().Name}: {ex.Message}");
            }

            foreach (var evt in domFoundEvents)
                yield return evt;

            var lastCount = 0;
            var staleStreak = 0;

            for (var scrollI = 1; scrollI <= cfg.MaxScrolls; scrollI++)
            {
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

                    var post = FacebookParser.ExtractPostFromStoryNode(storyJson);
                    if (post == null) continue;

                    var normalizedPostUrl = NormalizeFacebookUrl(post.PostUrl);
                    if (!string.IsNullOrEmpty(normalizedPostUrl) && seenPostUrls.Contains(normalizedPostUrl))
                    {
                        AddOrMergePost(post, storyJson, postId, "graphql");
                        continue;
                    }

                    if (stopUrls != null && stopUrls.Contains(post.PostUrl))
                    {
                        stopCrawling = true;
                        Console.WriteLine($"[STOP_URL] Gặp post đã có trong DB: {post.PostUrl}");
                        break;
                    }

                    DateTime? publishedDt = null;
                    if (!string.IsNullOrEmpty(post.PublishedAt) && DateTime.TryParse(post.PublishedAt, out var pdt))
                        publishedDt = pdt;

                    if (endDt.HasValue && publishedDt.HasValue && publishedDt.Value > endDt.Value) continue;
                    if (startDt.HasValue && publishedDt.HasValue && publishedDt.Value < startDt.Value)
                    {
                        stopCrawling = true;
                        yield return new CrawlEvent { Type = "log", Message = "Đã chạm mốc start_date, dừng lấy bài." };
                        break;
                    }

                    post.Platform = "facebook";
                    AddOrMergePost(post, storyJson, postId, "graphql");

                    Console.WriteLine($"[Facebook Crawl] Found: {post.PostUrl} | Likes: {post.Likes} | Comments: {post.Comments} | Shares: {post.Shares}");
                    yield return new CrawlEvent
                    {
                        Type = "log",
                        Message = $"👉 Found Post: {post.PostUrl} (Likes: {post.Likes}, Comments: {post.Comments}, Shares: {post.Shares}) | Caption: {post.Caption[..Math.Min(40, post.Caption.Length)]}..."
                    };
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

            // Sắp xếp bài viết theo thời gian giảm dần, lấy top maxPosts
            var topPosts = collectedPosts
                .OrderByDescending(p => TryParsePublishedAt(p.Post.PublishedAt) ?? DateTime.MinValue)
                .Take(maxPosts)
                .ToList();

            if (topPosts.Count < collectedPosts.Count)
            {
                var skipped = collectedPosts.Count - topPosts.Count;
                Console.WriteLine($"[Facebook Crawl] Đã thu thập {collectedPosts.Count} bài, giữ lại {topPosts.Count} bài mới nhất (bỏ {skipped} bài cũ hơn)");
            }

            // Chỉ giữ output của lần crawl hiện tại. Nếu không dọn, file cũ hơn từ lần chạy
            // trước vẫn nằm trong folder khiến nhìn giống crawler lấy sai top maxPosts.
            await CrawlLogger.ClearTargetOutputAsync("facebook", target);

            // Chỉ log raw/parsed cho top posts được giữ lại
            foreach (var (post, rawStory, postId, _) in topPosts)
            {
                if (rawStory.HasValue)
                {
                    post.CaptionTracks = FacebookParser.ExtractCaptionTracks(rawStory.Value);
                    if (includeTranscripts)
                        post.Transcript = await FetchFacebookTranscriptAsync(post.CaptionTracks, cancellationToken);
                    await CrawlLogger.LogRawJsonAsync("facebook", target, postId, rawStory.Value);
                    if (includeTranscripts)
                        await CrawlLogger.LogTranscriptAsync("facebook", target, postId, post.Transcript ?? new TranscriptData { HasTranscript = false, Captions = new List<CaptionEntry>() });
                }
                await CrawlLogger.LogParsedPostAsync("facebook", target, post, postId);
            }

            var resultPosts = topPosts.Select(t => t.Post).ToList();
            var resultFacebookItems = topPosts.Select(t => BuildFacebookRawItem(t.Post, t.RawStory, t.PostId)).ToList();

            if (resultPosts.Count == 0)
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
                Count = resultPosts.Count,
                Videos = resultPosts,
                FacebookItems = resultFacebookItems
            };
        }
    }

    private static FacebookRawItem BuildFacebookRawItem(PostData post, JsonElement? rawStory, string postId)
    {
        return new FacebookRawItem
        {
            Platform = "facebook",
            PostId = postId,
            VideoId = ExtractPostKey(post.Videos?.FirstOrDefault(v => IsFacebookVideoUrl(v)) ?? post.PostUrl),
            PostUrl = post.PostUrl,
            Caption = post.Caption,
            PublishedAt = post.PublishedAt,
            Author = new FacebookAuthor
            {
                Id = post.AuthorId,
                Name = post.AuthorName,
                Url = !string.IsNullOrEmpty(post.AuthorId) ? $"https://www.facebook.com/{post.AuthorId}" : null
            },
            Stats = new FacebookStats
            {
                Views = post.Views,
                Likes = post.Likes,
                Comments = post.Comments,
                Shares = post.Shares
            },
            Media = new FacebookMedia
            {
                ImageUrl = post.ImageUrl,
                Images = post.Images ?? new List<string>(),
                Videos = post.Videos ?? new List<string>(),
                CaptionTracks = post.CaptionTracks ?? new List<FacebookCaptionTrack>()
            },
            Transcript = post.Transcript,
            RawJson = rawStory.HasValue ? rawStory.Value.GetRawText() : null
        };
    }

    private static async Task<TranscriptData?> FetchFacebookTranscriptAsync(List<FacebookCaptionTrack>? tracks, CancellationToken cancellationToken)
    {
        var track = tracks?.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Url));
        if (track == null) return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, track.Url);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            using var resp = await Http.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode) return new TranscriptData { HasTranscript = false, Language = track.Locale, IsAutoGenerated = track.IsAutoGenerated };

            var srt = await resp.Content.ReadAsStringAsync(cancellationToken);
            var captions = ParseSrtCaptions(srt);
            return new TranscriptData
            {
                HasTranscript = captions.Count > 0,
                Language = track.Locale ?? track.Language,
                IsAutoGenerated = track.IsAutoGenerated,
                Captions = captions
            };
        }
        catch
        {
            return new TranscriptData { HasTranscript = false, Language = track.Locale, IsAutoGenerated = track.IsAutoGenerated };
        }
    }

    private static List<CaptionEntry> ParseSrtCaptions(string text)
    {
        var captions = new List<CaptionEntry>();
        if (string.IsNullOrWhiteSpace(text)) return captions;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var blocks = Regex.Split(normalized.Trim(), @"\n{2,}");

        foreach (var block in blocks)
        {
            var lines = block.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) continue;

            var timeIndex = lines.FindIndex(l => l.Contains("-->", StringComparison.Ordinal));
            if (timeIndex < 0) continue;

            var parts = lines[timeIndex].Split("-->", StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;

            var captionText = string.Join("\n", lines.Skip(timeIndex + 1));
            captionText = Regex.Replace(captionText, "<.*?>", "").Trim();
            captionText = WebUtility.HtmlDecode(captionText);
            if (string.IsNullOrWhiteSpace(captionText)) continue;

            captions.Add(new CaptionEntry
            {
                StartTime = parts[0].Replace(',', '.'),
                EndTime = parts[1].Replace(',', '.'),
                Text = captionText
            });
        }

        return captions;
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

    private static DateTime? ParseIsoDate(string? dateStr, bool isEndDate = false)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;
        try
        {
            DateTime result;
            // Date-only string like "2026-06-29" — treat as UTC midnight, NOT local time
            if (DateOnly.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                result = DateTime.Parse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
            }
            else
            {
                result = DateTime.Parse(dateStr.Replace("Z", "+00:00"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
            }

            // end_date should be inclusive of the entire day
            if (isEndDate)
                result = result.AddDays(1);

            return result;
        }
        catch { return null; }
    }

    private static async Task<List<PostData>> ExtractVisibleDomPostsAsync(IPage page, string target)
    {
        var json = await page.EvaluateAsync<string>(@"() => {
            function cleanUrl(raw) {
                try {
                    const u = new URL(raw, location.href);
                    if (!/facebook\.com$/i.test(u.hostname) && !/\.facebook\.com$/i.test(u.hostname)) return '';

                    const storyFbid = u.searchParams.get('story_fbid') || u.searchParams.get('fbid');
                    const id = u.searchParams.get('id');
                    if (storyFbid) return `${u.origin}/story.php?story_fbid=${storyFbid}${id ? '&id=' + id : ''}`;

                    u.hash = '';
                    u.search = '';
                    return u.href.replace(/\/$/, '');
                } catch { return ''; }
            }

            function scoreUrl(url) {
                if (!url || /comment_id=/i.test(url)) return -100;
                if (/\/posts\/(pfbid|\d+)/i.test(url)) return 120;
                if (/\/reel\/\d+/i.test(url)) return 110;
                if (/story\.php\?story_fbid=/i.test(url)) return 100;
                if (/\/videos\/\d+/i.test(url)) return 90;
                if (/\/photos\//i.test(url)) return 70;
                return 0;
            }

            function bestPostUrl(article) {
                let best = '';
                let bestScore = 0;
                for (const a of Array.from(article.querySelectorAll('a[href]'))) {
                    const href = a.href || a.getAttribute('href') || '';
                    const cleaned = cleanUrl(href);
                    const s = scoreUrl(href) || scoreUrl(cleaned);
                    if (s > bestScore) {
                        best = cleaned;
                        bestScore = s;
                    }
                }
                return best;
            }

            function getCaption(article) {
                const msg = article.querySelector('div[data-ad-preview=""message""], div[data-ad-comet-preview=""message""]');
                if (msg && msg.innerText && msg.innerText.trim()) return msg.innerText.trim();

                const skip = /^(Like|Comment|Share|Send|Thích|Bình luận|Chia sẻ|See more|Xem thêm|All reactions|Most relevant)$/i;
                const lines = (article.innerText || '')
                    .split('\n')
                    .map(x => x.trim())
                    .filter(Boolean)
                    .filter(x => !skip.test(x))
                    .filter(x => !/^(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\b/i.test(x));
                return lines.slice(1, 7).join('\n').trim();
            }

            function getDisplayTime(article) {
                const timeRe = /(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\s+\d{1,2}\s+[A-Za-z]+\s+\d{4}\s+at\s+\d{1,2}:\d{2}/i;
                const candidates = [];
                for (const el of Array.from(article.querySelectorAll('[aria-label], a, span'))) {
                    const aria = el.getAttribute('aria-label');
                    if (aria) candidates.push(aria);
                    if (el.innerText) candidates.push(el.innerText);
                }
                const allText = [article.innerText || '', ...candidates].join('\n');
                const m = allText.match(timeRe);
                return m ? m[0] : '';
            }

            function getAuthor(article) {
                const heading = article.querySelector('h2, h3, strong');
                return heading && heading.innerText ? heading.innerText.trim().split('\n')[0] : '';
            }

            const posts = [];
            const seen = new Set();
            const articles = Array.from(document.querySelectorAll('div[role=""article""]'));
            for (const article of articles) {
                const url = bestPostUrl(article);
                if (!url || seen.has(url)) continue;
                seen.add(url);
                posts.push({
                    postUrl: url,
                    caption: getCaption(article),
                    publishedAt: getDisplayTime(article),
                    authorName: getAuthor(article)
                });
                if (posts.length >= 12) break;
            }
            return JSON.stringify(posts);
        }");

        var domPosts = JsonSerializer.Deserialize<List<DomFacebookPost>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<DomFacebookPost>();

        return domPosts
            .Where(p => !string.IsNullOrWhiteSpace(p.PostUrl))
            .Select(p => new PostData
            {
                Platform = "facebook",
                PostUrl = NormalizeFacebookUrl(p.PostUrl),
                Caption = p.Caption ?? "",
                PublishedAt = ParseFacebookDisplayDate(p.PublishedAt)?.ToString("o"),
                Views = 0,
                Likes = 0,
                Comments = 0,
                Shares = 0,
                AuthorName = string.IsNullOrWhiteSpace(p.AuthorName) ? null : p.AuthorName,
                Images = new List<string>(),
                Videos = IsFacebookVideoUrl(p.PostUrl) ? new List<string> { NormalizeFacebookUrl(p.PostUrl) } : new List<string>()
            })
            .ToList();
    }

    private static JsonElement CreateSyntheticDomStory(PostData post, string postId)
    {
        var published = TryParsePublishedAt(post.PublishedAt);
        var raw = new
        {
            __typename = "Story",
            source = "visible_dom_fallback",
            post_id = postId,
            url = post.PostUrl,
            creation_time = published.HasValue ? new DateTimeOffset(published.Value).ToUnixTimeSeconds() : (long?)null,
            display_time = post.PublishedAt,
            message = new { text = post.Caption },
            author = new { name = post.AuthorName }
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(raw));
        return doc.RootElement.Clone();
    }

    private static DateTime? TryParsePublishedAt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed;
        return ParseFacebookDisplayDate(value);
    }

    private static DateTime? ParseFacebookDisplayDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = Regex.Replace(value.Trim(), @"\s+", " ");
        var patterns = new[]
        {
            "dddd d MMMM yyyy 'at' HH:mm",
            "dddd dd MMMM yyyy 'at' HH:mm",
            "d MMMM yyyy 'at' HH:mm",
            "dd MMMM yyyy 'at' HH:mm"
        };

        foreach (var pattern in patterns)
        {
            if (DateTime.TryParseExact(cleaned, pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out var localTime))
                return ConvertVietnamLocalToUtc(localTime);
        }

        if (DateTime.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback))
            return ConvertVietnamLocalToUtc(fallback);

        return null;
    }

    private static DateTime ConvertVietnamLocalToUtc(DateTime localTime)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), tz);
        }
        catch
        {
            return DateTime.SpecifyKind(localTime.AddHours(-7), DateTimeKind.Utc);
        }
    }

    private static string NormalizeFacebookUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLowerInvariant();
            if (host == "l.facebook.com") return url;

            if (uri.AbsolutePath.Equals("/story.php", StringComparison.OrdinalIgnoreCase))
            {
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var storyFbid = query.Get("story_fbid") ?? query.Get("fbid");
                var id = query.Get("id");
                if (!string.IsNullOrEmpty(storyFbid))
                    return $"{uri.Scheme}://{uri.Host}/story.php?story_fbid={storyFbid}{(!string.IsNullOrEmpty(id) ? "&id=" + id : "")}";
            }

            return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}".TrimEnd('/');
        }
        catch
        {
            return url.Trim().TrimEnd('/');
        }
    }

    private static string ExtractPostKey(string? url)
    {
        var normalized = NormalizeFacebookUrl(url);
        if (string.IsNullOrEmpty(normalized)) return "";

        try
        {
            var uri = new Uri(normalized);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var storyFbid = query.Get("story_fbid") ?? query.Get("fbid");
            if (!string.IsNullOrEmpty(storyFbid)) return storyFbid;

            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("posts", StringComparison.OrdinalIgnoreCase) ||
                    parts[i].Equals("reel", StringComparison.OrdinalIgnoreCase) ||
                    parts[i].Equals("videos", StringComparison.OrdinalIgnoreCase))
                    return parts[i + 1];
            }

            return parts.LastOrDefault() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool IsFacebookVideoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.Contains("/reel/", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("/videos/", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DomFacebookPost
    {
        public string? PostUrl { get; set; }
        public string? Caption { get; set; }
        public string? PublishedAt { get; set; }
        public string? AuthorName { get; set; }
    }
}
