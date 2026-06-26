using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SocialCrawler.Models;
using SocialCrawler.Services;

namespace SocialCrawler.Controllers;

[ApiController]
public class CrawlController : ControllerBase
{
    private readonly FacebookCrawlerService _facebookCrawler;
    private readonly TikTokCrawlerService _tikTokCrawler;
    private readonly SessionValidationService _sessionValidation;
    private readonly CrawlStateService _crawlState;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public CrawlController(
        FacebookCrawlerService facebookCrawler,
        TikTokCrawlerService tikTokCrawler,
        SessionValidationService sessionValidation,
        CrawlStateService crawlState)
    {
        _facebookCrawler = facebookCrawler;
        _tikTokCrawler = tikTokCrawler;
        _sessionValidation = sessionValidation;
        _crawlState = crawlState;
    }

    [HttpGet("/health", Name = "Health")]
    [Tags("System")]
    public IActionResult Health() =>
        Ok(new { status = "ok", service = "social-crawler" });

    [HttpGet("/status", Name = "Status")]
    [Tags("System")]
    public IActionResult Status()
    {
        var (state, target, elapsed, locked) = _crawlState.GetStatus();
        return Ok(new { state, target, elapsed_seconds = elapsed, locked });
    }

    [HttpPost("/cancel", Name = "Cancel")]
    [Tags("System")]
    public IActionResult Cancel()
    {
        if (_crawlState.IsRunning)
        {
            _crawlState.Cancel();
            return Ok(new { status = "ok", message = "✅ Đã phát tín hiệu hủy tiến trình hiện tại. Lock sẽ được giải phóng trong giây lát." });
        }
        return Ok(new { status = "ok", message = "Không có tiến trình nào đang chạy." });
    }

