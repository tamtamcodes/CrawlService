using System.Text.Json;
using SocialCrawler.Models;

namespace SocialCrawler.Services;

public static class FacebookParser
{
    private static readonly string[] SubattachmentFields =
    {
        "all_subattachments", "five_photos_subattachments", "four_photos_subattachments",
        "three_photos_subattachments", "two_photos_subattachments", "frame_sublayout_subattachments"
    };

    public static PostData? ExtractPostFromStoryNode(JsonElement story)
    {
        try
        {
            var postId = GetString(story, "post_id");
            if (string.IsNullOrEmpty(postId)) return null;

            var postUrl = "";
            DateTime? createdDate = null;

            try
            {
                // creation_time có sẵn ở root level — ưu tiên đọc từ đây
                if (story.TryGetProperty("creation_time", out var rootCt) && rootCt.TryGetInt64(out var rootTs))
                {
                    createdDate = DateTimeOffset.FromUnixTimeSeconds(rootTs).UtcDateTime;
                }

                // URL: tìm LongerTimestampStrategy trong metadata array (index không cố định!)
                var metaArr = story
                    .Nav("comet_sections")?.Nav("context_layout")?.Nav("story")
                    ?.Nav("comet_sections")?.GetProp("metadata");
                if (metaArr.HasValue && metaArr.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in metaArr.Value.EnumerateArray())
                    {
                        var tn = GetString(item, "__typename");
                        if (tn == "CometFeedStoryLongerTimestampStrategy")
                        {
                            var tsStory = item.Nav("story");
                            if (tsStory.HasValue)
                            {
                                postUrl = GetString(tsStory.Value, "url") ?? "";
                                if (createdDate == null && tsStory.Value.TryGetProperty("creation_time", out var ct) && ct.TryGetInt64(out var ts))
                                    createdDate = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
                            }
                            break;
                        }
                    }
                }

                // URL fallback: từ message story
                if (string.IsNullOrEmpty(postUrl))
                {
                    var msgUrl = story.Nav("comet_sections")?.Nav("content")?.Nav("story")
                        ?.Nav("comet_sections")?.Nav("message")?.Nav("story")?.GetProp("url");
                    if (msgUrl.HasValue) postUrl = msgUrl.Value.GetString() ?? "";
                }
            }
            catch { }

            var authorName = "";
            var authorId = "";
            var authorUrl = "";

            try
            {
                var owning = story.Nav("feedback")?.Nav("owning_profile");
                if (owning.HasValue)
                {
                    authorName = GetString(owning.Value, "name") ?? "";
                    authorId = GetString(owning.Value, "id") ?? "";
                }
            }
            catch { }

            try
            {
                JsonElement? actor = null;
                if (story.TryGetProperty("actors", out var actors) && actors.ValueKind == JsonValueKind.Array && actors.GetArrayLength() > 0)
                    actor = actors[0];

                if (!actor.HasValue)
                {
                    actor = story.Nav("comet_sections")?.Nav("context_layout")?.Nav("story")
                        ?.Nav("comet_sections")?.Nav("actor_photo")?.Nav("story")?.NavArray("actors", 0);
                }

                if (actor.HasValue)
                {
                    authorUrl = GetString(actor.Value, "url") ?? GetString(actor.Value, "profile_url") ?? "";
                    if (string.IsNullOrEmpty(authorName)) authorName = GetString(actor.Value, "name") ?? "";
                    if (string.IsNullOrEmpty(authorId)) authorId = GetString(actor.Value, "id") ?? "";
                }
            }
            catch { }

            if (string.IsNullOrEmpty(authorId))
            {
                try
                {
                    var owning = story.Nav("comet_sections")?.Nav("feedback")?.Nav("story")
                        ?.Nav("feedback_context")?.Nav("feedback_target_with_context")?.Nav("owning_profile");
                    if (owning.HasValue)
                    {
                        if (string.IsNullOrEmpty(authorName)) authorName = GetString(owning.Value, "name") ?? "";
                        authorId = GetString(owning.Value, "id") ?? "";
                    }
                }
                catch { }
            }

            var text = ExtractText(story);

            int likeCount = 0, commentCount = 0, shareCount = 0;

            try
            {
                var renderers = story.Nav("comet_sections")?.Nav("feedback")?.Nav("story")
                    ?.Nav("story_ufi_container")?.Nav("story")?.Nav("feedback_context")
                    ?.Nav("feedback_target_with_context")?.Nav("comet_ufi_summary_and_actions_renderer")
                    ?.Nav("feedback")?.GetProp("adaptive_ufi_action_renderers");

                if (renderers.HasValue && renderers.Value.ValueKind == JsonValueKind.Array)
                {
                    var arr = renderers.Value;
                    if (arr.GetArrayLength() > 0)
                    {
                        if (arr[0].Nav("feedback")?.Nav("reaction_count")?.GetProp("count") is { } lc)
                            likeCount = TryGetInt(lc);
                    }
                    if (arr.GetArrayLength() > 1)
                    {
                        if (arr[1].Nav("feedback")?.Nav("comment_rendering_instance")?.Nav("comments")?.GetProp("total_count") is { } cc)
                            commentCount = TryGetInt(cc);
                    }
                    if (arr.GetArrayLength() > 2)
                    {
                        if (arr[2].Nav("feedback")?.Nav("share_count")?.GetProp("count") is { } sc)
                            shareCount = TryGetInt(sc);
                    }
                }
            }
            catch { }

            var images = new List<string>();
            var videos = new List<string>();

            if (story.TryGetProperty("attachments", out var atts) && atts.ValueKind == JsonValueKind.Array)
                ParseAttachments(atts, images, videos);

            try
            {
                var atts2 = story.Nav("comet_sections")?.Nav("content")?.Nav("story")?.GetProp("attachments");
                if (atts2.HasValue && atts2.Value.ValueKind == JsonValueKind.Array)
                    ParseAttachments(atts2.Value, images, videos);
            }
            catch { }

            if (videos.Count == 0 && !string.IsNullOrEmpty(postUrl) && postUrl.Contains("/reel/"))
                videos.Add(postUrl);

            if (string.IsNullOrEmpty(authorUrl) && !string.IsNullOrEmpty(authorId))
                authorUrl = $"https://www.facebook.com/{authorId}";

            var imageUrl = images.Count > 0 ? images[0] : null;

            return new PostData
            {
                PostUrl = !string.IsNullOrEmpty(postUrl) ? postUrl : $"https://www.facebook.com/{authorId}/posts/{postId}",
                Caption = text,
                ImageUrl = imageUrl,
                PublishedAt = createdDate?.ToString("o"),
                Views = 0,
                Likes = likeCount,
                Comments = commentCount,
                Shares = shareCount,
                AuthorName = authorName,
                AuthorId = authorId,
                Images = images,
                Videos = videos
            };
        }
        catch
        {
            return null;
        }
    }

    public static string ExtractText(JsonElement story)
    {
        try
        {
            var text = story.Nav("comet_sections")?.Nav("content")?.Nav("story")
                ?.Nav("message")?.GetProp("text");
            if (text.HasValue && text.Value.ValueKind == JsonValueKind.String)
            {
                var t = text.Value.GetString();
                if (!string.IsNullOrEmpty(t)) return t;
            }
        }
        catch { }

        try
        {
            var text = story.Nav("comet_sections")?.Nav("content")?.Nav("story")
                ?.Nav("comet_sections")?.Nav("message_container")?.Nav("story")
                ?.Nav("message")?.GetProp("text");
            if (text.HasValue && text.Value.ValueKind == JsonValueKind.String)
            {
                var t = text.Value.GetString();
                if (!string.IsNullOrEmpty(t)) return t;
            }
        }
        catch { }

        try
        {
            var blocks = story.Nav("comet_sections")?.Nav("content")?.Nav("story")
                ?.Nav("comet_sections")?.Nav("message")?.GetProp("rich_message");
            if (blocks.HasValue && blocks.Value.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var b in blocks.Value.EnumerateArray())
                {
                    var t = GetString(b, "text");
                    if (!string.IsNullOrEmpty(t)) parts.Add(t);
                }
                if (parts.Count > 0) return string.Join("\n", parts);
            }
        }
        catch { }

        try
        {
            var text = story.Nav("comet_sections")?.Nav("content")?.Nav("story")
                ?.Nav("comet_sections")?.Nav("message")?.Nav("story")
                ?.Nav("message")?.GetProp("text");
            if (text.HasValue && text.Value.ValueKind == JsonValueKind.String)
            {
                var t = text.Value.GetString();
                if (!string.IsNullOrEmpty(t)) return t;
            }
        }
        catch { }

        return "";
    }

    private static void ParseAttachments(JsonElement attachments, List<string> images, List<string> videos)
    {
        foreach (var att in attachments.EnumerateArray())
        {
            var attachment = att.Nav("styles")?.Nav("attachment");
            if (!attachment.HasValue) continue;

            var media = attachment.Value.GetPropOpt("media");
            if (media.HasValue) CollectMedia(media.Value, images, videos);

            var styleInfos = attachment.Value.GetPropOpt("style_infos");
            if (styleInfos.HasValue && styleInfos.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var info in styleInfos.Value.EnumerateArray())
                {
                    var reelAtts = info.Nav("fb_shorts_story")?.GetProp("attachments");
                    if (reelAtts.HasValue && reelAtts.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var a in reelAtts.Value.EnumerateArray())
                        {
                            var m = a.GetPropOpt("media");
                            if (m.HasValue) CollectMedia(m.Value, images, videos);
                        }
                    }
                }
            }

            var bestNodes = BestSubattachmentNodes(attachment.Value);
            foreach (var node in bestNodes)
            {
                var m = node.GetPropOpt("media");
                if (m.HasValue) CollectMedia(m.Value, images, videos);
            }
        }
    }

    private static List<JsonElement> BestSubattachmentNodes(JsonElement attachment)
    {
        var bestNodes = new List<JsonElement>();
        var bestUsable = -1;

        foreach (var field in SubattachmentFields)
        {
            var block = attachment.GetPropOpt(field);
            if (!block.HasValue) continue;
            var nodes = block.Value.GetPropOpt("nodes");
            if (!nodes.HasValue || nodes.Value.ValueKind != JsonValueKind.Array) continue;

            var usable = 0;
            var current = new List<JsonElement>();

            foreach (var n in nodes.Value.EnumerateArray())
            {
                current.Add(n);
                var m = n.GetPropOpt("media");
                if (m.HasValue && (ExtractImageUri(m.Value) != null || ExtractVideoUrl(m.Value) != null))
                    usable++;
            }

            if (usable > bestUsable)
            {
                bestUsable = usable;
                bestNodes = current;
            }
        }

        return bestNodes;
    }

    private static void CollectMedia(JsonElement media, List<string> images, List<string> videos)
    {
        var typename = GetString(media, "__typename") ?? "";
        var uri = ExtractImageUri(media);

        if (uri != null)
        {
            if (!images.Contains(uri)) images.Add(uri);
        }
        else if (typename == "Video")
        {
            var thumb = ExtractVideoThumbnail(media);
            if (thumb != null && !images.Contains(thumb)) images.Add(thumb);

            var v = ExtractVideoUrl(media);
            if (v != null && !videos.Contains(v)) videos.Add(v);

            var permalink = GetString(media, "permalink_url") ?? GetString(media, "shareable_url") ?? "";
            if (!string.IsNullOrEmpty(permalink) && !videos.Contains(permalink)) videos.Add(permalink);
        }
        else if (typename == "ExternalUrl")
        {
            var link = GetString(media, "url") ?? GetString(media, "playable_url") ?? "";
            if (!string.IsNullOrEmpty(link) && link.Contains("reel") && !videos.Contains(link))
                videos.Add(link);
        }
    }

    private static string? ExtractImageUri(JsonElement media)
    {
        foreach (var key in new[] { "photo_image", "image", "viewer_image", "large_share_image", "flexible_height_share_image" })
        {
            var obj = media.GetPropOpt(key);
            if (obj.HasValue && obj.Value.ValueKind == JsonValueKind.Object)
            {
                var uri = GetString(obj.Value, "uri");
                if (!string.IsNullOrEmpty(uri)) return uri;
            }
        }
        return null;
    }

    private static string? ExtractVideoUrl(JsonElement media)
    {
        var candidates = new List<JsonElement> { media };
        var vgr = media.Nav("video_grid_renderer")?.Nav("video");
        if (vgr.HasValue) candidates.Add(vgr.Value);

        foreach (var obj in candidates)
        {
            var urls1 = obj.Nav("videoDeliveryResponseFragment")?.Nav("videoDeliveryResponseResult")
                ?.GetPropOpt("progressive_urls");
            if (urls1.HasValue && urls1.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in urls1.Value.EnumerateArray())
                {
                    var u = GetString(item, "progressive_url");
                    if (!string.IsNullOrEmpty(u)) return u;
                }
            }

            var urls2 = obj.Nav("video_delivery_response")?.GetPropOpt("progressive_urls");
            if (urls2.HasValue && urls2.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in urls2.Value.EnumerateArray())
                {
                    var u = GetString(item, "progressive_url");
                    if (!string.IsNullOrEmpty(u)) return u;
                }
            }

            foreach (var key in new[] { "browser_native_hd_url", "browser_native_sd_url", "playable_url", "url" })
            {
                var u = GetString(obj, key);
                if (!string.IsNullOrEmpty(u)) return u;
            }
        }

        return null;
    }

    private static string? ExtractVideoThumbnail(JsonElement media)
    {
        var candidates = new List<JsonElement> { media };
        var vid = media.GetPropOpt("video");
        if (vid.HasValue) candidates.Add(vid.Value);
        var vgr = media.Nav("video_grid_renderer")?.Nav("video");
        if (vgr.HasValue) candidates.Add(vgr.Value);

        foreach (var obj in candidates)
        {
            var uri = obj.Nav("preferred_thumbnail")?.Nav("image")?.GetPropOpt("uri");
            if (uri.HasValue && uri.Value.ValueKind == JsonValueKind.String)
            {
                var u = uri.Value.GetString();
                if (!string.IsNullOrEmpty(u)) return u;
            }

            var fft = GetString(obj, "first_frame_thumbnail");
            if (!string.IsNullOrEmpty(fft)) return fft;

            var lsi = obj.Nav("large_share_image")?.GetPropOpt("uri");
            if (lsi.HasValue && lsi.Value.ValueKind == JsonValueKind.String)
            {
                var u = lsi.Value.GetString();
                if (!string.IsNullOrEmpty(u)) return u;
            }

            var ti = obj.Nav("thumbnailImage")?.GetPropOpt("uri");
            if (ti.HasValue && ti.Value.ValueKind == JsonValueKind.String)
            {
                var u = ti.Value.GetString();
                if (!string.IsNullOrEmpty(u)) return u;
            }
        }

        return null;
    }

    public static void FindStoryNodes(JsonElement obj, List<JsonElement> stories, int depth = 0)
    {
        if (depth > 30) return;

        if (obj.ValueKind == JsonValueKind.Object)
        {
            var typename = GetString(obj, "__typename") ?? "";
            var postId = GetString(obj, "post_id") ?? "";

            if (typename == "Story" && !string.IsNullOrEmpty(postId))
            {
                stories.Add(obj);
                return;
            }

            if (obj.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("node", out var node) && node.ValueKind == JsonValueKind.Object)
                {
                    FindStoryNodes(node, stories, depth + 1);
                    return;
                }
            }

            foreach (var prop in obj.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    FindStoryNodes(prop.Value, stories, depth + 1);
            }
        }
        else if (obj.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in obj.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var tn = GetString(item, "__typename") ?? "";
                var pid = GetString(item, "post_id") ?? "";
                if (tn == "Story" && !string.IsNullOrEmpty(pid))
                {
                    stories.Add(item);
                    continue;
                }
                FindStoryNodes(item, stories, depth + 1);
            }
        }
    }

    public static List<JsonElement> ParseGraphQlResponse(string text)
    {
        var clean = text.Trim();
        if (clean.StartsWith("for (;;);"))
            clean = clean[9..].Trim();

        if (clean.Length < 100) return new List<JsonElement>();

        var stories = new List<JsonElement>();

        foreach (var line in clean.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 2) continue;

            try
            {
                var doc = JsonDocument.Parse(trimmed);
                FindStoryNodes(doc.RootElement, stories);
            }
            catch
            {
                var pos = 0;
                while (pos < trimmed.Length)
                {
                    try
                    {
                        var sub = trimmed[pos..];
                        var doc = JsonDocument.Parse(sub);
                        FindStoryNodes(doc.RootElement, stories);
                        break;
                    }
                    catch
                    {
                        pos++;
                        if (pos >= trimmed.Length) break;
                    }
                }
            }
        }

        return stories;
    }

    private static string? GetString(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString();
        return null;
    }

    private static int TryGetInt(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s)) return s;
        return 0;
    }
}

internal static class JsonElementExtensions
{
    public static JsonElement? Nav(this JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (el.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.Object)
            return val;
        return null;
    }

    public static JsonElement? Nav(this JsonElement? el, string key)
        => el.HasValue ? el.Value.Nav(key) : null;

    public static JsonElement? NavArray(this JsonElement el, string key, int index)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        if (index >= arr.GetArrayLength()) return null;
        return arr[index];
    }

    public static JsonElement? NavArray(this JsonElement? el, string key, int index)
        => el.HasValue ? el.Value.NavArray(key, index) : null;

    public static JsonElement? GetProp(this JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var val)) return val;
        return null;
    }

    public static JsonElement? GetProp(this JsonElement? el, string key)
        => el.HasValue ? el.Value.GetProp(key) : null;

    public static JsonElement? GetPropOpt(this JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (el.TryGetProperty(key, out var val)) return val;
        return null;
    }
}
