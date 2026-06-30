using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SocialCrawler.Models;
using SocialCrawler.Services;

namespace SocialCrawler.Controllers;

[ApiController]
[Route("api/crawl")]
public class CrawlEngineController : ControllerBase
{
    private readonly FacebookCrawlerService _facebookCrawler;
    private readonly TikTokCrawlerService _tikTokCrawler;
    private readonly CrawlStateService _crawlState;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CrawlEngineController(
        FacebookCrawlerService facebookCrawler,
        TikTokCrawlerService tikTokCrawler,
        CrawlStateService crawlState)
    {
        _facebookCrawler = facebookCrawler;
        _tikTokCrawler = tikTokCrawler;
        _crawlState = crawlState;
    }

    [HttpPost("facebook")]
    public async Task<IActionResult> CrawlFacebook([FromBody] CrawlEngineRequest req)
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
                var waitMsg = new { type = "log", message = "⚠️ Hàng đợi bận: Có tiến trình cào khác đang chạy. Đang chờ đến lượt..." };
                await writer.WriteAsync($"event: log\ndata: {JsonSerializer.Serialize(waitMsg, JsonOptions)}\n\n");
                await writer.FlushAsync();
            }

            await _crawlState.CrawlLock.WaitAsync(ct);
            try
            {
                _crawlState.ResetCancellation();
                _crawlState.SetRunning(string.Join(",", targets));

                var cookies = ParseCookies(req.Cookies, ".facebook.com");
                var stopUrls = req.StopUrls != null ? new HashSet<string>(req.StopUrls) : null;
                var maxPosts = req.FacebookMaxPosts ?? req.FacebookMaxPostsSnake ?? 50;

                var gen = _facebookCrawler.ScrapeAsync(
                    targets, req.StartDate, req.EndDate, maxPosts,
                    cookies, _crawlState.CancellationToken, stopUrls, null, false);

                await foreach (var evt in gen.WithCancellation(ct))
                {
                    var compatEvent = new
                    {
                        type = evt.Type,
                        message = evt.Message,
                        page = evt.Page,
                        collected = evt.Collected,
                        max_pages = evt.MaxPages,
                        videos = evt.Type == "done" ? evt.Videos : null,
                        count = evt.Type == "done" ? evt.Count : null
                    };

                    await writer.WriteAsync($"event: {evt.Type}\ndata: {JsonSerializer.Serialize(compatEvent, JsonOptions)}\n\n");
                    await writer.FlushAsync();
                    if (ct.IsCancellationRequested) break;
                }
            }
            catch (Exception e)
            {
                var errEvt = new { type = "error", message = e.Message };
                await writer.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(errEvt, JsonOptions)}\n\n");
                var doneEvt = new { type = "done", count = 0, videos = new List<object>(), aborted = true };
                await writer.WriteAsync($"event: done\ndata: {JsonSerializer.Serialize(doneEvt, JsonOptions)}\n\n");
                await writer.FlushAsync();
            }
            finally
            {
                _crawlState.SetIdle();
                _crawlState.CrawlLock.Release();
            }
        });
    }

    [HttpPost("tiktok")]
    public async Task<IActionResult> CrawlTikTok([FromBody] CrawlEngineRequest req)
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
                var waitMsg = new { type = "log", message = "⚠️ Hàng đợi bận: Có tiến trình cào khác đang chạy. Đang chờ đến lượt..." };
                await writer.WriteAsync($"event: log\ndata: {JsonSerializer.Serialize(waitMsg, JsonOptions)}\n\n");
                await writer.FlushAsync();
            }

            await _crawlState.CrawlLock.WaitAsync(ct);
            var accumulatedVideos = new List<TikTokVideoData>();
            try
            {
                _crawlState.ResetCancellation();
                _crawlState.SetRunning(string.Join(",", targets));

                var cookies = ParseCookies(req.Cookies, ".tiktok.com");
                var includeComments = req.IncludeComments == true;
                var period = req.Period ?? "30 days";

                var gen = _tikTokCrawler.ScrapeStreamAsync(
                    targets, req.StartDate, req.EndDate, period,
                    cookies, _crawlState.CancellationToken, includeComments);

                await foreach (var evt in gen.WithCancellation(ct))
                {
                    if (evt.Type == "item" && evt.RawItems != null)
                    {
                        foreach (var rawItem in evt.RawItems)
                        {
                            try
                            {
                                var json = JsonSerializer.Serialize(rawItem, JsonOptions);
                                using var doc = JsonDocument.Parse(json);
                                var videoData = TikTokVideoParser.ParseToVideoData(doc.RootElement);
                                if (rawItem.Comments != null)
                                {
                                    videoData.CommentsData = rawItem.Comments;
                                }
                                accumulatedVideos.Add(videoData);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[CrawlEngine/TikTok] Error mapping item: {ex.Message}");
                            }
                        }
                        // Skip sending the raw item event to nextjs since it only expects logs/progress and a final done event.
                        continue;
                    }

                    var compatEvent = new
                    {
                        type = evt.Type,
                        message = evt.Message,
                        page = evt.Page,
                        collected = evt.Collected,
                        max_pages = evt.MaxPages,
                        videos = evt.Type == "done" ? accumulatedVideos : null,
                        count = evt.Type == "done" ? (int?)accumulatedVideos.Count : null
                    };

                    await writer.WriteAsync($"event: {evt.Type}\ndata: {JsonSerializer.Serialize(compatEvent, JsonOptions)}\n\n");
                    await writer.FlushAsync();
                    if (ct.IsCancellationRequested) break;
                }
            }
            catch (Exception e)
            {
                var errEvt = new { type = "error", message = e.Message };
                await writer.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(errEvt, JsonOptions)}\n\n");
                var doneEvt = new { type = "done", count = 0, videos = new List<TikTokVideoData>(), aborted = true };
                await writer.WriteAsync($"event: done\ndata: {JsonSerializer.Serialize(doneEvt, JsonOptions)}\n\n");
                await writer.FlushAsync();
            }
            finally
            {
                _crawlState.SetIdle();
                _crawlState.CrawlLock.Release();
            }
        });
    }

    private static List<string>? ResolveTargets(CrawlEngineRequest req)
    {
        if (req.Targets != null && req.Targets.Count > 0) return req.Targets;
        if (!string.IsNullOrEmpty(req.Target)) return new List<string> { req.Target };
        return null;
    }

    private static List<PlaywrightCookie>? ParseCookies(JsonElement? cookiesEl, string defaultDomain)
    {
        if (!cookiesEl.HasValue) return null;
        return CookieService.ParseCookiesFromElement(cookiesEl.Value, defaultDomain);
    }
}

public class CrawlEngineRequest
{
    [JsonPropertyName("targets")]
    public List<string>? Targets { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("facebookMaxPosts")]
    public int? FacebookMaxPosts { get; set; }

    [JsonPropertyName("facebook_max_posts")]
    public int? FacebookMaxPostsSnake { get; set; }

    [JsonPropertyName("cookies")]
    public JsonElement? Cookies { get; set; }

    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    [JsonPropertyName("end_date")]
    public string? EndDate { get; set; }

    [JsonPropertyName("period")]
    public string? Period { get; set; }

    [JsonPropertyName("stop_urls")]
    public List<string>? StopUrls { get; set; }

    [JsonPropertyName("include_comments")]
    public bool? IncludeComments { get; set; }
}
