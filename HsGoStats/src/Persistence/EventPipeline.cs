using System.Threading.Channels;
using HsGoStats.Configuration;
using HsGoStats.Domain;

namespace HsGoStats.Persistence;

public sealed class EventPipeline
{
    private readonly Channel<NormalizedStatEvent> _channel;
    private readonly MySqlStore _store;
    private readonly DurableSpool _spool;
    private readonly PersistenceConfig _config;
    private readonly Action<string> _info;
    private readonly Action<string, Exception?> _error;
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private long _dropped;

    public EventPipeline(MySqlStore store, DurableSpool spool, PersistenceConfig config, Action<string> info, Action<string, Exception?> error)
    {
        _store = store;
        _spool = spool;
        _config = config;
        _info = info;
        _error = error;
        _channel = Channel.CreateBounded<NormalizedStatEvent>(new BoundedChannelOptions(config.MaxQueuedEvents)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public long DroppedEvents => Interlocked.Read(ref _dropped);

    public void Start() => _worker = Task.Run(RunAsync);

    public bool TryEnqueue(NormalizedStatEvent item)
    {
        if (_channel.Writer.TryWrite(item)) return true;
        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % 100 == 0) _error($"Statistics queue is full; dropped event count: {dropped}.", null);
        return false;
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        _channel.Writer.TryComplete();
        if (_worker is null) return;
        var completed = await Task.WhenAny(_worker, Task.Delay(timeout));
        if (completed != _worker)
        {
            _error("Timed out while flushing the statistics queue; spooled items will replay next start.", null);
            _stop.Cancel();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            var replay = await _spool.LoadAsync(_stop.Token);
            if (replay.Count > 0)
            {
                _info($"Replaying {replay.Count} durable pending statistics events.");
                foreach (var chunk in replay.Chunk(_config.MaxBatchSize)) await PersistWithRetryAsync(chunk, alreadySpooled: true, _stop.Token);
            }

            while (await _channel.Reader.WaitToReadAsync(_stop.Token))
            {
                var batch = new List<NormalizedStatEvent>(_config.MaxBatchSize);
                var deadline = DateTimeOffset.UtcNow.AddSeconds(_config.BatchIntervalSeconds);
                while (batch.Count < _config.MaxBatchSize)
                {
                    while (batch.Count < _config.MaxBatchSize && _channel.Reader.TryRead(out var item))
                    {
                        await _spool.AppendAsync(item, _stop.Token);
                        batch.Add(item);
                    }
                    if (batch.Count >= _config.MaxBatchSize || DateTimeOffset.UtcNow >= deadline || _channel.Reader.Completion.IsCompleted) break;
                    var remaining = deadline - DateTimeOffset.UtcNow;
                    var ready = _channel.Reader.WaitToReadAsync(_stop.Token).AsTask();
                    await Task.WhenAny(ready, Task.Delay(remaining, _stop.Token));
                }
                if (batch.Count > 0) await PersistWithRetryAsync(batch, alreadySpooled: true, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { _error("Statistics persistence worker stopped unexpectedly.", ex); }
    }

    private async Task PersistWithRetryAsync(IReadOnlyCollection<NormalizedStatEvent> batch, bool alreadySpooled, CancellationToken token)
    {
        if (!alreadySpooled) foreach (var item in batch) await _spool.AppendAsync(item, token);
        Exception? last = null;
        for (var attempt = 1; attempt <= _config.RetryMaxAttempts; attempt++)
        {
            try
            {
                await _store.WriteBatchAsync(batch, token);
                await _spool.AcknowledgeAsync(batch.Select(x => x.EventId), token);
                return;
            }
            catch (Exception ex) when (attempt < _config.RetryMaxAttempts)
            {
                last = ex;
                var delay = TimeSpan.FromSeconds(Math.Min(60, _config.RetryInitialDelaySeconds * Math.Pow(2, attempt - 1)));
                _error($"Database batch attempt {attempt} failed; retrying in {delay.TotalSeconds:0} seconds.", ex);
                await Task.Delay(delay, token);
            }
            catch (Exception ex) { last = ex; break; }
        }
        _error($"Database batch failed after {_config.RetryMaxAttempts} attempts; {batch.Count} events remain in the durable spool.", last);
    }
}
