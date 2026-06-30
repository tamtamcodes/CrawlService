using System.Text.Json;
using SocialCrawler.Models;

namespace SocialCrawler.Services;

public static class TikTokVideoParser
{
    public static TikTokRawItem ParseVideoItem(JsonElement item)
    {
        var rawJson = item.GetRawText();
        var raw = JsonSerializer.Deserialize<TikTokRawItem>(item.GetRawText()) ?? new TikTokRawItem();
        raw.RawJson = rawJson;
        return raw;
    }

    public static TikTokVideoData ParseToVideoData(JsonElement item)
    {
        var createTime = 0L;
        if (item.TryGetProperty("createTime", out var ct))
        {
            if (!ct.TryGetInt64(out createTime))
                long.TryParse(ct.GetString(), out createTime);
        }

        var videoId = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;

        string? uniqueId = null;
        if (item.TryGetProperty("author", out var authorElObj) && authorElObj.ValueKind == JsonValueKind.Object)
        {
            if (authorElObj.TryGetProperty("uniqueId", out var uid)) uniqueId = uid.GetString();
        }

        var videoUrl = (!string.IsNullOrEmpty(uniqueId) && !string.IsNullOrEmpty(videoId))
            ? $"https://www.tiktok.com/@{uniqueId}/video/{videoId}"
            : "";

        string? nickname = null, avatar = null;
        if (item.TryGetProperty("author", out var authorEl) && authorEl.ValueKind == JsonValueKind.Object)
        {
            if (authorEl.TryGetProperty("nickname", out var nn)) nickname = nn.GetString();
            if (authorEl.TryGetProperty("avatarLarger", out var av)) avatar = av.GetString();
            if (string.IsNullOrEmpty(avatar) && authorEl.TryGetProperty("avatarMedium", out var avm)) avatar = avm.GetString();
            if (string.IsNullOrEmpty(avatar) && authorEl.TryGetProperty("avatarThumb", out var avt)) avatar = avt.GetString();
        }

        string? musicTitle = null, musicAuthor = null;
        if (item.TryGetProperty("music", out var music) && music.ValueKind == JsonValueKind.Object)
        {
            if (music.TryGetProperty("title", out var mt)) musicTitle = mt.GetString();
            if (music.TryGetProperty("authorName", out var ma)) musicAuthor = ma.GetString();
        }

        // Parse stats from the response
        var views = 0;
        var likes = 0;
        var comments = 0;
        var shares = 0;
        Dictionary<string, object>? stats = null;

        if (item.TryGetProperty("stats", out var statsEl) && statsEl.ValueKind == JsonValueKind.Object)
        {
            stats = new Dictionary<string, object>();

            // Try to get from stats object
            if (statsEl.TryGetProperty("playCount", out var playCount))
                views = (int)playCount.GetInt64();
            if (statsEl.TryGetProperty("diggCount", out var diggCount))
                likes = (int)diggCount.GetInt64();
            if (statsEl.TryGetProperty("commentCount", out var commentCount))
                comments = (int)commentCount.GetInt64();
            if (statsEl.TryGetProperty("shareCount", out var shareCount))
                shares = (int)shareCount.GetInt64();

            // Collect stats into dictionary
            foreach (var prop in statsEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number)
                    stats[prop.Name] = prop.Value.GetInt64();
                else
                    stats[prop.Name] = prop.Value.GetString() ?? "";
            }
        }

        var formatted = DateTimeOffset.FromUnixTimeSeconds(createTime)
            .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        return new TikTokVideoData
        {
            Id = videoId,
            Url = videoUrl,
            Desc = item.TryGetProperty("desc", out var desc) ? desc.GetString() : null,
            CreateTime = createTime,
            CreateTimeFormatted = formatted,
            Author = new TikTokAuthor { UniqueId = uniqueId, Nickname = nickname, Avatar = avatar },
            Music = new TikTokMusic { Title = musicTitle, Author = musicAuthor },
            Stats = stats,
            Views = views,
            Likes = likes,
            Comments = comments,
            Shares = shares
        };
    }
}
