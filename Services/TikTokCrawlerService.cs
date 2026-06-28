using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Playwright;
using SocialCrawler.Models;

namespace SocialCrawler.Services;

public class TikTokCrawlerService
{
    private const int MaxPages = 50;
    private const int MaxFetchRetries = 2;
    private const double FetchRetryDelay = 2.0;

    private static readonly Random Rng = new();

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

    public async IAsyncEnumerable<CrawlEvent> ScrapeStreamAsync(
        List<string> targets,
        string? startDateStr,
        string? endDateStr,
        string periodStr,
        List<PlaywrightCookie>? cookies,
        [EnumeratorCancellation] CancellationToken cancellationToken)
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

        var allRaws = new List<TikTokRawItem>();
        var abortAll = false;
        var completedTargets = new HashSet<string>();
        const int maxBrowserRetries = 3;
        var browserAttempt = 0;

        while (browserAttempt < maxBrowserRetries)
        {
            browserAttempt++;
            var headless = (Environment.GetEnvironmentVariable("HEADLESS") ?? "false").ToLower() != "false";
            yield return new CrawlEvent { Type = "log", Message = $"Khởi động trình duyệt (lần {browserAttempt}, headless={headless})..." };

            using var playwright = await Playwright.CreateAsync();
            var launchOptions = new BrowserTypeLaunchOptions
            {
                Headless = headless,
                Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage" }
            };

            bool browserCrashed = false;

            await using var browser = await playwright.Chromium.LaunchAsync(launchOptions);
            await using var context = await browser.NewContextAsync();

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
                    HttpOnly = c.HttpOnly ?? false
                }).ToList();
                await context.AddCookiesAsync(pwCookies);
            }

            var page = await context.NewPageAsync();

            foreach (var target in targets)
            {
                if (abortAll || cancellationToken.IsCancellationRequested)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        yield return new CrawlEvent { Type = "log", Message = "🚫 Tiến trình đã bị hủy bởi người dùng." };
                        abortAll = true;
                    }
                    break;
                }

                if (completedTargets.Contains(target)) continue;
                yield return new CrawlEvent { Type = "log", Message = $"--- Bắt đầu thu thập mục tiêu: {target} ---" };

                string? secUid = null;
                var cursor = 0L;
                var hasMore = true;
                var seenIds = new HashSet<string?>();
                var pageCount = 0;
                var targetRaws = new List<TikTokRawItem>();
                var targetHasError = false;

                yield return new CrawlEvent { Type = "log", Message = $"Đang trích xuất secUid cho: {target}..." };

                CrawlEvent? secUidErrorEvent = null;
                var secUidAbortAll = false;
                var secUidContinue = false;

                try
                {
                    secUid = await ExtractSecUidAsync(page, target);
                }
                catch (Exception e)
                {
                    var errMsg = e.Message;
                    if (errMsg.Contains("CAPTCHA_DETECTED"))
                    {
                        secUidErrorEvent = new CrawlEvent { Type = "error", Message = "CAPTCHA_DETECTED: Phát hiện Captcha! Vui lòng đăng nhập TikTok trên trình duyệt để vượt captcha (hoặc cập nhật cookies mới) rồi chạy lại." };
                        secUidAbortAll = true;
                    }
                    else if (errMsg.Contains("LOGIN_REQUIRED"))
                    {
                        secUidErrorEvent = new CrawlEvent { Type = "error", Message = "LOGIN_REQUIRED: Bị chuyển hướng sang trang đăng nhập. Cookies có thể đã hết hạn hoặc bị block. Vui lòng cập nhật cookies mới." };
                        secUidAbortAll = true;
                    }
                    else
                    {
                        secUidErrorEvent = new CrawlEvent { Type = "error", Message = $"Không thể trích xuất secUid cho: {target}. Chi tiết: {e.Message}" };
                        secUidContinue = true;
                    }
                }

                if (secUidErrorEvent != null) yield return secUidErrorEvent;
                if (secUidAbortAll) { abortAll = true; break; }
                if (secUidContinue) continue;

                if (string.IsNullOrEmpty(secUid))
                {
                    yield return new CrawlEvent { Type = "error", Message = $"Không thể trích xuất secUid cho: {target}" };
                    continue;
                }

                yield return new CrawlEvent { Type = "log", Message = $"secUid đã giải quyết: {secUid[..Math.Min(30, secUid.Length)]}..." };

                yield return new CrawlEvent { Type = "log", Message = "Đang chờ byted_acrawler..." };
                await EnsureBytedAcrawlerAsync(page);

                yield return new CrawlEvent { Type = "log", Message = "Đang khởi tạo session params..." };
                var sessionParams = await BuildSessionParamsAsync(page, context, target, secUid);

                while (hasMore && pageCount < MaxPages)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        yield return new CrawlEvent { Type = "log", Message = "🚫 Tiến trình đã bị hủy giữa chừng." };
                        targetHasError = true;
                        abortAll = true;
                        break;
                    }

                    pageCount++;
                    sessionParams["cursor"] = cursor.ToString();

                    yield return new CrawlEvent
                    {
                        Type = "progress",
                        Target = target,
                        Page = pageCount,
                        MaxPages = MaxPages,
                        Collected = targetRaws.Count,
                        Message = $"Đang cào trang {pageCount} tại offset {cursor}... (Đã thu thập: {targetRaws.Count} video của mục tiêu này)"
                    };

                    JsonElement? data = null;
                    Exception? fetchError = null;

                    for (var attempt = 0; attempt <= MaxFetchRetries; attempt++)
                    {
                        var shouldRetryStatus = false;
                        var statusCodeToRetry = 0;

                        try
                        {
                            data = await SignAndFetchAsync(page, sessionParams);
                            var sc = GetStatusCode(data.Value);
                            if (sc == 0) break;
                            if (attempt < MaxFetchRetries)
                            {
                                shouldRetryStatus = true;
                                statusCodeToRetry = sc;
                            }
                        }
                        catch (Exception e)
                        {
                            fetchError = e;
                            if (attempt >= MaxFetchRetries)
                            {
                                browserCrashed = true;
                                break;
                            }
                        }

                        if (shouldRetryStatus)
                        {
                            yield return new CrawlEvent { Type = "log", Message = $"⚠️ API trả lỗi {statusCodeToRetry}. Thử lại ({attempt + 1}/{MaxFetchRetries})..." };
                            await Task.Delay(TimeSpan.FromSeconds(FetchRetryDelay * (attempt + 1)));
                        }

                        if (fetchError != null && attempt < MaxFetchRetries)
                        {
                            yield return new CrawlEvent { Type = "log", Message = $"⚠️ Lỗi fetch trang {pageCount}: {fetchError.Message}. Thử lại..." };
                            await Task.Delay(TimeSpan.FromSeconds(FetchRetryDelay));
                        }
                    }

                    if (browserCrashed || (fetchError != null && data == null)) break;

                    var statusCode = data.HasValue ? GetStatusCode(data.Value) : -1;
                    if (statusCode != 0)
                    {
                        var state = await page.EvaluateAsync<string?>(JsCheckCaptcha);
                        if (state == "CAPTCHA")
                        {
                            yield return new CrawlEvent { Type = "error", Message = "CAPTCHA_DETECTED: Phát hiện Captcha trong quá trình tải danh sách!" };
                            targetHasError = true;
                            abortAll = true;
                            break;
                        }
                        else if (state == "LOGIN")
                        {
                            yield return new CrawlEvent { Type = "error", Message = "LOGIN_REQUIRED: Bị văng ra trang đăng nhập trong lúc cào." };
                            targetHasError = true;
                            abortAll = true;
                            break;
                        }

                        yield return new CrawlEvent { Type = "error", Message = $"API thất bại sau {MaxFetchRetries} lần thử: status_code={statusCode}" };
                        targetHasError = true;
                        break;
                    }

                    var items = GetItemList(data!.Value);
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
                            raw.Transcript = await CrawlLogger.LogItemAsync(target, raw, item);
                            targetRaws.Add(raw);

                            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                            // Log raw JSON
                            JsonSerializerOptions JsonOptions = new()
                            {
                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                                WriteIndented = true
                            };
                            var rawDir = Path.Combine("output", "tiktok", "raw");
                            Directory.CreateDirectory(rawDir);
                            var rawJson = JsonSerializer.Serialize(raw.Transcript, JsonOptions);
                            await File.WriteAllTextAsync(
                                Path.Combine(rawDir, $"{timestamp}_{target}_{raw.Id}_raw.json"),
                                rawJson, Encoding.UTF8);
                        }
                    }

                    if (oldestInPage.HasValue && oldestInPage.Value < startTime)
                    {
                        var cutoffFmt = DateTimeOffset.FromUnixTimeSeconds(startTime).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                        yield return new CrawlEvent { Type = "log", Message = $"Video cũ nhất trên trang đã cũ hơn mốc thời gian {cutoffFmt}. Kết thúc mục tiêu này." };
                        break;
                    }

                    var hasMoreVal = data!.Value.TryGetProperty("hasMore", out var hm);
                    hasMore = hasMoreVal && (hm.ValueKind == JsonValueKind.True || (hm.ValueKind == JsonValueKind.Number && hm.GetInt32() == 1));

                    if (data.Value.TryGetProperty("cursor", out var nc))
                    {
                        long.TryParse(nc.ValueKind == JsonValueKind.Number ? nc.GetInt64().ToString() : nc.GetString(), out var nextCursor);
                        cursor = nextCursor > 0 ? nextCursor : cursor + items.Count;
                    }
                    else
                    {
                        cursor += items.Count;
                    }

                    var delay = 2.0 + Rng.NextDouble() * 2.0;
                    yield return new CrawlEvent { Type = "log", Message = $"Tạm nghỉ {delay:F2}s và giả lập tương tác..." };
                    await SimulateHumanInteraction(page);
                    await Task.Delay(TimeSpan.FromSeconds(delay));
                }

                if (abortAll) break;

                completedTargets.Add(target);
                allRaws.AddRange(targetRaws);

                yield return new CrawlEvent
                {
                    Type = "log",
                    Message = targetHasError
                        ? $"⚠️ Thu thập không trọn vẹn mục tiêu {target} (chỉ được {targetRaws.Count} video do lỗi)."
                        : $"✅ Hoàn tất mục tiêu {target} (thu thập {targetRaws.Count} video)."
                };

                if (browserCrashed) break;
            }

            if (!browserCrashed || browserAttempt >= maxBrowserRetries) break;

            yield return new CrawlEvent { Type = "log", Message = $"⚠️ Mất kết nối trình duyệt. Tự động khởi động lại (lần {browserAttempt}/{maxBrowserRetries})..." };
            await Task.Delay(3000);
        }

        yield return new CrawlEvent { Type = "log", Message = "Đang đóng trình duyệt..." };

        yield return new CrawlEvent
        {
            Type = "done",
            Count = allRaws.Count,
            RawItems = allRaws
        };
    }

    private static async Task<string?> ExtractSecUidAsync(IPage page, string target)
    {
        if (target.StartsWith("MS4wLjABAAAA") && target.Length > 50) return target;

        var username = ResolveUsername(target);
        var usernameClean = username.TrimStart('@');
        var profileUrl = $"https://www.tiktok.com/{username}";

        Console.WriteLine($"[secUid] Navigating to {profileUrl}");
        await page.GotoAsync(profileUrl, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });

        try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 }); } catch { }

        var state = await page.EvaluateAsync<string?>(JsCheckCaptcha);
        if (state == "CAPTCHA") throw new Exception("CAPTCHA_DETECTED");
        if (state == "LOGIN") throw new Exception("LOGIN_REQUIRED");

        await SimulateHumanInteraction(page);

        try { await page.WaitForFunctionAsync(JsWaitForUid, null, new() { Timeout = 45000 }); } catch { }

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

    private static async Task EnsureBytedAcrawlerAsync(IPage page)
    {
        try { await page.WaitForFunctionAsync("window.byted_acrawler !== undefined", null, new() { Timeout = 15000 }); }
        catch
        {
            await page.GotoAsync("https://www.tiktok.com", new PageGotoOptions { Timeout = 60000 });
            await page.WaitForFunctionAsync("window.byted_acrawler !== undefined", null, new() { Timeout = 30000 });
        }
    }

    private static async Task<Dictionary<string, string>> BuildSessionParamsAsync(IPage page, IBrowserContext context, string target, string secUid)
    {
        var userAgent = await page.EvaluateAsync<string>("() => navigator.userAgent");
        var language = await page.EvaluateAsync<string>("() => navigator.language || navigator.userLanguage");
        var platform = await page.EvaluateAsync<string>("() => navigator.platform");
        var timezone = await page.EvaluateAsync<string>("() => Intl.DateTimeFormat().resolvedOptions().timeZone");

        var deviceId = (Rng.NextInt64(1_000_000_000_000_000_000L, long.MaxValue)).ToString();
        var historyLen = Rng.Next(1, 11).ToString();
        var screenHeight = Rng.Next(600, 1081).ToString();
        var screenWidth = Rng.Next(800, 1921).ToString();

        var loadedCookies = await context.CookiesAsync();
        var cookieMap = new Dictionary<string, string>();
        foreach (var c in loadedCookies)
        {
            if (!cookieMap.ContainsKey(c.Name))
                cookieMap[c.Name] = c.Value;
        }

        cookieMap.TryGetValue("msToken", out var msToken);

        string? odinId = null;
        if (!cookieMap.TryGetValue("odinId", out odinId) && cookieMap.TryGetValue("multi_sids", out var multiSids))
        {
            var decoded = HttpUtility.UrlDecode(multiSids);
            if (decoded.Contains(':')) odinId = decoded.Split(':')[0];
        }

        cookieMap.TryGetValue("s_v_web_id", out var verifyFp);
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
            ["screen_height"] = screenHeight,
            ["screen_width"] = screenWidth,
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

    private static async Task<JsonElement> SignAndFetchAsync(IPage page, Dictionary<string, string> sessionParams)
    {
        var qs = string.Join("&", sessionParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        var fullUnsignedUrl = $"https://www.tiktok.com/api/post/item_list/?{qs}";

        var sigResult = await page.EvaluateAsync<JsonElement>($"() => window.byted_acrawler.frontierSign(\"{fullUnsignedUrl}\")");
        string? xBogus = null;
        if (sigResult.TryGetProperty("X-Bogus", out var xb)) xBogus = xb.GetString();
        if (string.IsNullOrEmpty(xBogus)) throw new Exception("Failed to generate X-Bogus signature.");

        var signedUrl = $"{fullUnsignedUrl}&X-Bogus={xBogus}";
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
            await page.Mouse.MoveAsync(Rng.Next(100, 900), Rng.Next(100, 700), new MouseMoveOptions { Steps = Rng.Next(5, 11) });
            if (Rng.NextDouble() < 0.3)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.3 + Rng.NextDouble() * 0.5));
                await page.EvaluateAsync($"window.scrollBy(0, -{Rng.Next(100, 301)})");
                await page.Mouse.MoveAsync(Rng.Next(100, 900), Rng.Next(100, 700), new MouseMoveOptions { Steps = Rng.Next(5, 11) });
            }
        }
        catch { }
    }

    private static int GetStatusCode(JsonElement data)
    {
        if (data.TryGetProperty("status_code", out var sc))
        {
            if (sc.TryGetInt32(out var i)) return i;
        }
        return -1;
    }

    private static List<JsonElement> GetItemList(JsonElement data)
    {
        var result = new List<JsonElement>();
        if (data.TryGetProperty("itemList", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var item in items.EnumerateArray()) result.Add(item);
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
}