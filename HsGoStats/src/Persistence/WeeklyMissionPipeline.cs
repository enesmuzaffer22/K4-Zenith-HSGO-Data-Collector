using System.Text.Json;
using System.Threading.Channels;
using HsGoStats.Configuration;
using HsGoStats.Domain;

namespace HsGoStats.Persistence;

public sealed class WeeklyMissionPipeline
{
    private readonly Channel<MissionSignal> _channel;
    private readonly WeeklyMissionRepository _repository;
    private readonly PersistenceConfig _config;
    private readonly string _spoolPath;
    private readonly Action<string, Exception?> _error;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _spoolGate = new(1, 1);
    private readonly Dictionary<Guid, MissionSignal> _pending = [];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private Task? _worker;

    public WeeklyMissionPipeline(WeeklyMissionRepository repository, PersistenceConfig config, string spoolPath, Action<string, Exception?> error)
    {
        _repository = repository; _config = config; _spoolPath = spoolPath; _error = error;
        _channel = Channel.CreateBounded<MissionSignal>(new BoundedChannelOptions(config.MaxQueuedEvents)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    }

    public void Start() => _worker = Task.Run(RunAsync);
    public bool TryEnqueue(MissionSignal signal) => _channel.Writer.TryWrite(signal);

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
            while (await _channel.Reader.WaitToReadAsync(_stop.Token))
            {
                var batch = new List<MissionSignal>(_config.MaxBatchSize);
                var deadline = DateTimeOffset.UtcNow.AddMilliseconds(250);
                while (batch.Count < _config.MaxBatchSize)
                {
                    while (batch.Count < _config.MaxBatchSize && _channel.Reader.TryRead(out var signal))
                    {
                        await AppendAsync(signal, _stop.Token);
                        batch.Add(signal);
                    }
                    if (batch.Count >= _config.MaxBatchSize || DateTimeOffset.UtcNow >= deadline || _channel.Reader.Completion.IsCompleted) break;
                    await Task.WhenAny(_channel.Reader.WaitToReadAsync(_stop.Token).AsTask(), Task.Delay(deadline - DateTimeOffset.UtcNow, _stop.Token));
                }
                if (batch.Count > 0) await PersistAsync(batch, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { _error("Weekly mission worker stopped unexpectedly.", ex); }
    }

    private async Task PersistAsync(IReadOnlyCollection<MissionSignal> signals, CancellationToken token)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= _config.RetryMaxAttempts; attempt++)
        {
            try { await _repository.ProcessBatchAsync(signals, token); await AcknowledgeAsync(signals.Select(x => x.EventId), token); return; }
            catch (Exception ex) when (attempt < _config.RetryMaxAttempts)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, _config.RetryInitialDelaySeconds * Math.Pow(2, attempt - 1))), token);
            }
            catch (Exception ex) { last = ex; break; }
        }
        _error($"{signals.Count} weekly mission events remain in the durable spool after retries.", last);
    }

    private async Task LoadSpoolAsync(CancellationToken token)
    {
        if (!_config.PersistPendingEventsToDisk || !File.Exists(_spoolPath)) return;
        var invalid = 0;
        foreach (var line in await File.ReadAllLinesAsync(_spoolPath, token))
        {
            try { var item = JsonSerializer.Deserialize<MissionSignal>(line, JsonOptions); if (item is not null) _pending[item.EventId] = item; }
            catch (JsonException) { invalid++; }
        }
        if (invalid > 0) _error($"Ignored {invalid} corrupt weekly mission spool line(s); no progress was guessed.", null);
    }

    private async Task AppendAsync(MissionSignal signal, CancellationToken token)
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
