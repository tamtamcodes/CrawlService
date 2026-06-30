using System.Text.Json.Serialization;

namespace SocialCrawler.Models;

public class CrawlApiResponse<TItem>
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "ok";

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "";

    [JsonPropertyName("targets")]
    public List<string> Targets { get; set; } = new();

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("items")]
    public List<TItem> Items { get; set; } = new();

    [JsonPropertyName("events")]
    public List<CrawlEvent> Events { get; set; } = new();

    [JsonPropertyName("errors")]
    public List<CrawlEvent> Errors { get; set; } = new();

    [JsonPropertyName("aborted")]
    public bool Aborted { get; set; }

    [JsonPropertyName("started_at")]
    public string StartedAt { get; set; } = "";

    [JsonPropertyName("finished_at")]
    public string FinishedAt { get; set; } = "";

    [JsonPropertyName("elapsed_ms")]
    public long ElapsedMs { get; set; }
}
