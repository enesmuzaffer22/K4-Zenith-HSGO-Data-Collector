using System.Text.Json;
using System.Threading.Channels;
using HsGoStats.Configuration;
using HsGoStats.Domain;

namespace HsGoStats.Persistence;

public sealed class PlayerSessionPipeline
{
    private readonly Channel<PlayerSessionSignal> _channel;
    private readonly PlayerSessionRepository _repository;
    private readonly PersistenceConfig _config;
    private readonly string _spoolPath;
    private readonly Action<string, Exception?> _error;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _spoolGate = new(1, 1);
    private readonly Dictionary<Guid, PlayerSessionSignal> _pending = [];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TaskCompletionSource _replayReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _worker;
    private long _enqueued;
    private long _persisted;
    private string? _lastFailure;

    public PlayerSessionPipeline(PlayerSessionRepository repository, PersistenceConfig config, string spoolPath,
        Action<string, Exception?> error)
    {
        _repository = repository; _config = config; _spoolPath = spoolPath; _error = error;
        _channel = Channel.CreateBounded<PlayerSessionSignal>(new BoundedChannelOptions(config.MaxQueuedEvents)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    }

    public async Task StartAsync(CancellationToken token)
    {
        _worker = Task.Run(RunAsync);
        await _replayReady.Task.WaitAsync(token);
    }
    public long EnqueuedEvents => Interlocked.Read(ref _enqueued);
    public long PersistedEvents => Interlocked.Read(ref _persisted);
    public bool IsRunning => _worker is { IsCompleted: false };
    public string? LastFailure => Volatile.Read(ref _lastFailure);

    public bool TryEnqueue(PlayerSessionSignal signal)
    {
        if (!_channel.Writer.TryWrite(signal)) return false;
        Interlocked.Increment(ref _enqueued);
        return true;
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        _channel.Writer.TryComplete();
        if (_worker is null) return;
        if (await Task.WhenAny(_worker, Task.Delay(timeout)) != _worker) _stop.Cancel();
    }

    private async Task RunAsync()
    {
        try
        {
            await LoadSpoolAsync(_stop.Token);
            foreach (var batch in _pending.Values.OrderBy(x => x.ObservedAt).Chunk(_config.MaxBatchSize)) await PersistAsync(batch, _stop.Token);
            _replayReady.TrySetResult();
            while (await _channel.Reader.WaitToReadAsync(_stop.Token))
            {
                var batch = new List<PlayerSessionSignal>(_config.MaxBatchSize);
                var deadline = DateTimeOffset.UtcNow.AddMilliseconds(250);
                while (batch.Count < _config.MaxBatchSize)
                {
                    while (batch.Count < _config.MaxBatchSize && _channel.Reader.TryRead(out var signal))
                    {
                        await AppendAsync(signal, _stop.Token);
                        batch.Add(signal);
                    }
                    if (batch.Count >= _config.MaxBatchSize || _channel.Reader.Completion.IsCompleted) break;
                    var remaining = deadline - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;
                    await Task.WhenAny(_channel.Reader.WaitToReadAsync(_stop.Token).AsTask(), Task.Delay(remaining, _stop.Token));
                }
                if (batch.Count > 0) await PersistAsync(batch, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { _replayReady.TrySetCanceled(_stop.Token); }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastFailure, $"{ex.GetType().Name}: {ex.Message}");
            _replayReady.TrySetException(ex);
            _error("Player session worker stopped unexpectedly.", ex);
        }
    }

    private async Task PersistAsync(IReadOnlyCollection<PlayerSessionSignal> signals, CancellationToken token)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= _config.RetryMaxAttempts; attempt++)
        {
            try
            {
                await _repository.ProcessBatchAsync(signals, token);
                await AcknowledgeAsync(signals.Select(x => x.EventId), token);
                Interlocked.Add(ref _persisted, signals.Count);
                return;
            }
            catch (Exception ex) when (attempt < _config.RetryMaxAttempts)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, _config.RetryInitialDelaySeconds * Math.Pow(2, attempt - 1))), token);
            }
            catch (Exception ex) { last = ex; break; }
        }
        _error($"{signals.Count} player session events remain in the durable spool after retries.", last);
    }

    private async Task LoadSpoolAsync(CancellationToken token)
    {
        if (!_config.PersistPendingEventsToDisk || !File.Exists(_spoolPath)) return;
        var invalid = 0;
        foreach (var line in await File.ReadAllLinesAsync(_spoolPath, token))
        {
            try { var item = JsonSerializer.Deserialize<PlayerSessionSignal>(line, JsonOptions); if (item is not null) _pending[item.EventId] = item; }
            catch (JsonException) { invalid++; }
        }
        if (invalid > 0) _error($"Ignored {invalid} corrupt player-session spool line(s); no duration was guessed.", null);
    }

    private async Task AppendAsync(PlayerSessionSignal signal, CancellationToken token)
    {
        if (!_config.PersistPendingEventsToDisk) return;
        await _spoolGate.WaitAsync(token);
        try
        {
            if (_pending.ContainsKey(signal.EventId)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_spoolPath)!);
            await File.AppendAllTextAsync(_spoolPath, JsonSerializer.Serialize(signal, JsonOptions) + Environment.NewLine, token);
            _pending[signal.EventId] = signal;
        }
        finally { _spoolGate.Release(); }
    }

    private async Task AcknowledgeAsync(IEnumerable<Guid> ids, CancellationToken token)
    {
        if (!_config.PersistPendingEventsToDisk) return;
        await _spoolGate.WaitAsync(token);
        try
        {
            foreach (var id in ids) _pending.Remove(id);
            Directory.CreateDirectory(Path.GetDirectoryName(_spoolPath)!);
            var temporary = _spoolPath + ".tmp";
            await File.WriteAllLinesAsync(temporary, _pending.Values.OrderBy(x => x.ObservedAt).Select(x => JsonSerializer.Serialize(x, JsonOptions)), token);
            File.Move(temporary, _spoolPath, true);
        }
        finally { _spoolGate.Release(); }
    }
}
