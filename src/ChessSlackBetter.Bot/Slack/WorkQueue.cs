using System.Threading.Channels;

namespace ChessSlackBetter.Bot.Slack;

/// <summary>
/// Slack wants an HTTP 200 within 3 seconds, so endpoints acknowledge immediately and queue the real work here.
/// Items run one at a time, which also keeps two moves in the same game from racing each other.
/// </summary>
public sealed class WorkQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel =
        Channel.CreateUnbounded<Func<IServiceProvider, CancellationToken, Task>>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Func<IServiceProvider, CancellationToken, Task> work) => _channel.Writer.TryWrite(work);

    public IAsyncEnumerable<Func<IServiceProvider, CancellationToken, Task>> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}

public sealed class WorkQueueService(WorkQueue queue, IServiceScopeFactory scopes, ILogger<WorkQueueService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await work(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to process Slack work item");
            }
        }
    }
}
