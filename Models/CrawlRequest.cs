using System.Text.Json;
using System.Text.Json.Serialization;

namespace SocialCrawler.Models;

public class CrawlRequest
{
    [JsonPropertyName("targets")]
    public List<string>? Targets { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("period")]
    public string Period { get; set; } = "30 days";

    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    [JsonPropertyName("end_date")]
    public string? EndDate { get; set; }

    [JsonPropertyName("cookies")]
    public JsonElement? Cookies { get; set; }

    [JsonPropertyName("facebookMaxPosts")]
    public int FacebookMaxPosts { get; set; } = 50;

    [JsonPropertyName("stop_urls")]
    public List<string>? StopUrls { get; set; }

    [JsonPropertyName("scroll_initial_delay_min")]
    public int? ScrollInitialDelayMin { get; set; }

    [JsonPropertyName("scroll_initial_delay_max")]
    public int? ScrollInitialDelayMax { get; set; }

    [JsonPropertyName("scroll_steps_min")]
    public int? ScrollStepsMin { get; set; }

    [JsonPropertyName("scroll_steps_max")]
    public int? ScrollStepsMax { get; set; }

    [JsonPropertyName("scroll_inter_step_delay_min")]
    public int? ScrollInterStepDelayMin { get; set; }

    [JsonPropertyName("scroll_inter_step_delay_max")]
    public int? ScrollInterStepDelayMax { get; set; }

    [JsonPropertyName("scroll_delay_min")]
    public int? ScrollDelayMin { get; set; }

    [JsonPropertyName("scroll_delay_max")]
    public int? ScrollDelayMax { get; set; }

    [JsonPropertyName("max_scrolls")]
    public int? MaxScrolls { get; set; }

    [JsonPropertyName("stale_limit")]
    public int? StaleLimit { get; set; }

    [JsonPropertyName("human_scroll_chance")]
    public double? HumanScrollChance { get; set; }

    [JsonPropertyName("human_scroll_delay_min")]
    public double? HumanScrollDelayMin { get; set; }

    [JsonPropertyName("human_scroll_delay_max")]
    public double? HumanScrollDelayMax { get; set; }

    [JsonPropertyName("human_mouse_move_steps_min")]
    public int? HumanMouseMoveStepsMin { get; set; }

    [JsonPropertyName("human_mouse_move_steps_max")]
    public int? HumanMouseMoveStepsMax { get; set; }

    [JsonPropertyName("human_scroll_up_chance")]
    public double? HumanScrollUpChance { get; set; }

    [JsonPropertyName("human_scroll_up_delay_min")]
    public double? HumanScrollUpDelayMin { get; set; }

    [JsonPropertyName("human_scroll_up_delay_max")]
    public double? HumanScrollUpDelayMax { get; set; }

    [JsonPropertyName("popup_dismiss_delay")]
    public int? PopupDismissDelay { get; set; }
}

public class ValidateSessionRequest
{
    [JsonPropertyName("session_data")]
    public JsonElement SessionData { get; set; }
}
