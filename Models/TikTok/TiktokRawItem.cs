using System.Text.Json.Serialization;

namespace SocialCrawler.Models;

public class TikTokRawItem
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("desc")] public string? Desc { get; set; }
    [JsonPropertyName("createTime")] public long CreateTime { get; set; }
    [JsonPropertyName("textLanguage")] public string? TextLanguage { get; set; }
    [JsonPropertyName("textTranslatable")] public bool TextTranslatable { get; set; }
    [JsonPropertyName("AIGCDescription")] public string? AIGCDescription { get; set; }
    [JsonPropertyName("CategoryType")] public int CategoryType { get; set; }
    [JsonPropertyName("HasPromoteEntry")] public int HasPromoteEntry { get; set; }
    [JsonPropertyName("IsHDBitrate")] public bool IsHDBitrate { get; set; }
    [JsonPropertyName("isAd")] public bool IsAd { get; set; }
    [JsonPropertyName("isReviewing")] public bool IsReviewing { get; set; }
    [JsonPropertyName("officalItem")] public bool OfficalItem { get; set; }
    [JsonPropertyName("originalItem")] public bool OriginalItem { get; set; }
    [JsonPropertyName("privateItem")] public bool PrivateItem { get; set; }
    [JsonPropertyName("secret")] public bool Secret { get; set; }
    [JsonPropertyName("collected")] public bool Collected { get; set; }
    [JsonPropertyName("digged")] public bool Digged { get; set; }
    [JsonPropertyName("forFriend")] public bool ForFriend { get; set; }
    [JsonPropertyName("shareEnabled")] public bool ShareEnabled { get; set; }
    [JsonPropertyName("duetEnabled")] public bool DuetEnabled { get; set; }
    [JsonPropertyName("stitchEnabled")] public bool StitchEnabled { get; set; }
    [JsonPropertyName("duetDisplay")] public int DuetDisplay { get; set; }
    [JsonPropertyName("stitchDisplay")] public int StitchDisplay { get; set; }
    [JsonPropertyName("itemCommentStatus")] public int ItemCommentStatus { get; set; }
    [JsonPropertyName("diversificationId")] public int DiversificationId { get; set; }
    [JsonPropertyName("backendSourceEventTracking")] public string? BackendSourceEventTracking { get; set; }

    [JsonPropertyName("author")] public TikTokRawAuthor? Author { get; set; }
    [JsonPropertyName("authorStats")] public TikTokRawAuthorStats? AuthorStats { get; set; }
    [JsonPropertyName("authorStatsV2")] public TikTokRawAuthorStatsV2? AuthorStatsV2 { get; set; }
    [JsonPropertyName("music")] public TikTokRawMusic? Music { get; set; }
    [JsonPropertyName("stats")] public TikTokRawStats? Stats { get; set; }
    [JsonPropertyName("statsV2")] public TikTokRawStatsV2? StatsV2 { get; set; }
    [JsonPropertyName("video")] public TikTokRawVideo? Video { get; set; }
    [JsonPropertyName("challenges")] public List<TikTokRawChallenge>? Challenges { get; set; }
    [JsonPropertyName("contents")] public List<TikTokRawContent>? Contents { get; set; }
    [JsonPropertyName("textExtra")] public List<TikTokRawTextExtra>? TextExtra { get; set; }
    [JsonPropertyName("item_control")] public TikTokRawItemControl? ItemControl { get; set; }
    [JsonPropertyName("creatorAIComment")] public TikTokRawCreatorAIComment? CreatorAIComment { get; set; }
    [JsonIgnore] public string? RawJson { get; set; }
    [JsonIgnore] public TikTokRawTranscript? Transcript { get; set; }
}

