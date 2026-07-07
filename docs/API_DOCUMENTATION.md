# Social Crawler API Documentation — Chi tiết đầy đủ

---

## Mục lục

1. [Tổng quan hệ thống](#1-tổng-quan-hệ-thống)
2. [Endpoints hệ thống (System)](#2-endpoints-hệ-thống-system)
   - [2.1 GET /health](#21-get-health)
   - [2.2 GET /status](#22-get-status)
   - [2.3 POST /cancel](#23-post-cancel)
3. [Facebook Crawl Endpoints](#3-facebook-crawl-endpoints)
   - [3.1 POST /engine/crawl/facebook —— CrawlController (JSON mode)](#31-post-enginecrawlfacebook--crawlcontroller-json-mode)
   - [3.2 POST /crawl/facebook —— CrawlEngineController (SSE stream mode)](#32-post-crawlfacebook--crawlenginecontroller-sse-stream-mode)
   - [3.3 POST /engine/crawl/facebook (SSE stream mode)](#33-post-enginecrawlfacebook-sse-stream-mode)
4. [TikTok Crawl Endpoints](#4-tiktok-crawl-endpoints)
   - [4.1 POST /engine/crawl/tiktok —— CrawlController (JSON mode)](#41-post-enginecrawltiktok--crawlcontroller-json-mode)
   - [4.2 POST /engine/crawl —— CrawlController (JSON mode, compat)](#42-post-enginecrawl--crawlcontroller-json-mode-compat)
   - [4.3 POST /crawl/tiktok —— CrawlEngineController (SSE stream mode)](#43-post-crawltiktok--crawlenginecontroller-sse-stream-mode)
5. [Session Validation](#5-session-validation)
   - [5.1 POST /validate_session (TikTok)](#51-post-validatesession-tiktok)
   - [5.2 POST /validate_facebook_session (Facebook)](#52-post-validatefacebooksession-facebook)
6. [CrawlRequest —— Tham số chi tiết](#6-crawlrequest--tham-số-chi-tiết)
7. [CrawlEngineRequest —— Tham số chi tiết](#7-crawlenginerequest--tham-số-chi-tiết)
8. [Response Models](#8-response-models)
   - [8.1 CrawlApiResponse (JSON mode)](#81-crawlapiresponse-json-mode)
   - [8.2 FacebookRawItem](#82-facebookrawitem)
   - [8.3 TikTokRawItem & TikTokVideoData](#83-tiktokrawitem--tiktokvideodata)
   - [8.4 CrawlEvent](#84-crawlevent)
   - [8.5 TranscriptData & CaptionEntry](#85-transcriptdata--captionentry)
9. [Field Projection (Lọc trường)](#9-field-projection-lọc-trường)
10. [Crawl Flow Chi Tiết](#10-crawl-flow-chi-tiết)
    - [10.1 Facebook Crawl Flow](#101-facebook-crawl-flow)
    - [10.2 TikTok Crawl Flow](#102-tiktok-crawl-flow)
11. [Ví dụ Request/Response Đầy Đủ](#11-ví-dụ-requestresponse-đầy-đủ)
12. [Xử lý lỗi & Edge Cases](#12-xử-lý-lỗi--edge-cases)

---

## 1. Tổng quan hệ thống

Hệ thống gồm **2 controllers**:

| Controller | Route prefix | Mục đích |
|---|---|---|
| `CrawlController` | `/engine/crawl/...`, `/` | JSON response (default), SSE stream (khi `response_mode=stream`), session validation, health/status/cancel |
| `CrawlEngineController` | `/crawl/...` | SSE stream ALWAYS (PushStreamResult), designed cho Next.js consumption |

**Kiến trúc crawl lock**: Global SemaphoreSlim(1,1) — chỉ một crawl chạy tại một thời điểm. Các request khác xếp hàng chờ.

---

## 2. Endpoints hệ thống (System)

### 2.1 GET /health

Kiểm tra service sống.

**Request**:
```
GET /health
```

**Response** (HTTP 200):
```json
{
  "status": "ok",
  "service": "social-crawler"
}
```

### 2.2 GET /status

Trả về trạng thái hiện tại của crawler.

**Request**:
```
GET /status
```

**Response** (HTTP 200):
```json
{
  "state": "idle|running",
  "target": "https://www.facebook.com/...",
  "elapsed_seconds": 45.2,
  "locked": true
}
```

| Field | Type | Mô tả |
|---|---|---|
| `state` | string | `"idle"` — không có crawl nào chạy; `"running"` — đang crawl |
| `target` | string \| null | Target URL/username đang được crawl |
| `elapsed_seconds` | double | Số giây đã trôi qua từ khi crawl bắt đầu |
| `locked` | bool | `true` nếu global lock đang được giữ |

### 2.3 POST /cancel

Gửi tín hiệu hủy tiến trình crawl hiện tại.

**Request**:
```
POST /cancel
```

**Response** (HTTP 200):
```json
{
  "status": "ok",
  "message": "✅ Đã phát tín hiệu hủy tiến trình hiện tại. Lock sẽ được giải phóng trong giây lát."
}
```

Khi không có tiến trình nào chạy:
```json
{
  "status": "ok",
  "message": "Không có tiến trình nào đang chạy."
}
```

---

## 3. Facebook Crawl Endpoints

### 3.1 POST /engine/crawl/facebook —— CrawlController (JSON mode)

**Route**: `POST /engine/crawl/facebook`

Endpoint chính cho Facebook. **Mặc định trả về JSON response**. Có thể chuyển sang SSE stream bằng `response_mode: "stream"`.

#### Request Body (CrawlRequest)

```json
{
  "target": "https://www.facebook.com/happy.live.invest?sorting_setting=CHRONOLOGICAL",
  "cookies": [ ... ],
  "start_date": "2026-07-01",
  "end_date": "2026-07-07",
  "facebook_max_posts": 10,
  "include_transcripts": false,
  "stop_urls": ["https://www.facebook.com/page/posts/existing-post"],
  "response_mode": null,
  "fields": null,
  "scroll_initial_delay_min": 3000,
  "scroll_initial_delay_max": 5000,
  "max_scrolls": 15,
  "stale_limit": 4
}
```

Xem chi tiết từng tham số ở [mục 6 — CrawlRequest](#6-crawlrequest--tham-số-chi-tiết).

#### Response (HTTP 200)

```json
{
  "status": "ok",
  "platform": "facebook",
  "targets": ["https://www.facebook.com/happy.live.invest"],
  "count": 3,
  "items": [ ... FacebookRawItem ... ],
  "events": [ ... CrawlEvent ... ],
  "errors": [],
  "aborted": false,
  "started_at": "2026-07-07T10:26:31.0000000Z",
  "finished_at": "2026-07-07T10:27:56.0000000Z",
  "elapsed_ms": 85123
}
```

**Các trường hợp response:**

| Tình huống | `status` | `count` | `aborted` | Mô tả |
|---|---|---|---|---|
| Crawl thành công, có bài | `"ok"` | > 0 | `false` | Items chứa danh sách bài viết |
| Crawl thành công, không có bài | `"ok"` | 0 | `false` | Không tìm thấy bài nào (page không có post, hoặc cookies hết hạn) |
| Lỗi exception | `"error"` | 0 | `true` | Crawler crash, errors chứa thông tin lỗi |
| Bị cancel | `"ok"` hoặc `"error"` | 0+ | `true` | Người dùng gọi `/cancel` |

#### Behavior đặc biệt

- **Cookies expired / Login redirect**: Nếu Facebook redirect về `/login` hoặc `/checkpoint`, endpoint trả về:
  ```json
  {
    "type": "error",
    "message": "LOGIN_REQUIRED: Bị chuyển hướng sang trang đăng nhập/checkpoint. Facebook đã block cookies hoặc yêu cầu xác minh."
  }
  ```
  và `abortAll = true` — dừng tất cả targets còn lại.

- **stop_urls**: Khi gặp post có URL trong `stop_urls`, crawl dừng ngay lập tức (dùng cho incremental crawl — chỉ crawl bài mới hơn).

- **Giới hạn số bài**: `facebook_max_posts` (mặc định 50). Sau crawl, tất cả bài được sắp xếp theo `publishedAt` giảm dần (mới nhất trước), chỉ giữ lại `maxPosts` bài.

- **Xuất file**: Crawler tự động xóa output cũ, ghi raw JSON (`output/facebook/raw/`), parsed JSON (`output/facebook/parsed/`), transcripts (`output/facebook/transcripts/`) cho mỗi target.

### 3.2 POST /crawl/facebook —— CrawlEngineController (SSE stream mode)

**Route**: `POST /crawl/facebook`

Endpoint dành riêng cho Next.js, **luôn trả về SSE stream** (PushStreamResult), không hỗ trợ JSON mode.

#### Request Body (CrawlEngineRequest)

```json
{
  "target": "https://www.facebook.com/happy.live.invest",
  "cookies": [ ... ],
  "start_date": "2026-07-01",
  "end_date": "2026-07-07",
  "facebook_max_posts": 10,
  "facebookMaxPosts": 10,
  "stop_urls": ["https://..."]
}
```

Xem chi tiết từng tham số ở [mục 7 — CrawlEngineRequest](#7-crawlenginerequest--tham-số-chi-tiết).

#### Response (SSE stream — text/event-stream)

```
event: log
data: {"type":"progress","message":"Bắt đầu crawl Facebook: ...","page":1,"maxPages":5,"collected":0}

event: log
data: {"type":"log","message":"👉 Found Post: https://.../posts/pfbid... (Likes: 4, Comments: 1, Shares: 0) | Caption: ..."}

event: done
data: {"type":"done","page":null,"collected":null,"maxPages":null,"videos":[{...PostData...}],"count":3}
```

**Các event types:**

| event | type trong data | Mô tả |
|---|---|---|
| `log` | `"progress"` | Tiến độ crawl (page, collected, maxPages) |
| `log` | `"log"` | Log message thông thường |
| `error` | `"error"` | Lỗi xảy ra, kèm message |
| `done` | `"done"` | Crawl hoàn tất, chứa `videos[]` (PostData[]) và `count` |

**PostData trong event `done`**:
```json
{
  "platform": "facebook",
  "postUrl": "https://www.facebook.com/.../posts/...",
  "caption": "Nội dung bài viết...",
  "imageUrl": "https://scontent...",
  "publishedAt": "2026-07-07T02:01:03.0000000Z",
  "views": 0,
  "likes": 4,
  "comments": 1,
  "shares": 0,
  "authorName": "happy.live",
  "authorId": "100081848437684",
  "images": ["https://..."],
  "videos": [],
  "captionTracks": [],
  "transcript": null
}
```

#### Behavior đặc biệt

- **Lock queue**: Nếu có crawl khác đang chạy, event đầu tiên báo:
  ```
  event: log
  data: {"type":"log","message":"⚠️ Hàng đợi bận: Có tiến trình cào khác đang chạy. Đang chờ đến lượt..."}
  ```
  Sau đó chờ lock, rồi gửi:
  ```
  event: log
  data: {"type":"log","message":"✅ Hàng đợi trống (đã chờ 5.2s). Bắt đầu tiến trình cào mới."}
  ```

- **Không hỗ trợ** field projection, scroll tuning, transcripts.

### 3.3 POST /engine/crawl/facebook (SSE stream mode)

Khi gọi `POST /engine/crawl/facebook` với `response_mode: "stream"` hoặc `"sse"`:

```json
{
  "target": "...",
  "cookies": [],
  "response_mode": "stream"
}
```

Thì CrawlController chuyển sang chế độ SSE stream (PushStreamResult) tương tự CrawlEngineController. Xem [mục 3.2](#32-post-crawlfacebook--crawlenginecontroller-sse-stream-mode).

---

## 4. TikTok Crawl Endpoints

### 4.1 POST /engine/crawl/tiktok —— CrawlController (JSON mode)

**Route**: `POST /engine/crawl/tiktok`

#### Request Body (CrawlRequest)

```json
{
  "target": "@username",
  "cookies": [ ... tiktok cookies ... ],
  "start_date": "2026-06-27",
  "end_date": "2026-06-28",
  "period": "week",
  "include_comments": false,
  "response_mode": null,
  "fields": null
}
```

| Parameter | Type | Default | Mô tả |
|---|---|---|---|
| `target` | string | — | TikTok username, ví dụ `"@happyliveofficial"` |
| `targets` | string[] | — | Array các usernames |
| `cookies` | PlaywrightCookie[] | null | Cookie TikTok (.tiktok.com) |
| `start_date` | string | null | ISO date, VD: `"2026-06-27"` |
| `end_date` | string | null | ISO date, VD: `"2026-06-28"` |
| `period` | string | `"30 days"` | Chu kỳ lọc mặc định khi không có start/end |
| `include_comments` | bool | false | Crawl comments cho mỗi video |
| `response_mode` | string | null | `"stream"` hoặc `"sse"` để SSE |
| `fields` | string[] | null | Field projection |

#### Response (HTTP 200)

```json
{
  "status": "ok",
  "platform": "tiktok",
  "targets": ["@username"],
  "count": 5,
  "items": [ ... TikTokRawItem ... ],
  "events": [ ... CrawlEvent ... ],
  "errors": [],
  "aborted": false,
  "started_at": "2026-07-07T10:00:00.0000000Z",
  "finished_at": "2026-07-07T10:01:30.0000000Z",
  "elapsed_ms": 90000
}
```

### 4.2 POST /engine/crawl —— CrawlController (JSON mode, compat)

**Route**: `POST /engine/crawl`

Compatibility alias cho TikTok. Giống hệt `POST /engine/crawl/tiktok`.

### 4.3 POST /crawl/tiktok —— CrawlEngineController (SSE stream mode)

**Route**: `POST /crawl/tiktok`

Endpoint TikTok dành cho Next.js, **luôn SSE stream**.

#### Request Body (CrawlEngineRequest)

```json
{
  "target": "@username",
  "cookies": [ ... ],
  "start_date": "2026-06-27",
  "end_date": "2026-06-28",
  "period": "week",
  "include_comments": true
}
```

#### Response (SSE stream)

```
event: log
data: {"type":"progress","message":"Bắt đầu crawl TikTok: @username","page":1,"collected":0}

event: done
data: {"type":"done","page":null,"collected":null,"maxPages":null,"videos":[{...TikTokVideoData...}],"count":5}
```

**Khác biệt với CrawlController JSON mode:**
- CrawlEngineController **TIK xử lý comments inline**: events `item` bị bỏ qua (continue), comment được tích hợp vào `TikTokVideoData.commentsData`
- Field projection KHÔNG được hỗ trợ

---

## 5. Session Validation

### 5.1 POST /validate_session (TikTok)

**Route**: `POST /validate_session`

Kiểm tra cookies TikTok còn sống không.

**Request**:
```json
{
  "session_data": [ ... PlaywrightCookie array ... ]
}
```

**Response** (HTTP 200):
```json
{
  "ok": true,
  "message": "Session valid"
}
```

### 5.2 POST /validate_facebook_session (Facebook)

**Route**: `POST /validate_facebook_session`

Kiểm tra cookies Facebook còn sống không.

**Request**:
```json
{
  "session_data": [ ... PlaywrightCookie array ... ]
}
```

**Response** (HTTP 200):
```json
{
  "ok": true,
  "message": "Session valid"
}
```

---

## 6. CrawlRequest —— Tham số chi tiết

Dùng cho `CrawlController` (`/engine/crawl/facebook`, `/engine/crawl/tiktok`).

### 6.1 Target parameters

| Tham số | JSON key | Type | Bắt buộc | Default | Mô tả |
|---|---|---|---|---|---|
| Target URL | `target` | string | Có* | — | URL Facebook page hoặc TikTok username. VD: `"https://www.facebook.com/page"` hoặc `"@username"` |
| Nhiều targets | `targets` | string[] | Có* | — | Array các target, crawl lần lượt từng cái |
| Cookies | `cookies` | array | Không | null | Array cookie objects (xem format bên dưới) |

> *Phải cung cấp `target` hoặc `targets`. Nếu cả hai, `targets` được ưu tiên.

### 6.2 Date filter parameters

| Tham số | JSON key | Type | Default | Mô tả |
|---|---|---|---|---|
| Ngày bắt đầu | `start_date` | string | null | ISO date `"YYYY-MM-DD"`. Bài viết TRƯỚC ngày này sẽ bị bỏ qua. Khi gặp bài đầu tiên ngoài range, crawl dừng luôn (vì Facebook trả về bài theo thứ tự thời gian giảm dần). |
| Ngày kết thúc | `end_date` | string | null | ISO date `"YYYY-MM-DD"`. Bài viết SAU ngày này sẽ bị bỏ qua (thêm 1 ngày để inclusive cả ngày). |
| Period (TikTok) | `period` | string | `"30 days"` | Chu kỳ mặc định cho TikTok khi không có start/end. VD: `"week"`, `"month"`, `"7 days"`. |

**Cách hoạt động date filter:**

```csharp
// end_date "2026-06-30" → endDt = 2026-07-01T00:00:00Z (addDays 1)
// Bài viết có publishedAt = 2026-07-07T02:01:03Z
// → publishedDt > endDt → SKIP (bài này sau end_date)

// start_date "2026-06-29" → startDt = 2026-06-29T00:00:00Z
// Bài viết có publishedAt = 2026-06-28T10:00:00Z
// → publishedDt < startDt → STOP CRAWL (dừng hẳn, không scroll tiếp)
```

### 6.3 Facebook-specific parameters

| Tham số | JSON key | Type | Default | Mô tả |
|---|---|---|---|---|
| Max posts | `facebook_max_posts` | int | 50 | Số bài viết tối đa giữ lại. Sau crawl, tất cả bài sắp xếp theo thời gian giảm dần (mới nhất trước), chỉ giữ `facebook_max_posts` bài đầu. |
| Stop URLs | `stop_urls` | string[] | null | Array URL. Khi gặp bài có URL trong danh sách này, crawl dừng ngay. Dùng cho incremental crawl — chỉ lấy bài mới hơn URL này. |
| Include transcripts | `include_transcripts` | bool | false | Tải transcript/subtitle cho video Facebook. Mặc định KHÔNG tải vì tốn thời gian. Có thể kích hoạt gián tiếp qua field projection: `"fields": ["items.transcript"]`. |

### 6.4 Scroll tuning parameters

| Tham số | JSON key | Type | Default | Mô tả |
|---|---|---|---|---|
| Delay trước scroll đầu | `scroll_initial_delay_min` | int (ms) | 3000 | Chờ tối thiểu (ms) sau khi page load xong mới bắt đầu scroll |
| | `scroll_initial_delay_max` | int (ms) | 5000 | Chờ tối đa (ms) |
| Số bước scroll nhỏ | `scroll_steps_min` | int | 3 | Mỗi lần scroll, chia thành N bước nhỏ (giả lập hành vi người dùng) |
| | `scroll_steps_max` | int | 5 | |
| Delay giữa các bước nhỏ | `scroll_inter_step_delay_min` | int (ms) | 400 | |
| | `scroll_inter_step_delay_max` | int (ms) | 800 | |
| Delay sau mỗi scroll | `scroll_delay_min` | int (ms) | 5000 | Chờ sau mỗi lần scroll để GraphQL response kịp về |
| | `scroll_delay_max` | int (ms) | 9000 | |
| Số lần scroll tối đa | `max_scrolls` | int | 15 | Giới hạn trên số lần scroll |
| Stale limit | `stale_limit` | int | 4 | Số lần scroll liên tiếp KHÔNG có post mới thì dừng sớm |
| Dismiss popup delay | `popup_dismiss_delay` | int (ms) | 2000 | Chờ trước khi tự động đóng popup/login modal |

### 6.5 Human behavior simulation parameters

| Tham số | JSON key | Type | Default | Mô tả |
|---|---|---|---|---|
| Chance scroll ngẫu nhiên | `human_scroll_chance` | double | 0.7 | Xác suất (0-1) thực hiện PageDown ngẫu nhiên |
| Delay scroll ngẫu nhiên | `human_scroll_delay_min` | double (s) | 0.5 | |
| | `human_scroll_delay_max` | double (s) | 1.2 | |
| Mouse move steps | `human_mouse_move_steps_min` | int | 15 | Số bước di chuột ngẫu nhiên |
| | `human_mouse_move_steps_max` | int | 30 | |
| Chance scroll up | `human_scroll_up_chance` | double | 0.3 | Xác suất scroll lên (giả lập đọc lại) |
| Scroll up delay | `human_scroll_up_delay_min` | double (s) | 0.5 | |
| | `human_scroll_up_delay_max` | double (s) | 1.5 | |

### 6.6 Response control parameters

| Tham số | JSON key | Type | Default | Mô tả |
|---|---|---|---|---|
| Response mode | `response_mode` | string | null | `"stream"` hoặc `"sse"` → chuyển sang SSE stream. `null` → JSON mode |
| Field projection | `fields` | string[] | null | Array các field paths để lọc response |
| | `response_fields` | string[] | null | Alias cho `fields` |
| Include comments (TikTok) | `include_comments` | bool | false | Crawl comments TikTok |
| Include transcripts (FB) | `include_transcripts` | bool | false | Tải transcript Facebook |

### 6.7 Cookie format

Mỗi cookie object trong array:

```json
{
  "name": "c_user",
  "value": "61553188067359",
  "domain": ".facebook.com",
  "path": "/",
  "secure": true,
  "httpOnly": false,
  "expirationDate": 1814257223.137702,
  "sameSite": "Lax|Strict|None|no_restriction|lax"
}
```

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `name` | string | Có | Tên cookie |
| `value` | string | Có | Giá trị cookie |
| `domain` | string | Có | Domain, VD: `".facebook.com"`, `".tiktok.com"` |
| `path` | string | Không | Mặc định `"/"` |
| `secure` | bool | Không | Mặc định `false` |
| `httpOnly` | bool | Không | Mặc định `false` |
| `expirationDate` | number | Không | Unix timestamp (seconds). `-1` hoặc omit = session |
| `sameSite` | string | Không | `"Lax"`, `"Strict"`, `"None"`, `"no_restriction"`, `"lax"`. Mặc định `"None"` |

---

## 7. CrawlEngineRequest —— Tham số chi tiết

Dùng cho `CrawlEngineController` (`/crawl/facebook`, `/crawl/tiktok`).

| Tham số | JSON key | Type | Bắt buộc | Default | Mô tả |
|---|---|---|---|---|---|
| Target | `target` | string | Có* | — | URL hoặc username |
| Targets | `targets` | string[] | Có* | — | Array targets |
| Cookies | `cookies` | array | Không | null | Array cookie objects |
| Start date | `start_date` | string | Không | null | ISO date |
| End date | `end_date` | string | Không | null | ISO date |
| Period | `period` | string | Không | null | TikTok period |
| Max posts (FB) | `facebook_max_posts` | int | Không | 50 | **Snake_case** |
| | `facebookMaxPosts` | int | Không | 50 | **CamelCase** alias |
| Stop URLs | `stop_urls` | string[] | Không | null | FB stop URLs |
| Include comments | `include_comments` | bool | Không | false | TikTok comments |

---

## 8. Response Models

### 8.1 CrawlApiResponse (JSON mode)

```json
{
  "status": "ok|error",
  "platform": "facebook|tiktok",
  "targets": ["https://..."],
  "count": 5,
  "items": [ ... ],
  "events": [ ... ],
  "errors": [ ... ],
  "aborted": false,
  "started_at": "2026-07-07T10:26:31.0000000Z",
  "finished_at": "2026-07-07T10:27:56.0000000Z",
  "elapsed_ms": 85123
}
```

| Field | Type | Mô tả |
|---|---|---|
| `status` | string | `"ok"` — thành công (có thể có lỗi nhẹ); `"error"` — có lỗi nghiêm trọng |
| `platform` | string | `"facebook"` hoặc `"tiktok"` |
| `targets` | string[] | Danh sách target đã crawl |
| `count` | int | Số lượng item trong response |
| `items` | T[] | Array items (FacebookRawItem hoặc TikTokRawItem) |
| `events` | CrawlEvent[] | Lịch sử sự kiện crawl (nếu được request qua field projection) |
| `errors` | CrawlEvent[] | Các lỗi xảy ra (nếu được request) |
| `aborted` | bool | `true` nếu crawl bị hủy giữa chừng |
| `started_at` | string | ISO 8601 timestamp lúc bắt đầu |
| `finished_at` | string | ISO 8601 timestamp lúc kết thúc |
| `elapsed_ms` | long | Tổng thời gian chạy (ms) |

### 8.2 FacebookRawItem

```json
{
  "platform": "facebook",
  "post_id": "1027744569963815",
  "video_id": "pfbid02MrXvKRnTf1...",
  "post_url": "https://www.facebook.com/.../posts/pfbid02MrXvKRnTf1...",
  "caption": "Nội dung bài viết...",
  "published_at": "2026-07-07T02:01:03.0000000Z",
  "author": {
    "id": "100081848437684",
    "name": "happy.live",
    "url": "https://www.facebook.com/100081848437684"
  },
  "stats": {
    "views": 0,
    "likes": 4,
    "comments": 1,
    "shares": 0
  },
  "media": {
    "image_url": "https://scontent...",
    "images": ["https://..."],
    "videos": [],
    "caption_tracks": [
      {
        "url": "https://...",
        "locale": "vi",
        "language": "Vietnamese",
        "creation_method": "AUTOMATED",
        "is_auto_generated": true
      }
    ]
  },
  "transcript": {
    "has_transcript": true,
    "language": "vi",
    "is_auto_generated": true,
    "captions": [
      {
        "start_time": "0.000",
        "end_time": "2.500",
        "text": "Nội dung phụ đề..."
      }
    ]
  }
}
```

| Field | Type | Điều kiện | Mô tả |
|---|---|---|---|
| `platform` | string | Luôn có | `"facebook"` |
| `post_id` | string | Luôn có | ID nội bộ của Facebook cho story node |
| `video_id` | string? | Nếu là video/reel | ID video (extracted từ post URL) |
| `post_url` | string | Luôn có | URL đầy đủ của bài viết |
| `caption` | string | Luôn có | Nội dung text của bài viết |
| `published_at` | string? | Nếu có thời gian | ISO 8601 timestamp |
| `author` | object? | Nếu có thông tin | `{ id, name, url }` |
| `stats` | object | Luôn có | `{ views, likes, comments, shares }` |
| `media.image_url` | string? | Nếu có ảnh | URL ảnh đại diện |
| `media.images` | string[] | Luôn có | Danh sách URL ảnh |
| `media.videos` | string[] | Luôn có | Danh sách URL video |
| `media.caption_tracks` | array | Nếu có | Danh sách caption tracks (nhẹ, luôn available) |
| `transcript` | object? | Chỉ khi `include_transcripts=true` | Dữ liệu phụ đề đã tải về |

### 8.3 TikTokRawItem & TikTokVideoData

**TikTokRawItem** (JSON mode — `/engine/crawl/tiktok`):

```json
{
  "id": "738511924",
  "desc": "Nội dung video...",
  "createTime": 1783394676,
  "author": {
    "uniqueId": "happyliveofficial",
    "nickname": "Happy Live",
    "avatarLarger": "https://..."
  },
  "authorStats": {
    "followerCount": 100000,
    "followingCount": 500,
    "heartCount": 5000000,
    "videoCount": 200
  },
  "stats": {
    "playCount": 15000,
    "diggCount": 2000,
    "commentCount": 50,
    "shareCount": 100,
    "collectCount": 300
  },
  "video": {
    "duration": 60,
    "playAddr": "https://...",
    "cover": "https://..."
  },
  "music": {
    "title": "Nhạc nền",
    "authorName": "Tác giả"
  },
  "comments": [ ... TikTokComment ... ]
}
```

**TikTokVideoData** (SSE stream mode — `/crawl/tiktok` từ CrawlEngineController):

```json
{
  "id": "738511924",
  "url": "https://www.tiktok.com/@user/video/738511924",
  "desc": "Nội dung video...",
  "create_time": 1783394676,
  "create_time_formatted": "2026-07-06 12:00:00",
  "author": {
    "unique_id": "happyliveofficial",
    "nickname": "Happy Live",
    "avatar": "https://..."
  },
  "music": {
    "title": "Nhạc nền",
    "author": "Tác giả"
  },
  "stats": {
    "playCount": 15000,
    "diggCount": 2000,
    "commentCount": 50,
    "shareCount": 100,
    "collectCount": 300
  },
  "views": 15000,
  "likes": 2000,
  "comments": 50,
  "shares": 100,
  "comments_data": [ ... TikTokComment ... ]
}
```

**TikTokComment**:

```json
{
  "cid": "738511924_12345",
  "videoId": "738511924",
  "text": "Bình luận...",
  "createTime": 1783394676,
  "diggCount": 10,
  "replyTotal": 3,
  "user": {
    "uniqueId": "commenter",
    "nickname": "Người bình luận",
    "uid": "12345",
    "avatarThumb": "https://..."
  },
  "inlineReplies": [
    {
      "cid": "738511924_12345_reply1",
      "text": "Trả lời...",
      "user": {
        "uniqueId": "replier",
        "nickname": "Người trả lời"
      }
    }
  ]
}
```

### 8.4 CrawlEvent

```json
{
  "type": "log|progress|error|done|item",
  "message": "Nội dung log...",
  "target": "https://www.facebook.com/...",
  "page": 1,
  "max_pages": 5,
  "collected": 3,
  "count": 3,
  "videos": [ ... PostData[] ... ],
  "facebook_items": [ ... FacebookRawItem[] ... ],
  "aborted": false
}
```

| Field | Type | Event types | Mô tả |
|---|---|---|---|
| `type` | string | Tất cả | `"log"`: thông báo; `"progress"`: tiến độ; `"error"`: lỗi; `"done"`: kết thúc; `"item"`: TikTok item |
| `message` | string? | log, progress, error | Nội dung chi tiết |
| `target` | string? | progress | Target hiện tại |
| `page` | int? | progress | Số lần scroll hiện tại |
| `max_pages` | int? | progress | Tổng số lần scroll tối đa |
| `collected` | int? | progress | Số bài đã thu thập |
| `count` | int? | done | Tổng số bài trong response |
| `videos` | PostData[]? | done, log | Danh sách bài viết (dùng trong SSE stream) |
| `facebook_items` | FacebookRawItem[]? | done | Raw items cho Facebook (JSON mode) |
| `aborted` | bool? | done, error | `true` nếu bị hủy |

### 8.5 TranscriptData & CaptionEntry

```json
{
  "has_transcript": true,
  "language": "vi",
  "is_auto_generated": true,
  "captions": [
    {
      "start_time": "0.000",
      "end_time": "2.500",
      "text": "Nội dung phụ đề..."
    },
    {
      "start_time": "2.500",
      "end_time": "5.000",
      "text": "Phụ đề tiếp theo..."
    }
  ]
}
```

---

## 9. Field Projection (Lọc trường)

Field projection cho phép client chỉ request những trường cần thiết, giúp giảm kích thước response và tăng performance.

**Cách dùng:**
```json
{
  "fields": [
    "status",
    "count",
    "items.post_id",
    "items.post_url",
    "items.caption",
    "items.published_at",
    "items.stats.likes",
    "items.stats.comments",
    "items.stats.shares"
  ]
}
```

**Quy tắc:**

| Rule | Ví dụ | Kết quả |
|---|---|---|
| Omit fields → trả về tất cả | `"fields": null` | Response đầy đủ |
| `"*"` — tất cả | `"fields": ["*"]` | Response đầy đủ |
| Top-level field | `"fields": ["status"]` | Chỉ trả về `status` |
| Nested path | `"fields": ["items.post_url"]` | Chỉ trả về `post_url` trong mỗi item |
| Array — item-by-item | `"fields": ["items.stats"]` | Mỗi item chỉ chứa `stats` |
| Comma-separated | `"fields": ["status, count"]` | Cả `status` và `count` |
| Nhiều entries | `"fields": ["status", "count", "items.id"]` | Kết hợp |

**Top-level fields có thể chọn:**
- `status`
- `platform`
- `targets`
- `count`
- `items` — và các sub-field như `items.post_id`, `items.caption`,...
- `events`
- `errors`
- `aborted`
- `started_at`
- `finished_at`
- `elapsed_ms`

**Heavy opt-in behavior:**
- Nếu `items` không được request → controller KHÔNG accumulate items (tiết kiệm memory)
- Nếu `events` không được request → controller KHÔNG accumulate events
- Nếu `errors` không được request → controller KHÔNG accumulate errors
- **Transcripts**: Chỉ tải khi `include_transcripts=true` hoặc `fields` chứa `items.transcript`
- **Comments (TikTok)**: Chỉ crawl khi `include_comments=true` hoặc `fields` chứa `items.comments`
- `items.media.caption_tracks` là metadata nhẹ, không trigger tải transcript SRT

---

## 10. Crawl Flow Chi Tiết

### 10.1 Facebook Crawl Flow

```
1. [Validate] Kiểm tra target(s), parse cookies
2. [Lock] Chờ global crawl lock
3. [Launch] Tạo Playwright browser (CloakBrowser stealth Chromium)
   - Headless=true (mặc định) hoặc false (HEADLESS env)
   - Channel: msedge (Microsoft Edge)
   - Extra args chống phát hiện automation
4. [Context] Tạo browser context + inject cookies
5. [Navigate] Điều hướng đến target URL
   - Nếu redirect sang /login hoặc /checkpoint → báo lỗi LOGIN_REQUIRED, dừng
6. [Intercept] Gắn page.Response event handler
   - Lọc URL: regex /(api/graphql|graphql|api\.graphql)
   - Parse response body → FindStoryNodes (__typename=="Story", có post_id)
   - Enqueue stories vào ConcurrentQueue
7. [Initial] Chờ (initial_delay), dismiss popup/login modal
8. [DOM Fallback] ExtractVisibleDomPostsAsync
   - QuerySelector `div[role="article"]`
   - Trích xuất post URL, caption, author từ DOM
   - Thêm vào collectedPosts (merge sau với GraphQL data)
9. [Scroll Loop] for i = 1..maxScrolls:
   a. Human behavior simulation (mouse move, thinking idle)
   b. Scroll page (delta Y 800)
   c. Chờ delay
   d. Click "See more" / "Xem thêm" buttons
   e. Drain storyBuffer → parse stories
   f. Với mỗi story:
      - ExtractPostFromStoryNode → PostData
      - Check seenPostUrls (tránh trùng với DOM)
      - Check stop_urls → dừng nếu gặp
      - Check date filter → skip hoặc dừng
      - AddOrMergePost → collectedPosts
      - Yield CrawlEvent
   g. Kiểm tra staleStreak → dừng sớm nếu quá nhiều lần không có post mới
10. [Sort & Trim] Sắp xếp collectedPosts theo publishedAt giảm dần
    - Take(maxPosts) → topPosts
11. [Logging] ClearTargetOutputAsync + LogRawJsonAsync + LogParsedPostAsync
12. [Cleanup] Detach event handler, close browser
13. [Return] Yield CrawlEvent done (chứa FacebookRawItem[])
```

### 10.2 TikTok Crawl Flow

```
1. [Validate] Kiểm tra target(s), parse cookies
2. [Lock] Chờ global crawl lock
3. [Launch] Tạo Playwright browser
4. [Context] Tạo context + inject cookies TikTok
5. [Navigate] Điều hướng đến profile TikTok
6. [Scroll & Collect] Lặp scroll, thu thập video data từ DOM/API
7. [Comments] Nếu include_comments=true:
   - Với mỗi video, gọi API comments
   - Parse comments + replies
8. [Return] Yield CrawlEvent items (TikTokRawItem) + done
```

---

## 11. Ví dụ Request/Response Đầy Đủ

### 11.1 Facebook — Minimal request (không cookies, không date filter)

```bash
curl -X POST http://localhost:5001/engine/crawl/facebook \
  -H "Content-Type: application/json" \
  -d '{
    "target": "https://www.facebook.com/happy.live.invest"
  }'
```

Response: Sẽ báo lỗi LOGIN_REQUIRED vì không có cookies.

### 11.2 Facebook — Đầy đủ cookies + date filter + field projection

```bash
curl -X POST http://localhost:5001/engine/crawl/facebook \
  -H "Content-Type: application/json" \
  -d '{
    "target": "https://www.facebook.com/happy.live.invest?sorting_setting=CHRONOLOGICAL",
    "cookies": [
      {"name":"c_user","value":"61553188067359","domain":".facebook.com","path":"/","secure":true,"httpOnly":false,"expirationDate":1814257223,"sameSite":"lax"},
      {"name":"xs","value":"36%3AON89iJELbI0OpQ%3A2%3A1781496472%3A-1%3A-1%3A%3AAcxFR...","domain":".facebook.com","path":"/","secure":true,"httpOnly":true,"expirationDate":1814257223,"sameSite":"no_restriction"}
    ],
    "start_date": "2026-07-06",
    "end_date": "2026-07-07",
    "facebook_max_posts": 5,
    "fields": [
      "status",
      "count",
      "items.post_id",
      "items.post_url",
      "items.caption",
      "items.published_at",
      "items.author.name",
      "items.stats.likes",
      "items.stats.comments",
      "items.stats.shares"
    ]
  }'
```

Response:
```json
{
  "status": "ok",
  "count": 3,
  "items": [
    {
      "post_id": "1027744569963815",
      "post_url": "https://www.facebook.com/happy.live.invest/posts/pfbid02MrXvKRnTf1...",
      "caption": "[ƯU ĐÃI 07/07] VỮNG VÀNG PHÒNG THỦ - LÀM CHỦ SÂN CHƠI...",
      "published_at": "2026-07-07T02:01:03.0000000Z",
      "author": { "name": "happy.live" },
      "stats": { "likes": 4, "comments": 1, "shares": 0 }
    }
  ]
}
```

### 11.3 Facebook — SSE stream mode

```bash
curl -N -X POST http://localhost:5001/engine/crawl/facebook \
  -H "Content-Type: application/json" \
  -d '{
    "target": "https://www.facebook.com/happy.live.invest",
    "cookies": [...],
    "response_mode": "stream"
  }'
```

Response (streaming):
```
event: log
data: {"type":"progress","message":"Bắt đầu crawl Facebook (GraphQL interception): https://www.facebook.com/happy.live.invest","target":"...","page":1,"maxPages":5,"collected":0}

event: log
data: {"type":"log","message":"👉 Found Post: https://.../posts/pfbid... (Likes: 4, Comments: 1, Shares: 0) | Caption: [ƯU ĐÃI 07/07]..."}

event: log
data: {"type":"progress","message":"Đang crawl (lần cuộn 1)... Thu được 3 bài.","target":"...","page":1,"maxPages":5,"collected":3}

event: done
data: {"type":"done","target":"...","count":3,"videos":[{...PostData...}]}
```

### 11.4 Facebook — CrawlEngineController (Next.js)

```bash
curl -N -X POST http://localhost:5001/crawl/facebook \
  -H "Content-Type: application/json" \
  -d '{
    "target": "https://www.facebook.com/happy.live.invest",
    "cookies": [...]
  }'
```

Response giống SSE stream ở 11.3 nhưng dùng event name `log`/`error`/`done` và field names theo camelCase.

### 11.5 TikTok — Minimal request

```bash
curl -X POST http://localhost:5001/engine/crawl/tiktok \
  -H "Content-Type: application/json" \
  -d '{
    "target": "@happyliveofficial",
    "cookies": [...],
    "start_date": "2026-06-27",
    "end_date": "2026-06-28",
    "period": "week"
  }'
```

### 11.6 TikTok — Với comments

```bash
curl -X POST http://localhost:5001/engine/crawl/tiktok \
  -H "Content-Type: application/json" \
  -d '{
    "target": "@happyliveofficial",
    "cookies": [...],
    "period": "week",
    "include_comments": true,
    "fields": [
      "status",
      "count",
      "items.id",
      "items.desc",
      "items.comments.cid",
      "items.comments.text",
      "items.comments.user.uniqueId",
      "items.comments.user.nickname"
    ]
  }'
```

### 11.7 Validate Facebook session

```bash
curl -X POST http://localhost:5001/validate_facebook_session \
  -H "Content-Type: application/json" \
  -d '{
    "session_data": [
      {"name":"c_user","value":"61553188067359","domain":".facebook.com","path":"/"}
    ]
  }'
```

### 11.8 Crawl với stop_urls (incremental crawl)

```bash
curl -X POST http://localhost:5001/engine/crawl/facebook \
  -H "Content-Type: application/json" \
  -d '{
    "target": "https://www.facebook.com/happy.live.invest",
    "cookies": [...],
    "facebook_max_posts": 200,
    "stop_urls": [
      "https://www.facebook.com/happy.live.invest/posts/pfbidExistingPost"
    ]
  }'
```

Khi gặp bài có URL trong `stop_urls`, crawl dừng ngay lập tức (không scroll tiếp). Dùng để chỉ lấy bài mới hơn bài đã có trong DB.

### 11.9 Crawl với scroll tuning

```bash
curl -X POST http://localhost:5001/engine/crawl/facebook \
  -H "Content-Type: application/json" \
  -d '{
    "target": "https://www.facebook.com/happy.live.invest",
    "cookies": [...],
    "facebook_max_posts": 150,
    "max_scrolls": 50,
    "stale_limit": 6,
    "scroll_delay_min": 3000,
    "scroll_delay_max": 8000,
    "popup_dismiss_delay": 2000
  }'
```

### 11.10 Crawl với transcripts

```bash
curl -X POST http://localhost:5001/engine/crawl/facebook \
  -H "Content-Type: application/json" \
  -d '{
    "target": "https://www.facebook.com/misslanenglish",
    "cookies": [...],
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
  }'
```

### 11.11 Status & Cancel flow

```bash
# Kiểm tra trạng thái
curl http://localhost:5001/status

# Hủy crawl đang chạy
curl -X POST http://localhost:5001/cancel

# Kiểm tra health
curl http://localhost:5001/health
```

---

## 12. Xử lý lỗi & Edge Cases

### 12.1 Lỗi thường gặp

| Tình huống | HTTP Status | Response | Nguyên nhân | Cách xử lý |
|---|---|---|---|---|
| Thiếu target | 400 | `{"detail": "Missing target or targets parameter"}` | Không có `target` hoặc `targets` trong body | Thêm `target` hoặc `targets` |
| Cookies hết hạn | 200 | `events` chứa error `"LOGIN_REQUIRED"` | Facebook redirect về login | Refresh cookies, re-validate |
| Facebook checkpoint | 200 | `events` chứa error `"LOGIN_REQUIRED"` | Facebook yêu cầu xác minh | Đăng nhập manual, export cookies mới |
| Exception trong crawl | 200 | `status: "error"`, `aborted: true` | Lỗi không xác định | Kiểm tra server log |
| Cancel giữa chừng | 200 | `aborted: true`, `count` có thể > 0 | Người dùng gọi `/cancel` | Xử lý partial result |
| Lock timeout | 200 | Chờ đến lượt | Crawl khác đang chạy | Đợi hoặc cancel crawl hiện tại |

### 12.2 Edge Cases

| Case | Behavior |
|---|---|
| **Target là URL không hợp lệ** | Playwright navigate lỗi → crawl event error |
| **Page không có bài viết nào** | `count: 0`, `items: []`, không có lỗi |
| **GraphQL response rỗng** | Crawler log `[NET] GraphQL matched but 0 stories`, scroll tiếp |
| **Nhiều GraphQL responses** | Mỗi response được xử lý riêng, stories enqueue vào ConcurrentQueue |
| **Stale streak ngắt giữa chừng** | Crawler dừng sớm nhưng vẫn trả về những bài đã collected |
| **facebook_max_posts = 0** | Crawl vẫn chạy nhưng không giữ lại bài nào (count = 0) |
| **start_date > end_date** | Filter sai → không có bài nào thỏa mãn |
| **Cookies null** | Crawl vẫn chạy nhưng có thể bị redirect login |
| **Crawl bị cancel khi đang ghi file** | File ghi dở có thể bị corrupt, lần chạy sau ClearTargetOutput sẽ clean |
| **Đã có output từ lần chạy trước** | ClearTargetOutputAsync xóa toàn bộ output cũ trước khi ghi mới |

### 12.3 Production recommendations

1. **Luôn validate session trước khi crawl:**
   ```json
   POST /validate_facebook_session
   { "session_data": [ ... cookies ... ] }
   ```

2. **Dùng field projection để giảm tải:**
   ```json
   { "fields": ["status", "count", "items.post_url", "items.stats.likes"] }
   ```

3. **Không request transcript trừ khi cần** — transcript tải file SRT từ CDN Facebook, tốn thời gian.

4. **Không request events trong production** — controller không accumulate events nếu không được request.

5. **Dùng stop_urls cho incremental crawl** — tránh crawl lại bài cũ.

6. **Tune scroll parameters** cho page có nhiều bài:
   - Tăng `max_scrolls` (mặc định 15)
   - Tăng `stale_limit` (mặc định 4)
   - Giảm `scroll_delay_min`/`scroll_delay_max` nếu muốn nhanh hơn

7. **Xử lý partial result khi `aborted: true`** — vẫn có thể có items hợp lệ.

---

## Index: Endpoint Quick Reference

| Method | Route | Controller | Response Type | Mục đích |
|---|---|---|---|---|
| GET | `/health` | CrawlController | JSON | Health check |
| GET | `/status` | CrawlController | JSON | Trạng thái crawler |
| POST | `/cancel` | CrawlController | JSON | Hủy crawl hiện tại |
| POST | `/engine/crawl/facebook` | CrawlController | JSON / SSE | Facebook crawl (chính) |
| POST | `/crawl/facebook` | CrawlEngineController | SSE stream | Facebook crawl (Next.js) |
| POST | `/engine/crawl/tiktok` | CrawlController | JSON / SSE | TikTok crawl (chính) |
| POST | `/engine/crawl` | CrawlController | JSON / SSE | TikTok crawl (compat) |
| POST | `/crawl/tiktok` | CrawlEngineController | SSE stream | TikTok crawl (Next.js) |
| POST | `/validate_session` | CrawlController | JSON | Validate TikTok session |
| POST | `/validate_facebook_session` | CrawlController | JSON | Validate Facebook session |

---

*Document version: 1.0*  
*Last updated: 2026-07-07*
