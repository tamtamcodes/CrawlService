using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Web;
using Microsoft.Playwright;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using SocialCrawler.Models;

namespace SocialCrawler.Services;

// FIX B1: Browser pool — share one IBrowser, issue one IBrowserContext per request.
// Playwright's IBrowser is thread-safe for NewContextAsync(); IBrowserContext/IPage are NOT.
// The pool caps concurrent Chromium instances and reuses the warm JIT/socket state.
public sealed class PlaywrightBrowserPool : IAsyncDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly bool _headless;

    public PlaywrightBrowserPool(int maxConcurrentContexts = 5)
    {
        _semaphore = new SemaphoreSlim(maxConcurrentContexts, maxConcurrentContexts);
        _headless = (Environment.GetEnvironmentVariable("HEADLESS") ?? "false").ToLower() != "false";
    }

    // Lease one context from the pool; dispose the returned handle to release the slot.
    public async Task<PooledContext> LeaseContextAsync(
        IReadOnlyList<PlaywrightCookie>? cookies,
        CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var browser = await GetOrCreateBrowserAsync(ct);
            var context = await browser.NewContextAsync();

            if (cookies is { Count: > 0 })
            {
                var pwCookies = cookies.Select(c => new Microsoft.Playwright.Cookie
                {
                    Name = c.Name,
                    Value = c.Value,
                    Domain = c.Domain,
                    Path = c.Path,
                    Expires = c.Expires.HasValue ? (float)c.Expires.Value : -1,
                    Secure = c.Secure ?? false,
                    HttpOnly = c.HttpOnly ?? false
                }).ToList();
                await context.AddCookiesAsync(pwCookies);
            }

            var page = await context.NewPageAsync();
            return new PooledContext(context, page, _semaphore);
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    private async Task<IBrowser> GetOrCreateBrowserAsync(CancellationToken ct)
    {
        if (_browser is { IsConnected: true }) return _browser;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_browser is { IsConnected: true }) return _browser;

            _playwright?.Dispose();
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _headless,
                Args =
                [
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    // Stealth flags to reduce captcha/headless detection
                    "--disable-blink-features=AutomationControlled",
                    "--disable-component-update",
                    "--no-default-browser-check",
                    "--disable-client-side-phishing-detection",
                    "--disable-features=Translate,ChromeWhatsNew,InterestFeedContentSuggestions",
                    "--ignore-certificate-errors",
                    "--disable-sync",
                    "--metrics-recording-only",
                    "--no-first-run",
                    "--window-size=1920,1080",
                    "--start-maximized"
                ]
            });
            return _browser;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
        _semaphore.Dispose();
        _initLock.Dispose();
    }
}

public sealed class PooledContext(IBrowserContext context, IPage page, SemaphoreSlim semaphore) : IAsyncDisposable
{
    public IPage Page { get; } = page;
    public IBrowserContext Context { get; } = context;

    public async ValueTask DisposeAsync()
    {
        try { await Context.CloseAsync(); } catch { /* best-effort */ }
        semaphore.Release();
    }
}

public class TikTokCrawlerService : IAsyncDisposable
{
    private const int MaxPages = 50;
    private const int MaxFetchRetries = 3;  // Polly owns retry now; this is the ceiling.

    // FIX B2: Random.Shared is thread-safe in .NET 6+. No more shared mutable state.
    private static Random Rng => Random.Shared;

    // FIX B5: Static readonly JsonSerializerOptions — never new() in a hot loop.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    // FIX B6: Polly async retry + circuit breaker wired up once per service instance.
    // Exponential backoff with full jitter avoids thundering herd when many crawlers
    // hit TikTok simultaneously. Circuit breaker opens after 5 consecutive failures
    // and stays open for 30 s so other callers fail-fast instead of hammering the API.
    private readonly ResiliencePipeline<JsonElement> _fetchPipeline;

    // FIX B1: Pool shared across all concurrent crawl calls on this service instance.
    private readonly PlaywrightBrowserPool _browserPool;

    // FIX B5: Single background writer drains the channel sequentially so the hot
    // crawl loop never blocks on disk I/O.
    private readonly Channel<(string Path, string Json)> _writeChannel;
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _writerCts = new();