public class TikTokRawAuthor
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("uniqueId")] public string? UniqueId { get; set; }
    [JsonPropertyName("nickname")] public string? Nickname { get; set; }
    [JsonPropertyName("signature")] public string? Signature { get; set; }
    [JsonPropertyName("secUid")] public string? SecUid { get; set; }
    [JsonPropertyName("avatarLarger")] public string? AvatarLarger { get; set; }
    [JsonPropertyName("avatarMedium")] public string? AvatarMedium { get; set; }
    [JsonPropertyName("avatarThumb")] public string? AvatarThumb { get; set; }
    [JsonPropertyName("verified")] public bool Verified { get; set; }
    [JsonPropertyName("privateAccount")] public bool PrivateAccount { get; set; }
    [JsonPropertyName("secret")] public bool Secret { get; set; }
    [JsonPropertyName("ftc")] public bool Ftc { get; set; }
    [JsonPropertyName("openFavorite")] public bool OpenFavorite { get; set; }
    [JsonPropertyName("isADVirtual")] public bool IsADVirtual { get; set; }
    [JsonPropertyName("isEmbedBanned")] public bool IsEmbedBanned { get; set; }
    [JsonPropertyName("relation")] public int Relation { get; set; }
    [JsonPropertyName("commentSetting")] public int CommentSetting { get; set; }
    [JsonPropertyName("downloadSetting")] public int DownloadSetting { get; set; }
    [JsonPropertyName("duetSetting")] public int DuetSetting { get; set; }
    [JsonPropertyName("stitchSetting")] public int StitchSetting { get; set; }
    [JsonPropertyName("UserStoryStatus")] public int UserStoryStatus { get; set; }
    [JsonPropertyName("shortDramaCreator")] public TikTokRawShortDramaCreator? ShortDramaCreator { get; set; }
}

public class TikTokRawShortDramaCreator
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
}

public class TikTokRawAuthorStats
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("diggCount")] public long DiggCount { get; set; }
    [JsonPropertyName("followerCount")] public long FollowerCount { get; set; }
    [JsonPropertyName("followingCount")] public long FollowingCount { get; set; }
    [JsonPropertyName("friendCount")] public long FriendCount { get; set; }
    [JsonPropertyName("heart")] public long Heart { get; set; }
    [JsonPropertyName("heartCount")] public long HeartCount { get; set; }
    [JsonPropertyName("videoCount")] public long VideoCount { get; set; }
}

public class TikTokRawAuthorStatsV2
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("diggCount")] public string? DiggCount { get; set; }
    [JsonPropertyName("followerCount")] public string? FollowerCount { get; set; }
    [JsonPropertyName("followingCount")] public string? FollowingCount { get; set; }
    [JsonPropertyName("friendCount")] public string? FriendCount { get; set; }
    [JsonPropertyName("heart")] public string? Heart { get; set; }
    [JsonPropertyName("heartCount")] public string? HeartCount { get; set; }
    [JsonPropertyName("videoCount")] public string? VideoCount { get; set; }
}

public class TikTokRawMusic
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("authorName")] public string? AuthorName { get; set; }
    [JsonPropertyName("playUrl")] public string? PlayUrl { get; set; }
    [JsonPropertyName("coverLarge")] public string? CoverLarge { get; set; }
    [JsonPropertyName("coverMedium")] public string? CoverMedium { get; set; }
    [JsonPropertyName("coverThumb")] public string? CoverThumb { get; set; }
    [JsonPropertyName("duration")] public int Duration { get; set; }
    [JsonPropertyName("shoot_duration")] public int ShootDuration { get; set; }
    [JsonPropertyName("original")] public bool Original { get; set; }
    [JsonPropertyName("private")] public bool Private { get; set; }
    [JsonPropertyName("isCopyrighted")] public bool IsCopyrighted { get; set; }
    [JsonPropertyName("is_commerce_music")] public bool IsCommerceMusic { get; set; }
    [JsonPropertyName("is_unlimited_music")] public bool IsUnlimitedMusic { get; set; }
    [JsonPropertyName("tt2dsp")] public TikTokRawTt2Dsp? Tt2Dsp { get; set; }
}

public class TikTokRawTt2Dsp
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
}

public class TikTokRawStats
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("playCount")] public long PlayCount { get; set; }
    [JsonPropertyName("diggCount")] public long DiggCount { get; set; }
    [JsonPropertyName("commentCount")] public long CommentCount { get; set; }
    [JsonPropertyName("shareCount")] public long ShareCount { get; set; }
    [JsonPropertyName("collectCount")] public long CollectCount { get; set; }
}

public class TikTokRawStatsV2
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("playCount")] public string? PlayCount { get; set; }
    [JsonPropertyName("diggCount")] public string? DiggCount { get; set; }
    [JsonPropertyName("commentCount")] public string? CommentCount { get; set; }
    [JsonPropertyName("shareCount")] public string? ShareCount { get; set; }
    [JsonPropertyName("collectCount")] public string? CollectCount { get; set; }
    [JsonPropertyName("repostCount")] public string? RepostCount { get; set; }
}

