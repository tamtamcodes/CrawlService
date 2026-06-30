# Social Crawler Enterprise API Integration Guide

## Production documentation endpoints

- Scalar API Reference: `GET /scalar`
- OpenAPI JSON for Scalar/SDK/Agent/MCP tooling: `GET /openapi/v1.json`
- Swagger JSON compatibility: `GET /swagger/v1/swagger.json`
- Swagger UI compatibility: `GET /swagger`
- Health: `GET /health`
- Status: `GET /status`
- Cancel current crawl: `POST /cancel`

Scalar is enabled in every environment, including production. The Scalar MCP integration metadata is configured with `SCALAR_MCP_SERVER_URL` or `Scalar:McpServerUrl`; if not set it falls back to `/mcp`.

## Common crawl response modes

### JSON mode

JSON mode is the default for `/crawl/facebook`, `/crawl/tiktok`, and `/crawl`.

```json
{
  "status": "ok",
  "platform": "facebook|tiktok",
  "targets": ["..."],
  "count": 0,
  "items": [],
  "events": [],
  "errors": [],
  "aborted": false,
  "started_at": "2026-06-30T00:00:00.0000000Z",
  "finished_at": "2026-06-30T00:00:10.0000000Z",
  "elapsed_ms": 10000
}
```

### SSE stream mode

Set `response_mode` to `stream` or `sse` to receive legacy `text/event-stream` events.

```json
{
  "response_mode": "stream"
}
```

## Field projection

Use `fields` or `response_fields` to return only selected paths.

```json
{
  "fields": [
    "status",
    "count",
    "items.post_id",
    "items.post_url",
    "items.stats.likes"
  ]
}
```

Rules:

- Omit `fields` to return the full response.
- Use comma-separated or array entries.
- Use nested dot paths.
- Arrays are projected item-by-item.
- `*` returns everything.
- If `items`, `events`, or `errors` are not requested, the controller avoids accumulating those heavy sections.

## Heavy crawl options

### Facebook transcripts

Default: not downloaded.

Enable explicitly:

```json
{
  "include_transcripts": true
}
```

Or implicitly by requesting transcript fields:

```json
{
  "fields": ["items.transcript"]
}
```

`items.media.caption_tracks` is lightweight metadata and does not download SRT transcript files by itself.

### TikTok comments

Default: not crawled.

Enable explicitly:

```json
{
  "include_comments": true
}
```

Or implicitly by requesting comment fields:

```json
{
  "fields": ["items.comments"]
}
```

## Facebook endpoint

`POST /crawl/facebook`

### Full JSON response

```json
{
  "target": "https://www.facebook.com/misslanenglish?sorting_setting=CHRONOLOGICAL",
  "cookies": [],
  "start_date": "2026-06-29",
  "end_date": "2026-06-30",
  "facebook_max_posts": 10
}
```

### Light response without transcripts

```json
{
  "target": "https://www.facebook.com/misslanenglish?sorting_setting=CHRONOLOGICAL",
  "cookies": [],
  "start_date": "2026-06-29",
  "end_date": "2026-06-30",
  "facebook_max_posts": 2,
  "fields": [
    "status",
    "count",
    "items.post_id",
    "items.video_id",
    "items.post_url",
    "items.caption",
    "items.published_at",
    "items.stats.likes",
    "items.stats.comments",
    "items.stats.shares",
    "items.media.caption_tracks"
  ]
}
```

### Transcript response

```json
{
  "target": "https://www.facebook.com/misslanenglish?sorting_setting=CHRONOLOGICAL",
  "cookies": [],
  "start_date": "2026-06-29",
  "end_date": "2026-06-30",
  "facebook_max_posts": 10,
  "include_transcripts": true,
  "fields": [
    "status",
    "count",
    "items.post_id",
    "items.post_url",
    "items.transcript.has_transcript",
    "items.transcript.language",
    "items.transcript.captions.start_time",
    "items.transcript.captions.end_time",
    "items.transcript.captions.text"
  ]
}
```

### Stop URLs

