using Microsoft.Playwright;

namespace SocialCrawler.Services.Human;

public interface IHumanBehaviorProvider
{
    Task NavigateToAsync(IPage page, string url);
    Task MoveMouseAsync(IPage page, double x, double y);
    Task ClickAsync(IPage page, string selector);
    Task TypeTextAsync(IPage page, string selector, string text);
    Task ScrollAsync(IPage page, string selector, int deltaY);
    Task SimulateThinkingIdleAsync(IPage page, int minMs = 500, int maxMs = 2000);
}