public class TikTokRawVideo
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("videoID")] public string? VideoID { get; set; }
    [JsonPropertyName("duration")] public int Duration { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("ratio")] public string? Ratio { get; set; }
    [JsonPropertyName("definition")] public string? Definition { get; set; }
    [JsonPropertyName("format")] public string? Format { get; set; }
    [JsonPropertyName("codecType")] public string? CodecType { get; set; }
    [JsonPropertyName("encodedType")] public string? EncodedType { get; set; }
    [JsonPropertyName("encodeUserTag")] public string? EncodeUserTag { get; set; }
    [JsonPropertyName("videoQuality")] public string? VideoQuality { get; set; }
    [JsonPropertyName("VQScore")] public string? VQScore { get; set; }
    [JsonPropertyName("bitrate")] public long Bitrate { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("playAddr")] public string? PlayAddr { get; set; }
    [JsonPropertyName("downloadAddr")] public string? DownloadAddr { get; set; }
    [JsonPropertyName("cover")] public string? Cover { get; set; }
    [JsonPropertyName("originCover")] public string? OriginCover { get; set; }
    [JsonPropertyName("dynamicCover")] public string? DynamicCover { get; set; }
    [JsonPropertyName("PlayAddrStruct")] public TikTokRawPlayAddrStruct? PlayAddrStruct { get; set; }
    [JsonPropertyName("bitrateInfo")] public List<TikTokRawBitrateInfo>? BitrateInfo { get; set; }
    [JsonPropertyName("claInfo")] public TikTokRawClaInfo? ClaInfo { get; set; }
    [JsonPropertyName("subtitleInfos")] public List<TikTokRawSubtitleInfo>? SubtitleInfos { get; set; }
    [JsonPropertyName("volumeInfo")] public TikTokRawVolumeInfo? VolumeInfo { get; set; }
    [JsonPropertyName("zoomCover")] public TikTokRawZoomCover? ZoomCover { get; set; }
}

public class TikTokRawPlayAddrStruct
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("DataSize")] public long DataSize { get; set; }
    [JsonPropertyName("FileCs")] public string? FileCs { get; set; }
    [JsonPropertyName("FileHash")] public string? FileHash { get; set; }
    [JsonPropertyName("Height")] public int Height { get; set; }
    [JsonPropertyName("Width")] public int Width { get; set; }
    [JsonPropertyName("Uri")] public string? Uri { get; set; }
    [JsonPropertyName("UrlKey")] public string? UrlKey { get; set; }
    [JsonPropertyName("UrlList")] public List<string>? UrlList { get; set; }
}

public class TikTokRawBitrateInfo
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("Bitrate")] public long Bitrate { get; set; }
    [JsonPropertyName("BitrateFPS")] public int BitrateFPS { get; set; }
    [JsonPropertyName("CodecType")] public string? CodecType { get; set; }
    [JsonPropertyName("Format")] public string? Format { get; set; }
    [JsonPropertyName("GearName")] public string? GearName { get; set; }
    [JsonPropertyName("MVMAF")] public string? MVMAF { get; set; }
    [JsonPropertyName("QualityType")] public int QualityType { get; set; }
    [JsonPropertyName("VideoExtra")] public string? VideoExtra { get; set; }
    [JsonPropertyName("PlayAddr")] public TikTokRawPlayAddrStruct? PlayAddr { get; set; }
}

public class TikTokRawClaInfo
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("captionsType")] public int CaptionsType { get; set; }
    [JsonPropertyName("enableAutoCaption")] public bool EnableAutoCaption { get; set; }
    [JsonPropertyName("hasOriginalAudio")] public bool HasOriginalAudio { get; set; }
    [JsonPropertyName("captionInfos")] public List<TikTokRawCaptionInfo>? CaptionInfos { get; set; }
    [JsonPropertyName("originalLanguageInfo")] public TikTokRawLanguageInfo? OriginalLanguageInfo { get; set; }
}

