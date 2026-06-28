using System.Text.Json.Serialization;

namespace SocialCrawler.Models;

/// <summary>
/// User info embedded in comments and replies.
/// </summary>
public class TikTokCommentUser
{
    [JsonPropertyName("uniqueId")]
    public string? UniqueId { get; set; }

    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    [JsonPropertyName("uid")]
    public string? Uid { get; set; }

    [JsonPropertyName("avatarThumb")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AvatarThumb { get; set; }
}

/// <summary>
/// Inline reply object embedded inside a comment (first few replies).
/// </summary>
public class TikTokInlineReply
{
    [JsonPropertyName("cid")]
    public string? Cid { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("user")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TikTokCommentUser? User { get; set; }
}

/// <summary>
/// A comment fetched from the TikTok comment list API.
/// </summary>
public class TikTokComment
{
    [JsonPropertyName("cid")]
    public string? Cid { get; set; }

    [JsonPropertyName("videoId")]
    public string? VideoId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("createTime")]
    public long CreateTime { get; set; }

    [JsonPropertyName("diggCount")]
    public int DiggCount { get; set; }

    [JsonPropertyName("replyTotal")]
    public int ReplyTotal { get; set; }

    [JsonPropertyName("user")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TikTokCommentUser? User { get; set; }

    [JsonPropertyName("inlineReplies")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TikTokInlineReply>? InlineReplies { get; set; }
}

/// <summary>
/// A reply to a comment, fetched via the reply API.
/// Saved as _reply.json alongside comments.
/// </summary>
public class TikTokReply
{
    [JsonPropertyName("cid")]
    public string? Cid { get; set; }

    [JsonPropertyName("videoId")]
    public string? VideoId { get; set; }

    [JsonPropertyName("parentCommentId")]
    public string? ParentCommentId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("createTime")]
    public long CreateTime { get; set; }

    [JsonPropertyName("diggCount")]
    public int DiggCount { get; set; }

    [JsonPropertyName("replyId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReplyId { get; set; }

    [JsonPropertyName("replyToReplyId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReplyToReplyId { get; set; }

    [JsonPropertyName("user")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TikTokCommentUser? User { get; set; }
}
