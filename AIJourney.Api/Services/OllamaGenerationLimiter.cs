using AIJourney.Api.Options;
using Microsoft.Extensions.Options;

namespace AIJourney.Api.Services;

public sealed class OllamaGenerationLimiter
{
    private readonly SemaphoreSlim _semaphore;
    private readonly TimeSpan _queueTimeout;

    public OllamaGenerationLimiter(IOptions<OllamaOptions> options)
    {
        var maxConcurrentRequests = Math.Max(1, options.Value.MaxConcurrentRequests);
        _semaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        _queueTimeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.QueueTimeoutSeconds));
    }

    public async Task<IDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var acquired = await _semaphore.WaitAsync(_queueTimeout, cancellationToken);
        return acquired ? new Releaser(_semaphore) : null;
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            semaphore.Release();
            _disposed = true;
        }
    }
}