    [HttpPost("/crawl/facebook", Name = "CrawlFacebook")]
    [Tags("Crawlers")]
    public IActionResult CrawlFacebook([FromBody] CrawlRequest req)
    {
        var targets = ResolveTargets(req);
        if (targets == null)
            return BadRequest(new { detail = "Missing target or targets parameter" });

        return new PushStreamResult(async (stream, ct) =>
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);

            var isWaiting = _crawlState.CrawlLock.CurrentCount == 0;
            if (isWaiting)
            {
                var waitMsg = new CrawlEvent { Type = "log", Message = "⚠️ Hàng đợi bận: Có tiến trình cào khác đang chạy. Đang chờ đến lượt..." };
                await writer.WriteAsync(FormatSse(waitMsg));
                await writer.FlushAsync();
            }

            var waitStart = DateTime.UtcNow;
            await _crawlState.CrawlLock.WaitAsync(ct);
            try
            {
                if (isWaiting)
                {
                    var elapsed = Math.Round((DateTime.UtcNow - waitStart).TotalSeconds, 1);
                    var readyMsg = new CrawlEvent { Type = "log", Message = $"✅ Hàng đợi trống (đã chờ {elapsed}s). Bắt đầu tiến trình cào mới." };
                    await writer.WriteAsync(FormatSse(readyMsg));
                    await writer.FlushAsync();
                }

                _crawlState.ResetCancellation();
                _crawlState.SetRunning(string.Join(",", targets));

                var cookies = ParseCookies(req.Cookies, ".facebook.com");
                var scrollConfig = ScrollConfig.FromRequest(req);
                var stopUrls = req.StopUrls != null ? new HashSet<string>(req.StopUrls) : null;

                var gen = _facebookCrawler.ScrapeAsync(
                    targets, req.StartDate, req.EndDate, req.FacebookMaxPosts,
                    cookies, _crawlState.CancellationToken, stopUrls, scrollConfig);

                await foreach (var evt in gen.WithCancellation(ct))
                {
                    await writer.WriteAsync(FormatSse(evt));
                    await writer.FlushAsync();
                    if (ct.IsCancellationRequested) break;
                }
            }
            catch (Exception e)
            {
                var errEvt = new CrawlEvent { Type = "error", Message = e.Message };
                await writer.WriteAsync(FormatSse(errEvt));
                var doneEvt = new CrawlEvent { Type = "done", Count = 0, Videos = new(), Aborted = true };
                await writer.WriteAsync(FormatSse(doneEvt));
                await writer.FlushAsync();
            }
            finally
            {
                _crawlState.SetIdle();
                _crawlState.CrawlLock.Release();
            }
        });
    }

    [HttpPost("/crawl/tiktok", Name = "CrawlTikTok")]
    [HttpPost("/crawl", Name = "CrawlTikTokCompat")]
    [Tags("Crawlers")]
    public IActionResult CrawlTikTok([FromBody] CrawlRequest req)
    {
        var targets = ResolveTargets(req);
        if (targets == null)
            return BadRequest(new { detail = "Missing target or targets parameter" });

        return new PushStreamResult(async (stream, ct) =>
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);

            var isWaiting = _crawlState.CrawlLock.CurrentCount == 0;
            if (isWaiting)
            {
                var waitMsg = new CrawlEvent { Type = "log", Message = "⚠️ Hàng đợi bận: Có tiến trình cào khác đang chạy. Đang chờ đến lượt..." };
                await writer.WriteAsync(FormatSse(waitMsg));
                await writer.FlushAsync();
            }

            var waitStart = DateTime.UtcNow;
            await _crawlState.CrawlLock.WaitAsync(ct);
            try
            {
                if (isWaiting)
                {
                    var elapsed = Math.Round((DateTime.UtcNow - waitStart).TotalSeconds, 1);
                    var readyMsg = new CrawlEvent { Type = "log", Message = $"✅ Hàng đợi trống (đã chờ {elapsed}s). Bắt đầu tiến trình cào mới." };
                    await writer.WriteAsync(FormatSse(readyMsg));
                    await writer.FlushAsync();
                }

                _crawlState.ResetCancellation();
                _crawlState.SetRunning(string.Join(",", targets));

                var cookies = ParseCookies(req.Cookies, ".tiktok.com");

                var gen = _tikTokCrawler.ScrapeStreamAsync(
                    targets, req.StartDate, req.EndDate, req.Period,
                    cookies, _crawlState.CancellationToken);

                await foreach (var evt in gen.WithCancellation(ct))
                {
                    await writer.WriteAsync(FormatSse(evt));
                    await writer.FlushAsync();
                    if (ct.IsCancellationRequested) break;
                }
            }
            catch (Exception e)
            {
                var errEvt = new CrawlEvent { Type = "error", Message = e.Message };
                await writer.WriteAsync(FormatSse(errEvt));
                var doneEvt = new CrawlEvent { Type = "done", Count = 0, Videos = new(), Aborted = true };
                await writer.WriteAsync(FormatSse(doneEvt));
                await writer.FlushAsync();
            }
            finally
            {
                _crawlState.SetIdle();
                _crawlState.CrawlLock.Release();
            }
        });
    }

    [HttpPost("/validate_session", Name = "ValidateSession")]
    [Tags("System")]
    public async Task<IActionResult> ValidateSession([FromBody] ValidateSessionRequest req)
    {
        var cookies = ParseCookiesFromElement(req.SessionData, ".tiktok.com");
        var (ok, message) = await _sessionValidation.ValidateTikTokSessionAsync(cookies);
        return Ok(new { ok, message });
    }

    [HttpPost("/validate_facebook_session", Name = "ValidateFacebookSession")]
    [Tags("System")]
    public async Task<IActionResult> ValidateFacebookSession([FromBody] ValidateSessionRequest req)
    {
        var cookies = ParseCookiesFromElement(req.SessionData, ".facebook.com");
        var (ok, message) = await _sessionValidation.ValidateFacebookSessionAsync(cookies);
        return Ok(new { ok, message });
    }

    private static List<string>? ResolveTargets(CrawlRequest req)
    {
        if (req.Targets != null && req.Targets.Count > 0) return req.Targets;
        if (!string.IsNullOrEmpty(req.Target)) return new List<string> { req.Target };
        return null;
    }

    private static List<PlaywrightCookie>? ParseCookies(System.Text.Json.JsonElement? cookiesEl, string defaultDomain)
    {
        if (!cookiesEl.HasValue) return null;
        return CookieService.ParseCookiesFromElement(cookiesEl.Value, defaultDomain);
    }

    private static List<PlaywrightCookie> ParseCookiesFromElement(System.Text.Json.JsonElement el, string defaultDomain)
        => CookieService.ParseCookiesFromElement(el, defaultDomain);

    private static string FormatSse(CrawlEvent evt)
    {
        var json = JsonSerializer.Serialize(evt, JsonOptions);
        return $"event: {evt.Type}\ndata: {json}\n\n";
    }
}

public class PushStreamResult : IActionResult
{
    private readonly Func<Stream, CancellationToken, Task> _callback;

    public PushStreamResult(Func<Stream, CancellationToken, Task> callback)
    {
        _callback = callback;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        await _callback(response.Body, context.HttpContext.RequestAborted);
    }
}
