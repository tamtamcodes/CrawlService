namespace SocialCrawler.Services;

public class CrawlStateService
{
    private readonly SemaphoreSlim _crawlLock = new(1, 1);
    private readonly object _statusLock = new();
    private CancellationTokenSource _cts = new();

    private string _state = "idle";
    private string? _target;
    private DateTime? _startedAt;

    public SemaphoreSlim CrawlLock => _crawlLock;

    public CancellationToken CancellationToken => _cts.Token;

    public bool IsRunning
    {
        get
        {
            lock (_statusLock) return _state == "running";
        }
    }

    public void SetRunning(string target)
    {
        lock (_statusLock)
        {
            _state = "running";
            _target = target;
            _startedAt = DateTime.UtcNow;
        }
    }

    public void SetIdle()
    {
        lock (_statusLock)
        {
            _state = "idle";
            _target = null;
            _startedAt = null;
        }
    }

    public void ResetCancellation()
    {
        if (_cts.IsCancellationRequested)
        {
            _cts.Dispose();
            _cts = new CancellationTokenSource();
        }
        else
        {
            _cts.TryReset();
        }
    }

    public void Cancel()
    {
        _cts.Cancel();
    }

    public (string State, string? Target, double? ElapsedSeconds, bool Locked) GetStatus()
    {
        lock (_statusLock)
        {
            var elapsed = _startedAt.HasValue
                ? Math.Round((DateTime.UtcNow - _startedAt.Value).TotalSeconds, 1)
                : (double?)null;
            return (_state, _target, elapsed, _crawlLock.CurrentCount == 0);
        }
    }
}