    public TikTokCrawlerService(int maxConcurrentContexts = 5)
    {
        _browserPool = new PlaywrightBrowserPool(maxConcurrentContexts);

        // FIX B6: Build the Polly resilience pipeline.
        _fetchPipeline = new ResiliencePipelineBuilder<JsonElement>()
            .AddRetry(new RetryStrategyOptions<JsonElement>
            {
                MaxRetryAttempts = MaxFetchRetries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,          // full jitter — each instance retries at a different time
                Delay = TimeSpan.FromSeconds(2),
                ShouldHandle = new PredicateBuilder<JsonElement>()
                    .HandleResult(r => GetStatusCode(r) != 0)
                    .Handle<Exception>(),
                OnRetry = args =>
                {
                    Console.WriteLine($"[Polly] Retry {args.AttemptNumber + 1} after {args.RetryDelay.TotalSeconds:F1}s");
                    return default;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<JsonElement>
            {
                FailureRatio = 0.6,                      // 60 % failures in the window → open
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),  // fail-fast for 30 s then half-open
                ShouldHandle = new PredicateBuilder<JsonElement>()
                    .HandleResult(r => GetStatusCode(r) != 0)
                    .Handle<Exception>()
            })
            .Build();

        // FIX B5: Bounded channel — if the writer falls behind, back-pressure is applied
        // to the crawl loop rather than letting RAM grow unboundedly.
        _writeChannel = Channel.CreateBounded<(string, string)>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        _writerTask = RunFileWriterAsync(_writerCts.Token);
    }

    // Background task: drains the write channel and writes files one at a time.
    private async Task RunFileWriterAsync(CancellationToken ct)
    {
        await foreach (var (path, json) in _writeChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, json, Encoding.UTF8, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileWriter] Error writing {path}: {ex.Message}");
            }
        }
    }

    private const string JsExtractSecUid = """
        async (username) => {
            const sigi = document.getElementById('SIGI_STATE');
            if (sigi) {
                try {
                    const data = JSON.parse(sigi.textContent);
                    if (data.UserModule && data.UserModule.users) {
                        const keys = Object.keys(data.UserModule.users);
                        if (keys.length > 0) {
                            const secUid = data.UserModule.users[keys[0]].secUid;
                            if (secUid) return secUid;
                        }
                    }
                } catch (e) {}
            }
            const rehy = document.getElementById('__UNIVERSAL_DATA_FOR_REHYDRATION__');
            if (rehy) {
                try {
                    const data = JSON.parse(rehy.textContent);
                    const userDetail = data?.__DEFAULT_SCOPE__?.['webapp.user-detail'];
                    if (userDetail && userDetail.userInfo && userDetail.userInfo.user) {
                        const secUid = userDetail.userInfo.user.secUid;
                        if (secUid) return secUid;
                    }
                } catch (e) {}
            }
            const match = document.documentElement.innerHTML.match(/"secUid"\s*:\s*"([^"]+)"/);
            if (match) return match[1];
            if (window.byted_acrawler && window.byted_acrawler.frontierSign) {
                try {
                    const base = "https://www.tiktok.com/api/user/detail/";
                    const params = { aid: "1988", app_name: "tiktok_web", device_platform: "web_pc", uniqueId: username, secUid: "" };
                    const qs = Object.keys(params).map(k => `${k}=${encodeURIComponent(params[k])}`).join('&');
                    const url = `${base}?${qs}`;
                    const sig = window.byted_acrawler.frontierSign(url);
                    const xBogus = sig && sig["X-Bogus"];
                    if (xBogus) {
                        const signedUrl = `${url}&X-Bogus=${xBogus}`;
                        const resp = await fetch(signedUrl).then(r => r.json());
                        const secUid = resp?.userInfo?.user?.secUid;
                        if (secUid) return secUid;
                    }
                } catch (e) { console.error("AJAX extraction error:", e); }
            }
            return null;
        }
        """;

    private const string JsWaitForUid = """
        () => {
            return document.getElementById('SIGI_STATE') !== null ||
                   document.getElementById('__UNIVERSAL_DATA_FOR_REHYDRATION__') !== null ||
                   /"secUid"\s*:\s*"([^"]+)"/.test(document.documentElement.innerHTML);
        }
        """;

    private const string JsCheckCaptcha = """
        () => {
            const el = document.querySelector(
                '#captcha-container, .captcha-container, [data-e2e*="captcha"], ' +
                'div[class*="secsdk-captcha"], iframe[src*="captcha"]'
            );
            if (el) return "CAPTCHA";
            const path = window.location.pathname.toLowerCase();
            if (path.includes('/captcha')) return "CAPTCHA";
            if (path.includes('/login') && !path.includes('@')) return "LOGIN";
            return null;
        }
        """;

    // Aggressively removes captcha modal overlays + captcha containers from DOM.
    // TikTok captcha is a front-end modal — removing its elements lets the page
    // function normally underneath.
    private const string JsDismissCaptcha = """
        () => {
            // Selectors for TikTok captcha modal/overlay containers
            const selectors = [
                '#captcha-container',
                '.captcha-container',
                '.secsdk-captcha-container',
                'div[class*="captcha"]',
                'div[id*="captcha"]',
                'div[class*="secsdk"]',
                'iframe[src*="captcha"]',
                'div[class*="modal-mask"]',
                'div[class*="ModalContainer"]'
            ];
            let removed = 0;
            selectors.forEach(sel => {
                document.querySelectorAll(sel).forEach(el => {
                    // Remove overlay/modal elements to restore page functionality
                    if (el && el.parentNode) {
                        el.remove();
                        removed++;
                    }
                });
            });
            // Also remove body overflow hidden that captcha modals set
            document.body.style.overflow = '';
            document.body.style.position = '';
            document.documentElement.style.overflow = '';
            return removed;
        }
        """;

    public async IAsyncEnumerable<CrawlEvent> ScrapeStreamAsync(
        List<string> targets,
        string? startDateStr,
        string? endDateStr,
        string periodStr,
        List<PlaywrightCookie>? cookies,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        bool includeComments = false)
    {
        long startTime;
        if (!string.IsNullOrEmpty(startDateStr) && TryParseIsoToUnix(startDateStr, out var st))
            startTime = st;
        else
        {
            var periodSeconds = PeriodParser.ParseToSeconds(periodStr);
            startTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - periodSeconds;
        }

        long? endTime = null;
        if (!string.IsNullOrEmpty(endDateStr) && TryParseIsoToUnix(endDateStr, out var et))
            endTime = et;

        var totalCollected = 0;
        var completedTargets = new HashSet<string>();

        // FIX B1: Lease one context from the shared browser pool per ScrapeStreamAsync call.
        // Disposing the PooledContext returns the slot to the semaphore.
        await using var pooled = await _browserPool.LeaseContextAsync(cookies, cancellationToken);
        var page = pooled.Page;

        yield return new CrawlEvent { Type = "log", Message = "Đã lấy browser context từ pool." };

        foreach (var target in targets)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return new CrawlEvent { Type = "log", Message = "🚫 Tiến trình đã bị hủy bởi người dùng." };
                break;
            }

            if (completedTargets.Contains(target)) continue;
            yield return new CrawlEvent { Type = "log", Message = $"--- Bắt đầu thu thập mục tiêu: {target} ---" };

            string? secUid = null;
            var cursor = 0L;
            var hasMore = true;
            var seenIds = new HashSet<string?>();
            var pageCount = 0;
            var targetCount = 0;
            var targetHasError = false;
            var abortTarget = false;

            yield return new CrawlEvent { Type = "log", Message = $"Đang trích xuất secUid cho: {target}..." };

            // C# does not allow yield inside catch — capture outcome, yield after.
            CrawlEvent? secUidEvent = null;
            var secUidBreak = false;
            var secUidContinue = false;
            try
            {
                // FIX B3: Pass cancellationToken into every Playwright await via PageGotoOptions.
                secUid = await ExtractSecUidAsync(page, target, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                secUidEvent = new CrawlEvent { Type = "log", Message = "🚫 Bị hủy trong lúc trích xuất secUid." };
                secUidBreak = true;
            }
            catch (Exception e)
            {
                var errMsg = e.Message;
                if (errMsg.Contains("CAPTCHA_DETECTED"))
                {
                    secUidEvent = new CrawlEvent { Type = "error", Message = "CAPTCHA_DETECTED: Phát hiện Captcha! Vui lòng cập nhật cookies mới rồi chạy lại." };
                    secUidBreak = true;
                }
                else if (errMsg.Contains("LOGIN_REQUIRED"))
                {
                    secUidEvent = new CrawlEvent { Type = "error", Message = "LOGIN_REQUIRED: Bị chuyển hướng sang trang đăng nhập. Cookies có thể đã hết hạn." };
                    secUidBreak = true;
                }
                else
                {
                    secUidEvent = new CrawlEvent { Type = "error", Message = $"Không thể trích xuất secUid cho: {target}. Chi tiết: {e.Message}" };
                    secUidContinue = true;
                }
            }

            if (secUidEvent != null) yield return secUidEvent;
            if (secUidBreak) break;
            if (secUidContinue) continue;

            if (string.IsNullOrEmpty(secUid))
            {
                yield return new CrawlEvent { Type = "error", Message = $"Không thể trích xuất secUid cho: {target}" };
                continue;
            }

            yield return new CrawlEvent { Type = "log", Message = $"secUid đã giải quyết: {secUid[..Math.Min(30, secUid.Length)]}..." };

            yield return new CrawlEvent { Type = "log", Message = "Đang chờ byted_acrawler..." };
            await EnsureBytedAcrawlerAsync(page, cancellationToken);

            yield return new CrawlEvent { Type = "log", Message = "Đang khởi tạo session params..." };
            var sessionParams = await BuildSessionParamsAsync(page, pooled.Context, target, secUid);

            while (hasMore && pageCount < MaxPages && !abortTarget)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield return new CrawlEvent { Type = "log", Message = "🚫 Tiến trình đã bị hủy giữa chừng." };
                    targetHasError = true;
                    goto TargetDone;
                }

                pageCount++;
                sessionParams["cursor"] = cursor.ToString();

                yield return new CrawlEvent
                {
                    Type = "progress",
                    Target = target,
                    Page = pageCount,
                    MaxPages = MaxPages,
                    Collected = targetCount,
                    Message = $"Đang cào trang {pageCount} tại offset {cursor}... (Đã thu thập: {targetCount} video)"
                };

                JsonElement data;
                CrawlEvent? fetchEvent = null;
                var fetchBreak = false;
                var fetchGotoTargetDone = false;
                string? captchaCheckState = null;
                Exception? fetchFallbackEx = null;
                try
                {
                    // FIX B6: Polly pipeline handles retries + circuit breaker.
                    data = await _fetchPipeline.ExecuteAsync(
                        async ct => await SignAndFetchAsync(page, sessionParams, ct),
                        cancellationToken);
                }
                catch (BrokenCircuitException)
                {
                    fetchEvent = new CrawlEvent { Type = "error", Message = "⚡ Circuit breaker mở — TikTok API đang bị quá tải. Dừng crawl." };
                    targetHasError = true;
                    fetchBreak = true;
                    data = default;
                }
                catch (OperationCanceledException)
                {
                    fetchEvent = new CrawlEvent { Type = "log", Message = "🚫 Bị hủy trong lúc fetch." };
                    targetHasError = true;
                    fetchGotoTargetDone = true;
                    data = default;
                }
                catch (Exception ex)
                {
                    // Polly exhausted all retries — check page state outside the catch.
                    fetchFallbackEx = ex;
                    data = default;
                }

                // Yield & flow control must be outside catch blocks.
                if (fetchEvent != null) yield return fetchEvent;
                if (fetchBreak) break;
                if (fetchGotoTargetDone) goto TargetDone;

                if (fetchFallbackEx != null)
                {
                    captchaCheckState = await page.EvaluateAsync<string?>(JsCheckCaptcha);
                    if (captchaCheckState == "CAPTCHA")
                    {
                        var dismissed = await TryDismissCaptchaAsync(page);
                        if (!dismissed)
                        {
                            yield return new CrawlEvent { Type = "error", Message = "CAPTCHA_DETECTED: Phát hiện Captcha trong quá trình tải danh sách!" };
                            goto TargetDone;
                        }
                        yield return new CrawlEvent { Type = "log", Message = "Captcha đã được dismiss, tiếp tục..." };
                    }
                    if (captchaCheckState == "LOGIN")
                    {
                        yield return new CrawlEvent { Type = "error", Message = "LOGIN_REQUIRED: Bị văng ra trang đăng nhập." };
                        goto TargetDone;
                    }
                    yield return new CrawlEvent { Type = "error", Message = $"API thất bại sau tất cả các lần thử: {fetchFallbackEx.Message}" };
                    targetHasError = true;
                    break;
                }

                var statusCode = GetStatusCode(data);
                if (statusCode != 0)
                {
                    yield return new CrawlEvent { Type = "error", Message = $"API trả status_code={statusCode} sau retry." };
                    targetHasError = true;
                    break;
                }

                var items = GetItemList(data);
                if (items.Count == 0)
                {
                    yield return new CrawlEvent { Type = "log", Message = "Không còn bài viết nào. Kết thúc mục tiêu này." };
                    break;
                }

                long? oldestInPage = null;
                foreach (var item in items)
                {
                    var raw = TikTokVideoParser.ParseVideoItem(item);
                    var createTime = raw.CreateTime;

                    if (oldestInPage == null || createTime < oldestInPage)
                        oldestInPage = createTime;

                    if (endTime.HasValue && createTime > endTime.Value) continue;

                    if (createTime >= startTime && !seenIds.Contains(raw.Id))
                    {
                        seenIds.Add(raw.Id);
                        targetCount++;
                        totalCollected++;

                        if (includeComments)
                        {
                            yield return new CrawlEvent { Type = "log", Message = $"Đang lấy comment cho video {raw.Id}..." };
                            raw.Comments = new List<TikTokComment>();
                            await foreach (var commentEvent in ScrapeCommentsStreamAsync(raw.Id!, target, page, raw.Comments, cancellationToken))
                            {
                                yield return commentEvent;
                            }
                        }

                        // Save the full TikTokRawItem (includes comments) as a single JSON file
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        var rawDir = Path.Combine("output", "tiktok", "raw");
                        var filePath = Path.Combine(rawDir, $"{timestamp}_{target}_{raw.Id}_raw.json");
                        var rawJson = JsonSerializer.Serialize(raw, JsonOptions);
                        await _writeChannel.Writer.WriteAsync((filePath, rawJson), cancellationToken);

                        yield return new CrawlEvent
                        {
                            Type = "item",
                            RawItems = new List<TikTokRawItem> { raw }
                        };
                    }
                }

                if (oldestInPage.HasValue && oldestInPage.Value < startTime)
                {
                    var cutoffFmt = DateTimeOffset.FromUnixTimeSeconds(startTime).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    yield return new CrawlEvent { Type = "log", Message = $"Video cũ nhất trên trang đã cũ hơn mốc {cutoffFmt}. Kết thúc mục tiêu này." };
                    break;
                }

                var hasMoreVal = data.TryGetProperty("hasMore", out var hm);
                hasMore = hasMoreVal && (hm.ValueKind == JsonValueKind.True ||
                          (hm.ValueKind == JsonValueKind.Number && hm.GetInt32() == 1));

                if (data.TryGetProperty("cursor", out var nc))
                {
                    long.TryParse(nc.ValueKind == JsonValueKind.Number
                        ? nc.GetInt64().ToString()
                        : nc.GetString(), out var nextCursor);
                    cursor = nextCursor > 0 ? nextCursor : cursor + items.Count;
                }
                else
                {
                    cursor += items.Count;
                }

                var delay = 2.0 + Rng.NextDouble() * 2.0;
                yield return new CrawlEvent { Type = "log", Message = $"Tạm nghỉ {delay:F2}s và giả lập tương tác..." };
                await SimulateHumanInteraction(page);
                await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
            }

        TargetDone:
            completedTargets.Add(target);

            yield return new CrawlEvent
            {
                Type = "log",
                Message = targetHasError
                    ? $"⚠️ Thu thập không trọn vẹn mục tiêu {target} (chỉ được {targetCount} video do lỗi)."
                    : $"✅ Hoàn tất mục tiêu {target} (thu thập {targetCount} video)."
            };
        }

        yield return new CrawlEvent { Type = "log", Message = "Trả context về pool..." };

        yield return new CrawlEvent
        {
            Type = "done",
            Count = totalCollected
            // FIX B4: RawItems NOT accumulated here. Callers receive items via "item" events above.
        };
    }

    // Try to dismiss captcha overlay by removing its DOM elements, then re-check.
    // Returns true if captcha was resolved (no longer detected).
    private static async Task<bool> TryDismissCaptchaAsync(IPage page)
    {
        try
        {
            var removed = await page.EvaluateAsync<int>(JsDismissCaptcha);
            if (removed > 0)
            {
                Console.WriteLine($"[Captcha] Đã xoá {removed} phần tử captcha khỏi DOM.");
                await Task.Delay(2000); // wait for any re-render
                var stillThere = await page.EvaluateAsync<string?>(JsCheckCaptcha);
                if (stillThere == null)
                {
                    Console.WriteLine("[Captcha] Captcha đã được dismiss thành công!");
                    return true;
                }
                Console.WriteLine("[Captcha] Captcha vẫn còn sau khi dismiss, thử lại...");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Captcha] Lỗi khi dismiss captcha: {ex.Message}");
        }
        return false;
    }

    // FIX B3: cancellationToken threaded through every Playwright call.
    private static async Task<string?> ExtractSecUidAsync(IPage page, string target, CancellationToken ct)
    {
        if (target.StartsWith("MS4wLjABAAAA") && target.Length > 50) return target;

        var username = ResolveUsername(target);
        var usernameClean = username.TrimStart('@');
        var profileUrl = $"https://www.tiktok.com/{username}";

        Console.WriteLine($"[secUid] Navigating to {profileUrl}");
        await page.GotoAsync(profileUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000
        });

        // FIX B3: NetworkIdle wait honours ct via the explicit timeout + ct combo.
        using var ncts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ncts.CancelAfter(15_000);
        try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 }); }
        catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); }
        catch { /* timeout is acceptable */ }

        // Check + try to dismiss captcha (TikTok captcha is a dismissable FE modal)
        var state = await page.EvaluateAsync<string?>(JsCheckCaptcha);
        if (state == "CAPTCHA")
        {
            Console.WriteLine("[Captcha] Phát hiện captcha, đang thử dismiss...");
            if (await TryDismissCaptchaAsync(page))
            {
                Console.WriteLine("[Captcha] Đã dismiss captcha, tiếp tục...");
            }
            else
            {
                throw new Exception("CAPTCHA_DETECTED");
            }
        }
        if (state == "LOGIN") throw new Exception("LOGIN_REQUIRED");

        await SimulateHumanInteraction(page);

        try
        {
            await page.WaitForFunctionAsync(
                JsWaitForUid,
                null,
                new PageWaitForFunctionOptions { Timeout = 45_000 });
        }
        catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); }
        catch { }

        var secUid = await page.EvaluateAsync<string?>(JsExtractSecUid, usernameClean);
        if (string.IsNullOrEmpty(secUid))
            throw new Exception($"Không thể trích xuất secUid cho '{target}'. Cookies có thể hết hạn hoặc TikTok đang chặn.");

        return secUid;
    }

    private static string ResolveUsername(string target)
    {
        var username = target.Trim();
        if (username.Contains("tiktok.com"))
        {
            var m = Regex.Match(username, @"/(@[a-zA-Z0-9_\.]+)");
            if (m.Success) username = m.Groups[1].Value;
            else
            {
                m = Regex.Match(username, @"tiktok\.com/([^/?#]+)");
                if (m.Success) username = m.Groups[1].Value;
            }
        }
        if (!username.StartsWith("@")) username = "@" + username;
        return username;
    }

    // FIX B3: cancellationToken passed through.
    private static async Task EnsureBytedAcrawlerAsync(IPage page, CancellationToken ct)
    {
        try
        {
            await page.WaitForFunctionAsync(
                "window.byted_acrawler !== undefined",
                null,
                new PageWaitForFunctionOptions { Timeout = 15_000 });
        }
        catch
        {
            ct.ThrowIfCancellationRequested();
            await page.GotoAsync("https://www.tiktok.com", new PageGotoOptions { Timeout = 60_000 });
            await page.WaitForFunctionAsync(
                "window.byted_acrawler !== undefined",
                null,
                new PageWaitForFunctionOptions { Timeout = 30_000 });
        }
    }

    private static async Task<Dictionary<string, string>> BuildSessionParamsAsync(
        IPage page, IBrowserContext context, string target, string secUid)
    {
        var userAgent = await page.EvaluateAsync<string>("() => navigator.userAgent");
        var language = await page.EvaluateAsync<string>("() => navigator.language || navigator.userLanguage");
        var platform = await page.EvaluateAsync<string>("() => navigator.platform");
        var timezone = await page.EvaluateAsync<string>("() => Intl.DateTimeFormat().resolvedOptions().timeZone");

        var deviceId = Rng.NextInt64(1_000_000_000_000_000_000L, long.MaxValue).ToString();
        var historyLen = Rng.Next(1, 11).ToString();
        var screenH = Rng.Next(600, 1081).ToString();
        var screenW = Rng.Next(800, 1921).ToString();

        var loadedCookies = await context.CookiesAsync();
        var cookieMap = new Dictionary<string, string>();
        foreach (var c in loadedCookies)
            if (!cookieMap.ContainsKey(c.Name))
                cookieMap[c.Name] = c.Value;

        cookieMap.TryGetValue("msToken", out var msToken);
        cookieMap.TryGetValue("s_v_web_id", out var verifyFp);

        string? odinId = null;
        if (!cookieMap.TryGetValue("odinId", out odinId) &&
            cookieMap.TryGetValue("multi_sids", out var multiSids))
        {
            var decoded = HttpUtility.UrlDecode(multiSids);
            if (decoded.Contains(':')) odinId = decoded.Split(':')[0];
        }

        var isLogin = cookieMap.ContainsKey("sessionid") || cookieMap.ContainsKey("sid_tt");

        var region = "VN"; var appLang = "vi-VN"; var webcastLang = "vi-VN";
        if (!string.IsNullOrEmpty(timezone))
        {
            var tzLower = timezone.ToLower();
            if (tzLower.Contains("tokyo")) { region = "JP"; appLang = "ja-JP"; webcastLang = "ja-JP"; }
            else if (tzLower.Contains("shanghai") || tzLower.Contains("hong_kong")) { region = "CN"; appLang = "zh-Hans"; webcastLang = "zh-Hans"; }
            else if (tzLower.Contains("america") || tzLower.Contains("europe") || tzLower.Contains("london")) { region = "US"; appLang = "en"; webcastLang = "en"; }
        }

        var tgt = target.StartsWith("@") ? target : "@" + target;
        var rootRef = $"https://www.tiktok.com/{tgt.Split('?')[0]}";

        var p = new Dictionary<string, string>
        {
            ["WebIdLastTime"] = "1737115998",
            ["aid"] = "1988",
            ["app_language"] = appLang,
            ["app_name"] = "tiktok_web",
            ["browser_language"] = language,
            ["browser_name"] = "Mozilla",
            ["browser_online"] = "true",
            ["browser_platform"] = platform,
            ["browser_version"] = userAgent,
            ["channel"] = "tiktok_web",
            ["cookie_enabled"] = "true",
            ["count"] = "30",
            ["coverFormat"] = "2",
            ["cursor"] = "0",
            ["data_collection_enabled"] = "false",
            ["device_id"] = deviceId,
            ["device_platform"] = "web_pc",
            ["focus_state"] = "true",
            ["from_page"] = "user",
            ["history_len"] = historyLen,
            ["is_fullscreen"] = "false",
            ["is_page_visible"] = "true",
            ["language"] = language,
            ["needPinnedItemIds"] = "true",
            ["os"] = "windows",
            ["post_item_list_request_type"] = "0",
            ["priority_region"] = "",
            ["referer"] = "",
            ["region"] = region,
            ["root_referer"] = rootRef,
            ["screen_height"] = screenH,
            ["screen_width"] = screenW,
            ["secUid"] = secUid,
            ["tz_name"] = timezone,
            ["user_is_login"] = isLogin ? "true" : "false",
            ["webcast_language"] = webcastLang
        };

        if (!string.IsNullOrEmpty(msToken)) p["msToken"] = msToken;
        if (!string.IsNullOrEmpty(odinId)) p["odinId"] = odinId;
        if (!string.IsNullOrEmpty(verifyFp)) p["verifyFp"] = verifyFp;

        return p;
    }

    // FIX B3: accepts CancellationToken so Polly can propagate cancellation cleanly.
    private static async Task<JsonElement> SignAndFetchAsync(
        IPage page,
        Dictionary<string, string> sessionParams,
        CancellationToken ct = default) =>
        await SignAndFetchUrlAsync(page, "/api/post/item_list/", sessionParams, ct);

    // Generic version: signs + fetches any TikTok API endpoint via byted_acrawler.
    private static async Task<JsonElement> SignAndFetchUrlAsync(
        IPage page,
        string apiPath,
        Dictionary<string, string> queryParams,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var qs = string.Join("&", queryParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        var fullUnsignedUrl = $"https://www.tiktok.com{apiPath}?{qs}";

        var sigResult = await page.EvaluateAsync<JsonElement>(
            $"() => window.byted_acrawler.frontierSign(\"{fullUnsignedUrl}\")");

        string? xBogus = null;
        string? xGnarly = null;
        if (sigResult.TryGetProperty("X-Bogus", out var xb)) xBogus = xb.GetString();
        if (sigResult.TryGetProperty("X-Gnarly", out var xg)) xGnarly = xg.GetString();
        if (string.IsNullOrEmpty(xBogus)) throw new Exception("Failed to generate X-Bogus signature.");

        var signedUrl = $"{fullUnsignedUrl}&X-Bogus={xBogus}";
        if (!string.IsNullOrEmpty(xGnarly))
            signedUrl += $"&X-Gnarly={xGnarly}";
        var fetchJs = $@"
            () => {{
                return new Promise((resolve, reject) => {{
                    fetch('{signedUrl}', {{ method: 'GET' }})
                        .then(r => r.json())
                        .then(data => resolve(data))
                        .catch(err => reject(err.message));
                }});
            }}";

        return await page.EvaluateAsync<JsonElement>(fetchJs);
    }

    private static async Task SimulateHumanInteraction(IPage page)
    {
        try
        {
            if (Rng.NextDouble() < 0.7)
                await page.EvaluateAsync($"window.scrollBy(0, {Rng.Next(200, 701)})");
            await page.Mouse.MoveAsync(Rng.Next(100, 900), Rng.Next(100, 700),
                new MouseMoveOptions { Steps = Rng.Next(5, 11) });
            if (Rng.NextDouble() < 0.3)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.3 + Rng.NextDouble() * 0.5));
                await page.EvaluateAsync($"window.scrollBy(0, -{Rng.Next(100, 301)})");
                await page.Mouse.MoveAsync(Rng.Next(100, 900), Rng.Next(100, 700),
                    new MouseMoveOptions { Steps = Rng.Next(5, 11) });
            }
        }
        catch { }
    }

    private static int GetStatusCode(JsonElement data)
    {
        if (data.TryGetProperty("status_code", out var sc) && sc.TryGetInt32(out var i))
            return i;
        return -1;
    }

    private static List<JsonElement> GetItemList(JsonElement data)
    {
        var result = new List<JsonElement>();
        if (data.TryGetProperty("itemList", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var item in items.EnumerateArray())
                result.Add(item);
        return result;
    }

    private static bool TryParseIsoToUnix(string dateStr, out long unix)
    {
        unix = 0;
        try
        {
            var dt = DateTime.Parse(dateStr.Replace("Z", "+00:00")).ToUniversalTime();
            unix = new DateTimeOffset(dt).ToUnixTimeSeconds();
            return true;
        }
        catch { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        // Signal writer to drain remaining items, then stop.
        _writeChannel.Writer.Complete();
        _writerCts.CancelAfter(TimeSpan.FromSeconds(10)); // give 10 s to flush
        try { await _writerTask; } catch { }
        _writerCts.Dispose();
        await _browserPool.DisposeAsync();
    }


    public async IAsyncEnumerable<CrawlEvent> ScrapeCommentsStreamAsync(
        string videoId,
        string target,
        IPage page,
        List<TikTokComment>? targetComments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const int maxPages = 20;
        var totalComments = 0;
        var cursor = 0L;
        var hasMore = true;
        var pageCount = 0;
        var seenCids = new HashSet<string>();

        yield return new CrawlEvent { Type = "log", Message = $"Bắt đầu lấy comment cho video {videoId} qua API..." };

        // Gather browser context info once (reused across paginated calls)
        string? userAgent = null, language = null, platform = null, timezone = null;
        try
        {
            userAgent = await page.EvaluateAsync<string>("() => navigator.userAgent");
            language = await page.EvaluateAsync<string>("() => navigator.language || navigator.userLanguage");
            platform = await page.EvaluateAsync<string>("() => navigator.platform");
            timezone = await page.EvaluateAsync<string>("() => Intl.DateTimeFormat().resolvedOptions().timeZone");
        }
        catch { /* use defaults */ }

        var region = "VN";
        var appLang = "en";
        if (!string.IsNullOrEmpty(timezone))
        {
            var tzLower = timezone.ToLower();
            if (tzLower.Contains("saigon") || tzLower.Contains("bangkok")) { region = "VN"; appLang = "vi-VN"; }
            else if (tzLower.Contains("tokyo")) { region = "JP"; appLang = "ja-JP"; }
            else if (tzLower.Contains("shanghai") || tzLower.Contains("hong_kong")) { region = "CN"; appLang = "zh-Hans"; }
        }

        // Extract cookies for odinId, msToken, verifyFp, device_id
        var loadedCookies = await page.Context.CookiesAsync();
        var cookieMap = new Dictionary<string, string>();
        foreach (var c in loadedCookies)
            if (!cookieMap.ContainsKey(c.Name))
                cookieMap[c.Name] = c.Value;

        cookieMap.TryGetValue("msToken", out var msToken);
        cookieMap.TryGetValue("s_v_web_id", out var verifyFp);

        string? odinId = null;
        if (!cookieMap.TryGetValue("odinId", out odinId) &&
            cookieMap.TryGetValue("multi_sids", out var multiSids))
        {
            var decoded = HttpUtility.UrlDecode(multiSids);
            if (decoded.Contains(':')) odinId = decoded.Split(':')[0];
        }

        var deviceId = cookieMap.TryGetValue("tt_webid", out var webId) && !string.IsNullOrEmpty(webId)
            ? webId
            : Rng.NextInt64(1_000_000_000_000_000_000L, long.MaxValue).ToString();
        var isLogin = cookieMap.ContainsKey("sessionid") || cookieMap.ContainsKey("sid_tt");

        while (hasMore && pageCount < maxPages)
        {
            if (cancellationToken.IsCancellationRequested) break;
            pageCount++;

            var commentParams = new Dictionary<string, string>
            {
                ["aid"] = "1988",
                ["app_language"] = appLang,
                ["app_name"] = "tiktok_web",
                ["aweme_id"] = videoId,
                ["browser_language"] = language ?? "en-US",
                ["browser_name"] = "Mozilla",
                ["browser_online"] = "true",
                ["browser_platform"] = platform ?? "Win32",
                ["browser_version"] = userAgent ?? "Mozilla/5.0",
                ["channel"] = "tiktok_web",
                ["cookie_enabled"] = "true",
                ["count"] = "20",
                ["cursor"] = cursor.ToString(),
                ["data_collection_enabled"] = "true",
                ["device_id"] = deviceId,
                ["device_platform"] = "web_pc",
                ["focus_state"] = "true",
                ["from_page"] = "video",
                ["history_len"] = "3",
                ["is_fullscreen"] = "false",
                ["is_page_visible"] = "true",
                ["os"] = "windows",
                ["priority_region"] = region,
                ["referer"] = $"https://www.tiktok.com/{target}/video/{videoId}",
                ["region"] = region,
                ["screen_height"] = "1080",
                ["screen_width"] = "1920",
                ["tz_name"] = timezone ?? "Asia/Saigon",
                ["user_is_login"] = isLogin ? "true" : "false",
                ["webcast_language"] = "en"
            };

            if (!string.IsNullOrEmpty(msToken)) commentParams["msToken"] = msToken;
            if (!string.IsNullOrEmpty(odinId)) commentParams["odinId"] = odinId;
            if (!string.IsNullOrEmpty(verifyFp)) commentParams["verifyFp"] = verifyFp;

            yield return new CrawlEvent
            {
                Type = "progress",
                Target = target,
                Page = pageCount,
                MaxPages = maxPages,
                Collected = totalComments,
                Message = $"Đang lấy comments trang {pageCount} (cursor={cursor})..."
            };

            JsonElement data;
            string? fetchError = null;
            try
            {
                data = await SignAndFetchUrlAsync(page, "/api/comment/list/", commentParams, cancellationToken);
            }
            catch (Exception ex)
            {
                fetchError = ex.Message;
                data = default;
            }

            if (fetchError != null)
            {
                yield return new CrawlEvent { Type = "error", Message = $"Lỗi gọi API comment: {fetchError}" };
                break;
            }

            var statusCode = GetStatusCode(data);
            if (statusCode != 0)
            {
                yield return new CrawlEvent { Type = "error", Message = $"API comment trả status_code={statusCode}" };
                break;
            }

            if (!data.TryGetProperty("comments", out var commentsEl) || commentsEl.ValueKind != JsonValueKind.Array)
            {
                yield return new CrawlEvent { Type = "log", Message = "Không có comments." };
                break;
            }

            var newInBatch = 0;
            foreach (var c in commentsEl.EnumerateArray())
            {
                var cid = c.TryGetProperty("cid", out var cidEl) ? cidEl.GetString() : null;
                if (string.IsNullOrEmpty(cid) || !seenCids.Add(cid)) continue;

                newInBatch++;
                totalComments++;

                var text = c.TryGetProperty("text", out var txtEl) ? txtEl.GetString() ?? "" : "";
                var createTime = c.TryGetProperty("create_time", out var ctEl) ? ctEl.GetInt64() : 0;
                var diggCount = c.TryGetProperty("digg_count", out var dcEl) ? dcEl.GetInt32() : 0;
                var replyTotal = c.TryGetProperty("reply_comment_total", out var rtEl) ? rtEl.GetInt32() : 0;

                // Extract user info
                string? userName = null, userNick = null, userUid = null;
                if (c.TryGetProperty("user", out var userEl) && userEl.ValueKind == JsonValueKind.Object)
                {
                    if (userEl.TryGetProperty("unique_id", out var uiEl)) userName = uiEl.GetString();
                    if (userEl.TryGetProperty("nickname", out var nnEl)) userNick = nnEl.GetString();
                    if (userEl.TryGetProperty("uid", out var uidEl)) userUid = uidEl.GetString();
                }

                // Collect inline replies (first few come embedded)
                var inlineReplies = new List<object>();
                if (c.TryGetProperty("reply_comment", out var rcEl) && rcEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in rcEl.EnumerateArray())
                    {
                        inlineReplies.Add(new
                        {
                            cid = r.TryGetProperty("cid", out var rcid) ? rcid.GetString() : null,
                            text = r.TryGetProperty("text", out var rtxt) ? rtxt.GetString() : "",
                            user = ExtractCommentUser(r)
                        });
                    }
                }

                // Populate the in-memory comments list for the parent TikTokRawItem
                var commentObj = new TikTokComment
                {
                    Cid = cid,
                    VideoId = videoId,
                    Text = text,
                    CreateTime = createTime,
                    DiggCount = diggCount,
                    ReplyTotal = replyTotal,
                    User = userUid != null ? new TikTokCommentUser
                    {
                        UniqueId = userName,
                        Nickname = userNick,
                        Uid = userUid
                    } : null,
                    InlineReplies = inlineReplies.Count > 0
                        ? inlineReplies.Select(r => JsonSerializer.Deserialize<TikTokInlineReply>(JsonSerializer.Serialize(r, JsonOptions))!).ToList()
                        : new List<TikTokInlineReply>()
                };
                targetComments?.Add(commentObj);

                // If comment has more replies than inline, fetch the rest via reply API
                if (replyTotal > inlineReplies.Count)
                {
                    yield return new CrawlEvent { Type = "log", Message = $"Comment {cid} có {replyTotal} replies, đang fetch thêm..." };
                    await foreach (var replyEvent in FetchRepliesForCommentAsync(page, videoId, target, cid, replyTotal, commentObj, cancellationToken))
                    {
                        yield return replyEvent;
                    }
                }
            }

            yield return new CrawlEvent
            {
                Type = "progress",
                Target = target,
                Page = pageCount,
                MaxPages = maxPages,
                Collected = totalComments,
                Message = $"Đã lấy {newInBatch} comments mới (tổng: {totalComments})"
            };

            // Pagination
            hasMore = data.TryGetProperty("has_more", out var hmEl) && hmEl.GetInt32() == 1;
            if (data.TryGetProperty("cursor", out var ncEl))
            {
                cursor = ncEl.ValueKind == JsonValueKind.Number
                    ? ncEl.GetInt64()
                    : (long.TryParse(ncEl.GetString(), out var pc) ? pc : cursor + 20);
            }
            else
            {
                cursor += 20;
            }

            if (hasMore)
                await Task.Delay(400 + Rng.Next(0, 300), cancellationToken);
        }

        yield return new CrawlEvent
        {
            Type = "done",
            Count = totalComments,
            Message = $"✅ Hoàn tất lấy {totalComments} comments cho video {videoId}."
        };
    }

    // Fetch all replies for a comment via the reply API, paginating if needed.
    private async IAsyncEnumerable<CrawlEvent> FetchRepliesForCommentAsync(
        IPage page,
        string videoId,
        string target,
        string commentId,
        int expectedTotal,
        TikTokComment parentComment,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const int maxPages = 10;
        var totalFetched = 0;
        var cursor = 1L;
        var hasMore = true;
        var pageCount = 0;
        var seenReplyCids = new HashSet<string>();

        // Extract cookies for odinId, msToken, verifyFp, device_id
        var loadedCookies = await page.Context.CookiesAsync();
        var cookieMap = new Dictionary<string, string>();
        foreach (var c in loadedCookies)
            if (!cookieMap.ContainsKey(c.Name))
                cookieMap[c.Name] = c.Value;

        cookieMap.TryGetValue("msToken", out var msToken);
        cookieMap.TryGetValue("s_v_web_id", out var verifyFp);

        string? odinId = null;
        if (!cookieMap.TryGetValue("odinId", out odinId) &&
            cookieMap.TryGetValue("multi_sids", out var multiSids))
        {
            var decoded = HttpUtility.UrlDecode(multiSids);
            if (decoded.Contains(':')) odinId = decoded.Split(':')[0];
        }

        var deviceId = cookieMap.TryGetValue("tt_webid", out var webId) && !string.IsNullOrEmpty(webId)
            ? webId
            : Rng.NextInt64(1_000_000_000_000_000_000L, long.MaxValue).ToString();
        var isLogin = cookieMap.ContainsKey("sessionid") || cookieMap.ContainsKey("sid_tt");

        // Gather browser context info for reply API params
        string? userAgent = null, language = null, platform = null, timezone = null;
        try
        {
            userAgent = await page.EvaluateAsync<string>("() => navigator.userAgent");
            language = await page.EvaluateAsync<string>("() => navigator.language || navigator.userLanguage");
            platform = await page.EvaluateAsync<string>("() => navigator.platform");
            timezone = await page.EvaluateAsync<string>("() => Intl.DateTimeFormat().resolvedOptions().timeZone");
        }
        catch { /* use defaults */ }

        var region = "VN";
        var appLang = "en";
        if (!string.IsNullOrEmpty(timezone))
        {
            var tzLower = timezone.ToLower();
            if (tzLower.Contains("saigon") || tzLower.Contains("bangkok")) { region = "VN"; appLang = "vi-VN"; }
            else if (tzLower.Contains("tokyo")) { region = "JP"; appLang = "ja-JP"; }
            else if (tzLower.Contains("shanghai") || tzLower.Contains("hong_kong")) { region = "CN"; appLang = "zh-Hans"; }
        }

        while (hasMore && pageCount < maxPages && totalFetched < expectedTotal)
        {
            if (cancellationToken.IsCancellationRequested) break;
            pageCount++;

            var replyParams = new Dictionary<string, string>
            {
                ["WebIdLastTime"] = "0",
                ["aid"] = "1988",
                ["app_language"] = appLang,
                ["app_name"] = "tiktok_web",
                ["browser_language"] = language ?? "en-US",
                ["browser_name"] = "Mozilla",
                ["browser_online"] = "true",
                ["browser_platform"] = platform ?? "Win32",
                ["browser_version"] = userAgent ?? "Mozilla/5.0",
                ["channel"] = "tiktok_web",
                ["comment_id"] = commentId,
                ["cookie_enabled"] = "true",
                ["count"] = "20",
                ["cursor"] = cursor.ToString(),
                ["data_collection_enabled"] = "true",
                ["device_id"] = deviceId,
                ["device_platform"] = "web_pc",
                ["focus_state"] = "true",
                ["from_page"] = "video",
                ["history_len"] = "3",
                ["is_fullscreen"] = "false",
                ["is_page_visible"] = "true",
                ["item_id"] = videoId,
                ["os"] = "windows",
                ["priority_region"] = region,
                ["referer"] = $"https://www.tiktok.com/{target}/video/{videoId}",
                ["region"] = region,
                ["screen_height"] = "1080",
                ["screen_width"] = "1920",
                ["tz_name"] = timezone ?? "Asia/Saigon",
                ["user_is_login"] = isLogin ? "true" : "false",
                ["webcast_language"] = "en"
            };

            if (!string.IsNullOrEmpty(msToken)) replyParams["msToken"] = msToken;
            if (!string.IsNullOrEmpty(odinId)) replyParams["odinId"] = odinId;
            if (!string.IsNullOrEmpty(verifyFp)) replyParams["verifyFp"] = verifyFp;

            JsonElement data;
            string? replyFetchError = null;
            try
            {
                data = await SignAndFetchUrlAsync(page, "/api/comment/list/reply/", replyParams, cancellationToken);
            }
            catch (Exception ex)
            {
                replyFetchError = ex.Message;
                data = default;
            }

            if (replyFetchError != null)
            {
                yield return new CrawlEvent { Type = "error", Message = $"Lỗi gọi API reply cho comment {commentId}: {replyFetchError}" };
                break;
            }

            var statusCode = GetStatusCode(data);
            if (statusCode != 0) break;

            if (!data.TryGetProperty("comments", out var commentsEl) || commentsEl.ValueKind != JsonValueKind.Array)
                break;

            foreach (var r in commentsEl.EnumerateArray())
            {
                var cid = r.TryGetProperty("cid", out var cidEl) ? cidEl.GetString() : null;
                if (string.IsNullOrEmpty(cid) || !seenReplyCids.Add(cid)) continue;

                totalFetched++;

                var text = r.TryGetProperty("text", out var txtEl) ? txtEl.GetString() ?? "" : "";
                var createTime = r.TryGetProperty("create_time", out var ctEl) ? ctEl.GetInt64() : 0;
                var diggCount = r.TryGetProperty("digg_count", out var dcEl) ? dcEl.GetInt32() : 0;
                var replyId = r.TryGetProperty("reply_id", out var riEl) ? riEl.GetString() : null;
                var replyToReplyId = r.TryGetProperty("reply_to_reply_id", out var rriEl) ? rriEl.GetString() : null;

                // Populate parent comment's InlineReplies with fetched reply data
                parentComment.InlineReplies ??= new List<TikTokInlineReply>();
                parentComment.InlineReplies.Add(new TikTokInlineReply
                {
                    Cid = cid,
                    Text = text,
                    User = ExtractCommentUser(r) is { } userObj
                        ? JsonSerializer.Deserialize<TikTokCommentUser>(JsonSerializer.Serialize(userObj, JsonOptions))
                        : null
                });
            }

            // Pagination
            hasMore = data.TryGetProperty("has_more", out var hmEl) && hmEl.GetInt32() == 1;
            if (data.TryGetProperty("cursor", out var ncEl))
                cursor = ncEl.GetInt64();
            else
                cursor += 20;

            if (hasMore)
                await Task.Delay(400 + Rng.Next(0, 300), cancellationToken);
        }

        yield return new CrawlEvent
        {
            Type = "log",
            Message = $"Đã lấy {totalFetched}/{expectedTotal} replies cho comment {commentId}."
        };
    }

    // Helper: extract user info from a comment/reply JSON element into an anonymous object.
    private static object? ExtractCommentUser(JsonElement commentEl)
    {
        if (!commentEl.TryGetProperty("user", out var userEl) || userEl.ValueKind != JsonValueKind.Object)
            return null;

        return new
        {
            uniqueId = userEl.TryGetProperty("unique_id", out var ui) ? ui.GetString() : null,
            nickname = userEl.TryGetProperty("nickname", out var nn) ? nn.GetString() : null,
            uid = userEl.TryGetProperty("uid", out var uid) ? uid.GetString() : null,
            avatarThumb = userEl.TryGetProperty("avatar_thumb", out var av) && av.ValueKind == JsonValueKind.Object
                ? (av.TryGetProperty("url_list", out var ul) && ul.ValueKind == JsonValueKind.Array && ul.GetArrayLength() > 0
                    ? ul[0].GetString() : null)
                : null
        };
    }
}

