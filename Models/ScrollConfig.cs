namespace SocialCrawler.Models;

public class ScrollConfig
{
    public int InitialDelayMin { get; set; } = 3000;
    public int InitialDelayMax { get; set; } = 5000;
    public int ScrollStepsMin { get; set; } = 3;
    public int ScrollStepsMax { get; set; } = 5;
    public int InterStepDelayMin { get; set; } = 400;
    public int InterStepDelayMax { get; set; } = 800;
    public int ScrollDelayMin { get; set; } = 5000;
    public int ScrollDelayMax { get; set; } = 9000;
    public double HumanScrollChance { get; set; } = 0.7;
    public double HumanScrollDelayMin { get; set; } = 0.5;
    public double HumanScrollDelayMax { get; set; } = 1.2;
    public int HumanMouseMoveStepsMin { get; set; } = 15;
    public int HumanMouseMoveStepsMax { get; set; } = 30;
    public double HumanScrollUpChance { get; set; } = 0.3;
    public double HumanScrollUpDelayMin { get; set; } = 0.5;
    public double HumanScrollUpDelayMax { get; set; } = 1.5;
    public int MaxScrolls { get; set; } = 15;
    public int StaleLimit { get; set; } = 4;
    public int PopupDismissDelay { get; set; } = 2000;

    public static ScrollConfig FromRequest(CrawlRequest req)
    {
        var cfg = new ScrollConfig();
        if (req.ScrollInitialDelayMin.HasValue) cfg.InitialDelayMin = req.ScrollInitialDelayMin.Value;
        if (req.ScrollInitialDelayMax.HasValue) cfg.InitialDelayMax = req.ScrollInitialDelayMax.Value;
        if (req.ScrollStepsMin.HasValue) cfg.ScrollStepsMin = req.ScrollStepsMin.Value;
        if (req.ScrollStepsMax.HasValue) cfg.ScrollStepsMax = req.ScrollStepsMax.Value;
        if (req.ScrollInterStepDelayMin.HasValue) cfg.InterStepDelayMin = req.ScrollInterStepDelayMin.Value;
        if (req.ScrollInterStepDelayMax.HasValue) cfg.InterStepDelayMax = req.ScrollInterStepDelayMax.Value;
        if (req.ScrollDelayMin.HasValue) cfg.ScrollDelayMin = req.ScrollDelayMin.Value;
        if (req.ScrollDelayMax.HasValue) cfg.ScrollDelayMax = req.ScrollDelayMax.Value;
        if (req.MaxScrolls.HasValue) cfg.MaxScrolls = req.MaxScrolls.Value;
        if (req.StaleLimit.HasValue) cfg.StaleLimit = req.StaleLimit.Value;
        if (req.HumanScrollChance.HasValue) cfg.HumanScrollChance = req.HumanScrollChance.Value;
        if (req.HumanScrollDelayMin.HasValue) cfg.HumanScrollDelayMin = req.HumanScrollDelayMin.Value;
        if (req.HumanScrollDelayMax.HasValue) cfg.HumanScrollDelayMax = req.HumanScrollDelayMax.Value;
        if (req.HumanMouseMoveStepsMin.HasValue) cfg.HumanMouseMoveStepsMin = req.HumanMouseMoveStepsMin.Value;
        if (req.HumanMouseMoveStepsMax.HasValue) cfg.HumanMouseMoveStepsMax = req.HumanMouseMoveStepsMax.Value;
        if (req.HumanScrollUpChance.HasValue) cfg.HumanScrollUpChance = req.HumanScrollUpChance.Value;
        if (req.HumanScrollUpDelayMin.HasValue) cfg.HumanScrollUpDelayMin = req.HumanScrollUpDelayMin.Value;
        if (req.HumanScrollUpDelayMax.HasValue) cfg.HumanScrollUpDelayMax = req.HumanScrollUpDelayMax.Value;
        if (req.PopupDismissDelay.HasValue) cfg.PopupDismissDelay = req.PopupDismissDelay.Value;
        return cfg;
    }
}
