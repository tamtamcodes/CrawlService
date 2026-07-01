using Microsoft.Playwright;
namespace SocialCrawler.Services.Human;

public class HumanBehaviorProvider : IHumanBehaviorProvider
{
    private readonly ILogger<HumanBehaviorProvider> _logger;
    private readonly List<string> _whitelistedDomains;
    private readonly int _maxRequestsPerSession;
    private int _requestCount;
    private static readonly object LogLock = new();
    private static readonly string AuditLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output", "audit_trail.log");

    public HumanBehaviorProvider(IConfiguration configuration, ILogger<HumanBehaviorProvider> logger)
    {
        _logger = logger;
        
        // Parse configurations
        var domains = configuration.GetSection("HumanizeOptions:WhitelistedDomains").Get<List<string>>();
        _whitelistedDomains = domains ?? new List<string> { "facebook.com", "tiktok.com", "www.facebook.com", "www.tiktok.com" };
        
        _maxRequestsPerSession = configuration.GetValue<int>("HumanizeOptions:MaxRequestsPerSession", 100);
        _requestCount = 0;

        // Ensure output directory exists
        var dir = Path.GetDirectoryName(AuditLogPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private void CheckRateLimit()
    {
        var currentCount = System.Threading.Interlocked.Increment(ref _requestCount);
        if (currentCount > _maxRequestsPerSession)
        {
            throw new InvalidOperationException($"Rate limit exceeded: session request count {currentCount} exceeds configured limit of {_maxRequestsPerSession}");
        }
    }

    private bool IsDomainWhitelisted(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        try
        {
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "https://" + url;
            }
            var uri = new Uri(url);
            var host = uri.Host.ToLower();
            
            return _whitelistedDomains.Any(domain => 
                host == domain.ToLower() || host.EndsWith("." + domain.ToLower()));
        }
        catch
        {
            return false;
        }
    }

    private void LogAuditAction(string targetUrl, string action, string details = "")
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logMessage = $"[{timestamp}] Target: {targetUrl} | Action: {action} | Details: {details}\n";
        
        lock (LogLock)
        {
            File.AppendAllText(AuditLogPath, logMessage);
        }
        _logger.LogInformation("Audit Action: {Action} on {Target} - {Details}", action, targetUrl, details);
    }

    public async Task NavigateToAsync(IPage page, string url)
    {
        CheckRateLimit();

        if (!IsDomainWhitelisted(url))
        {
            LogAuditAction(url, "NAVIGATION_REJECTED", "Domain is not in whitelist");
            throw new UnauthorizedAccessException($"Access to {url} is denied because the domain is not whitelisted.");
        }

        LogAuditAction(url, "NAVIGATE", $"Navigating to {url}");
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    public async Task MoveMouseAsync(IPage page, double x, double y)
    {
        CheckRateLimit();
        var url = page.Url;
        if (!IsDomainWhitelisted(url))
        {
            LogAuditAction(url, "MOUSE_MOVE_REJECTED", "Domain is not in whitelist");
            throw new UnauthorizedAccessException($"Interactions with {url} are denied because the domain is not whitelisted.");
        }

        LogAuditAction(url, "MOUSE_MOVE", $"x: {x}, y: {y}");
        await page.Mouse.MoveAsync((float)x, (float)y);
    }

    public async Task ClickAsync(IPage page, string selector)
    {
        CheckRateLimit();
        var url = page.Url;
        if (!IsDomainWhitelisted(url))
        {
            LogAuditAction(url, "CLICK_REJECTED", "Domain is not in whitelist");
            throw new UnauthorizedAccessException($"Interactions with {url} are denied because the domain is not whitelisted.");
        }

        LogAuditAction(url, "CLICK", $"Selector: {selector}");
        await page.ClickAsync(selector);
    }

    public async Task TypeTextAsync(IPage page, string selector, string text)
    {
        CheckRateLimit();
        var url = page.Url;
        if (!IsDomainWhitelisted(url))
        {
            LogAuditAction(url, "TYPE_REJECTED", "Domain is not in whitelist");
            throw new UnauthorizedAccessException($"Interactions with {url} are denied because the domain is not whitelisted.");
        }

        LogAuditAction(url, "TYPE", $"Selector: {selector}, Text length: {text.Length}");
        await page.FillAsync(selector, text);
    }

    public async Task ScrollAsync(IPage page, string selector, int deltaY)
    {
        CheckRateLimit();
        var url = page.Url;
        if (!IsDomainWhitelisted(url))
        {
            LogAuditAction(url, "SCROLL_REJECTED", "Domain is not in whitelist");
            throw new UnauthorizedAccessException($"Interactions with {url} are denied because the domain is not whitelisted.");
        }

        LogAuditAction(url, "SCROLL", $"Selector: {selector}, Delta Y: {deltaY}");
        await page.Mouse.WheelAsync(0, deltaY);
    }

    public async Task SimulateThinkingIdleAsync(IPage page, int minMs = 500, int maxMs = 2000)
    {
        var url = page.Url;
        var rng = new Random();
        var delay = rng.Next(minMs, maxMs);
        
        LogAuditAction(url, "THINKING_IDLE", $"Delaying for {delay} ms");
        await Task.Delay(delay);
    }
}