public class TikTokRawCaptionInfo
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("claSubtitleID")] public string? ClaSubtitleID { get; set; }
    [JsonPropertyName("subID")] public string? SubID { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("languageCode")] public string? LanguageCode { get; set; }
    [JsonPropertyName("languageID")] public string? LanguageID { get; set; }
    [JsonPropertyName("captionFormat")] public string? CaptionFormat { get; set; }
    [JsonPropertyName("subtitleType")] public string? SubtitleType { get; set; }
    [JsonPropertyName("translationType")] public string? TranslationType { get; set; }
    [JsonPropertyName("variant")] public string? Variant { get; set; }
    [JsonPropertyName("expire")] public string? Expire { get; set; }
    [JsonPropertyName("isAutoGen")] public bool IsAutoGen { get; set; }
    [JsonPropertyName("isOriginalCaption")] public bool IsOriginalCaption { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("urlList")] public List<string>? UrlList { get; set; }
}

public class TikTokRawLanguageInfo
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("languageCode")] public string? LanguageCode { get; set; }
    [JsonPropertyName("languageID")] public string? LanguageID { get; set; }
    [JsonPropertyName("canTranslateRealTimeNoCheck")] public bool CanTranslateRealTimeNoCheck { get; set; }
}

public class TikTokRawSubtitleInfo
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("Format")] public string? Format { get; set; }
    [JsonPropertyName("LanguageCodeName")] public string? LanguageCodeName { get; set; }
    [JsonPropertyName("LanguageID")] public string? LanguageID { get; set; }
    [JsonPropertyName("Size")] public long Size { get; set; }
    [JsonPropertyName("Source")] public string? Source { get; set; }
    [JsonPropertyName("Url")] public string? Url { get; set; }
    [JsonPropertyName("UrlExpire")] public long UrlExpire { get; set; }
    [JsonPropertyName("Version")] public string? Version { get; set; }
}

public class TikTokRawVolumeInfo
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("Loudness")] public double Loudness { get; set; }
    [JsonPropertyName("Peak")] public double Peak { get; set; }
}

public class TikTokRawZoomCover
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("240")] public string? Cover240 { get; set; }
    [JsonPropertyName("480")] public string? Cover480 { get; set; }
    [JsonPropertyName("720")] public string? Cover720 { get; set; }
    [JsonPropertyName("960")] public string? Cover960 { get; set; }
}

public class TikTokRawChallenge
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("desc")] public string? Desc { get; set; }
    [JsonPropertyName("coverLarger")] public string? CoverLarger { get; set; }
    [JsonPropertyName("coverMedium")] public string? CoverMedium { get; set; }
    [JsonPropertyName("coverThumb")] public string? CoverThumb { get; set; }
    [JsonPropertyName("profileLarger")] public string? ProfileLarger { get; set; }
    [JsonPropertyName("profileMedium")] public string? ProfileMedium { get; set; }
    [JsonPropertyName("profileThumb")] public string? ProfileThumb { get; set; }
}

public class TikTokRawContent
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("desc")] public string? Desc { get; set; }
    [JsonPropertyName("textExtra")] public List<TikTokRawTextExtra>? TextExtra { get; set; }
}

public class TikTokRawTextExtra
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("awemeId")] public string? AwemeId { get; set; }
    [JsonPropertyName("hashtagName")] public string? HashtagName { get; set; }
    [JsonPropertyName("start")] public int Start { get; set; }
    [JsonPropertyName("end")] public int End { get; set; }
    [JsonPropertyName("type")] public int Type { get; set; }
    [JsonPropertyName("subType")] public int SubType { get; set; }
    [JsonPropertyName("isCommerce")] public bool IsCommerce { get; set; }
}

public class TikTokRawItemControl
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("can_repost")] public bool CanRepost { get; set; }
}

public class TikTokRawCreatorAIComment
{
    [JsonPropertyName("$id")] public string? DollarId { get; set; }
    [JsonPropertyName("eligibleVideo")] public bool EligibleVideo { get; set; }
    [JsonPropertyName("hasAITopic")] public bool HasAITopic { get; set; }
    [JsonPropertyName("notEligibleReason")] public int NotEligibleReason { get; set; }
}

public class TikTokRawTranscript
{
    [JsonPropertyName("has_transcript")] public bool HasTranscript { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("is_auto_generated")] public bool IsAutoGenerated { get; set; }
    [JsonPropertyName("captions")] public List<TikTokRawCaptionEntry> Captions { get; set; } = new();
}

public class TikTokRawCaptionEntry
{
    [JsonPropertyName("start_time")] public string? StartTime { get; set; }
    [JsonPropertyName("end_time")] public string? EndTime { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
}