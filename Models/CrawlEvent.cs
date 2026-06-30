using System.Text.Json.Serialization;

namespace SocialCrawler.Models;

public class CrawlEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "log";

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; set; }

    [JsonPropertyName("page")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Page { get; set; }

    [JsonPropertyName("max_pages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxPages { get; set; }

    [JsonPropertyName("collected")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Collected { get; set; }

    [JsonPropertyName("count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Count { get; set; }

    [JsonPropertyName("videos")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostData>? Videos { get; set; }

    [JsonPropertyName("facebook_items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FacebookRawItem>? FacebookItems { get; set; }

    [JsonPropertyName("aborted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Aborted { get; set; }
    public List<TikTokRawItem>? RawItems { get; set; }
}

public class PostData
{
    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "";

    [JsonPropertyName("postUrl")]
    public string PostUrl { get; set; } = "";

    [JsonPropertyName("caption")]
    public string Caption { get; set; } = "";

    [JsonPropertyName("imageUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("publishedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublishedAt { get; set; }

    [JsonPropertyName("views")]
    public int Views { get; set; }

    [JsonPropertyName("likes")]
    public int Likes { get; set; }

    [JsonPropertyName("comments")]
    public int Comments { get; set; }

    [JsonPropertyName("shares")]
    public int Shares { get; set; }

    [JsonPropertyName("authorName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthorName { get; set; }

    [JsonPropertyName("authorId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthorId { get; set; }

    [JsonPropertyName("images")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Images { get; set; }

    [JsonPropertyName("videos")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Videos { get; set; }

    [JsonPropertyName("captionTracks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FacebookCaptionTrack>? CaptionTracks { get; set; }

    [JsonPropertyName("transcript")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TranscriptData? Transcript { get; set; }
}

public class TikTokVideoData
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("desc")]
    public string? Desc { get; set; }

    [JsonPropertyName("create_time")]
    public long CreateTime { get; set; }

    [JsonPropertyName("create_time_formatted")]
    public string CreateTimeFormatted { get; set; } = "";

    [JsonPropertyName("author")]
    public TikTokAuthor? Author { get; set; }

    [JsonPropertyName("music")]
    public TikTokMusic? Music { get; set; }

    [JsonPropertyName("stats")]
    public Dictionary<string, object>? Stats { get; set; }

    [JsonPropertyName("views")]
    public int Views { get; set; }

    [JsonPropertyName("likes")]
    public int Likes { get; set; }

    [JsonPropertyName("comments")]
    public int Comments { get; set; }

    [JsonPropertyName("shares")]
    public int Shares { get; set; }

    [JsonPropertyName("comments_data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TikTokComment>? CommentsData { get; set; }
}

public class TikTokAuthor
{
    [JsonPropertyName("unique_id")]
    public string? UniqueId { get; set; }

    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }
}

public class TikTokMusic
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }
}

public class TranscriptData
{
    [JsonPropertyName("has_transcript")]
    public bool HasTranscript { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("is_auto_generated")]
    public bool IsAutoGenerated { get; set; }

    [JsonPropertyName("captions")]
    public List<CaptionEntry> Captions { get; set; } = new();
}

public class CaptionEntry
{
    [JsonPropertyName("start_time")]
    public string StartTime { get; set; } = "";

    [JsonPropertyName("end_time")]
    public string EndTime { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

public class PlaywrightCookie
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "/";

    [JsonPropertyName("expires")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Expires { get; set; }

    [JsonPropertyName("secure")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Secure { get; set; }

    [JsonPropertyName("httpOnly")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HttpOnly { get; set; }

    [JsonPropertyName("sameSite")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SameSite { get; set; }
}