```json
{
  "target": "https://www.facebook.com/page",
  "cookies": [],
  "start_date": "2026-01-01",
  "end_date": "2026-06-30",
  "facebook_max_posts": 200,
  "stop_urls": [
    "https://www.facebook.com/page/posts/existing-post"
  ]
}
```

### Scroll tuning

```json
{
  "target": "https://www.facebook.com/page",
  "cookies": [],
  "start_date": "2026-01-01",
  "end_date": "2026-06-30",
  "facebook_max_posts": 150,
  "max_scrolls": 50,
  "stale_limit": 6,
  "scroll_delay_min": 3000,
  "scroll_delay_max": 8000,
  "scroll_steps_min": 2,
  "scroll_steps_max": 5,
  "popup_dismiss_delay": 2000
}
```

## TikTok endpoint

`POST /crawl/tiktok` and compatibility alias `POST /crawl`

### Full JSON response without comments

```json
{
  "target": "@username",
  "cookies": [],
  "start_date": "2026-06-27",
  "end_date": "2026-06-28",
  "period": "week"
}
```

### Light response without comments

```json
{
  "target": "@username",
  "cookies": [],
  "start_date": "2026-06-27",
  "end_date": "2026-06-28",
  "period": "week",
  "fields": [
    "status",
    "count",
    "items.id",
    "items.desc",
    "items.createTime",
    "items.author.uniqueId",
    "items.author.nickname",
    "items.stats.playCount",
    "items.stats.diggCount",
    "items.stats.commentCount",
    "items.stats.shareCount"
  ]
}
```

### Comments response

```json
{
  "target": "@username",
  "cookies": [],
  "start_date": "2026-06-27",
  "end_date": "2026-06-28",
  "period": "week",
  "include_comments": true,
  "fields": [
    "status",
    "count",
    "items.id",
    "items.desc",
    "items.comments.cid",
    "items.comments.text",
    "items.comments.createTime",
    "items.comments.diggCount",
    "items.comments.user.uniqueId",
    "items.comments.user.nickname"
  ]
}
```

## Session validation

### TikTok

`POST /validate_session`

```json
{
  "session_data": []
}
```

### Facebook

`POST /validate_facebook_session`

```json
{
  "session_data": []
}
```

## Operational behavior

- Only one crawl runs at a time because the controller uses a global crawl lock.
- Concurrent requests wait and are processed in order.
- `POST /cancel` signals cancellation for the running crawl.
- `GET /status` reports current state, target, elapsed seconds, and lock state.
- Facebook output folders are cleaned per target before writing the current trimmed result set.
- TikTok raw items are written to `output/tiktok/raw`.
- Facebook raw, parsed, and transcript files are written to `output/facebook`.

## Common production issues

### Login required or checkpoint

Cause: expired/invalid cookies or platform security checkpoint.

Action:

- Refresh browser cookies.
- Re-run validation endpoint.
- Avoid excessive concurrency and repeated failed crawls.

### Captcha detected

Cause: platform anti-bot protection.

Action:

- Slow down crawl frequency.
- Use valid session cookies.
- Reduce request volume.
- Run with browser visible when manual verification is required.

### Slow responses

Use field projection and opt-in heavy options only when needed.

Recommended defaults:

- Do not request `items.transcript` unless transcript text is required.
- Do not request `items.comments` unless comment data is required.
- Do not request `events` in production integrations unless debugging.

### Large JSON payloads

Use selective fields:

```json
{
  "fields": ["status", "count", "items.id", "items.post_url"]
}
```

### SSE compatibility

Use `response_mode: "stream"` only for clients that consume server-sent events. New integrations should prefer JSON mode.

## Recommended enterprise integration flow

1. Validate session cookies.
2. Call crawl endpoint with light fields first.
3. Enable comments/transcripts only for records that require enrichment.
4. Poll `/status` for operational dashboards.
5. Use `/cancel` for SLA timeout handling.
6. Consume `/openapi/v1.json` for SDK generation and Scalar MCP/agent tooling.
7. Use `/scalar` as the canonical interactive production documentation.
