using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SocialCrawler.Models;
using SocialCrawler.Services;

namespace SocialCrawler.Controllers;

/// <summary>
/// Next.js compatible SSE-stream-only endpoints for Facebook and TikTok crawling.
/// All responses are <c>text/event-stream</c> (PushStreamResult).
/// Does NOT support JSON mode or field projection.
/// Designed for frontend consumption where real-time progress is needed.
/// </summary>
[ApiController]
[Route("crawl")]
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

    /// <summary>
    /// Crawl Facebook page(s) and stream results as SSE events.
    /// Always returns <c>text/event-stream</c>. Progress, log, and found posts are
    /// streamed in real-time. Only one crawl runs at a time (global lock).
    /// </summary>
    /// <param name="req">
    /// Crawl request. See <see cref="CrawlEngineRequest"/>.
    /// Required: <c>target</c> or <c>targets</c>.
    /// Supports: <c>cookies</c>, <c>start_date</c>, <c>end_date</c>,
    /// <c>facebook_max_posts</c> / <c>facebookMaxPosts</c>, <c>stop_urls</c>.
    /// </param>
    /// <returns>
    /// SSE stream (text/event-stream) with events:
    /// <c>event: log</c> — progress updates and found posts,
    /// <c>event: error</c> — errors,
    /// <c>event: done</c> — final results with <c>videos[]</c> (PostData[]) and <c>count</c>.
    /// </returns>
    /// <example>
    /// POST /crawl/facebook
    /// {
    ///   "target": "https://www.facebook.com/page",
    ///   "cookies": [...],
    ///   "facebook_max_posts": 10
    /// }
    /// </example>
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

    /// <summary>
    /// Crawl TikTok profile(s) and stream results as SSE events.
    /// Always returns <c>text/event-stream</c>. Videos are accumulated server-side and
    /// returned in the final <c>done</c> event. Comments are embedded into
    /// <see cref="TikTokVideoData.CommentsData"/> when <c>include_comments=true</c>.
    /// Only one crawl runs at a time (global lock).
    /// </summary>
    /// <param name="req">
    /// Crawl request. See <see cref="CrawlEngineRequest"/>.
    /// Required: <c>target</c> (e.g. "@username") or <c>targets</c>.
    /// Supports: <c>cookies</c>, <c>start_date</c>, <c>end_date</c>, <c>period</c>, <c>include_comments</c>.
    /// </param>
    /// <returns>
    /// SSE stream (text/event-stream) with events:
    /// <c>event: log</c> — progress updates,
    /// <c>event: error</c> — errors,
    /// <c>event: done</c> — final results with <c>videos[]</c> (TikTokVideoData[]) and <c>count</c>.
    /// </returns>
    /// <example>
    /// POST /crawl/tiktok
    /// {
    ///   "target": "@username",
    ///   "cookies": [...],
    ///   "period": "week",
    ///   "include_comments": true
    /// }
    /// </example>
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

/// <summary>
/// Request model for <see cref="CrawlEngineController"/> endpoints.
/// Supports both snake_case and camelCase for JSON property names.
/// </summary>
public class CrawlEngineRequest
{
    /// <summary>
    /// Array of target URLs/usernames to crawl.
    /// Takes precedence over <see cref="Target"/> when both are provided.
    /// </summary>
    /// <example>["https://www.facebook.com/page1", "https://www.facebook.com/page2"]</example>
    [JsonPropertyName("targets")]
    public List<string>? Targets { get; set; }

    /// <summary>
    /// Single target URL or username to crawl.
    /// Ignored when <see cref="Targets"/> is provided.
    /// </summary>
    /// <example>"https://www.facebook.com/page"</example>
    [JsonPropertyName("target")]
    public string? Target { get; set; }

    /// <summary>
    /// Max Facebook posts to keep (camelCase).
    /// Only the N most recent posts are returned after sorting.
    /// </summary>
    /// <example>10</example>
    [JsonPropertyName("facebookMaxPosts")]
    public int? FacebookMaxPosts { get; set; }

    /// <summary>
    /// Max Facebook posts to keep (snake_case alias).
    /// Used as fallback when <see cref="FacebookMaxPosts"/> is null.
    /// </summary>
    /// <example>10</example>
    [JsonPropertyName("facebook_max_posts")]
    public int? FacebookMaxPostsSnake { get; set; }

    /// <summary>
    /// Browser cookies array. Each cookie must include <c>name</c>, <c>value</c>, <c>domain</c>.
    /// See <see cref="PlaywrightCookie"/> for full format.
    /// </summary>
    /// <example>[{ "name": "c_user", "value": "123", "domain": ".facebook.com", "path": "/" }]</example>
    [JsonPropertyName("cookies")]
    public JsonElement? Cookies { get; set; }

    /// <summary>
    /// Start date filter (inclusive). ISO 8601 date string.
    /// Posts published before this date are skipped.
    /// When the crawler encounters a post older than start_date, it stops entirely.
    /// </summary>
    /// <example>"2026-07-01"</example>
    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    /// <summary>
    /// End date filter (inclusive). ISO 8601 date string.
    /// Posts published after this date are skipped.
    /// Internally adds 1 day to make the date fully inclusive.
    /// </summary>
    /// <example>"2026-07-07"</example>
    [JsonPropertyName("end_date")]
    public string? EndDate { get; set; }

    /// <summary>
    /// Period string for TikTok date filtering (used when start/end dates are not specified).
    /// </summary>
    /// <example>"30 days", "week", "month"</example>
    [JsonPropertyName("period")]
    public string? Period { get; set; }

    /// <summary>
    /// Stop URLs for Facebook incremental crawl.
    /// When a post matching one of these URLs is found, the crawler stops immediately.
    /// Use this to only fetch posts newer than what you already have.
    /// </summary>
    /// <example>["https://www.facebook.com/page/posts/existing-post"]</example>
    [JsonPropertyName("stop_urls")]
    public List<string>? StopUrls { get; set; }

    /// <summary>
    /// Whether to crawl comments for each TikTok video.
    /// Default: false. Comments are embedded in <see cref="TikTokVideoData.CommentsData"/>.
    /// </summary>
    /// <example>true</example>
    [JsonPropertyName("include_comments")]
    public bool? IncludeComments { get; set; }
}
