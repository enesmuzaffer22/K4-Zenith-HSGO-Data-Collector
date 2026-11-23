using System.Text.Json;
using HsGoStats.Domain;

namespace HsGoStats.Persistence;

public sealed class DurableSpool(string path, bool enabled, Action<string>? warning = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, NormalizedStatEvent> _pending = [];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<NormalizedStatEvent>> LoadAsync(CancellationToken token)
    {
        if (!enabled || !File.Exists(path)) return [];
        await _gate.WaitAsync(token);
        try
        {
            var invalidLines = 0;
            foreach (var line in await File.ReadAllLinesAsync(path, token))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var item = JsonSerializer.Deserialize<NormalizedStatEvent>(line, JsonOptions);
                    if (item is not null) _pending[item.EventId] = item;
                }
                catch (JsonException) { invalidLines++; }
            }
            if (invalidLines > 0) warning?.Invoke($"Ignored {invalidLines} corrupt line(s) in the durable statistics spool; no values were guessed.");
            return _pending.Values.OrderBy(x => x.ObservedAt).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task AppendAsync(NormalizedStatEvent item, CancellationToken token)
    {
        if (!enabled) return;
        await _gate.WaitAsync(token);
        try
        {
            if (_pending.ContainsKey(item.EventId)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(item, JsonOptions) + Environment.NewLine, token);
            _pending[item.EventId] = item;
        }
        finally { _gate.Release(); }
    }

    public async Task AcknowledgeAsync(IEnumerable<Guid> ids, CancellationToken token)
    {
        if (!enabled) return;
        await _gate.WaitAsync(token);
        try
        {
            foreach (var id in ids) _pending.Remove(id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            var lines = _pending.Values.OrderBy(x => x.ObservedAt).Select(x => JsonSerializer.Serialize(x, JsonOptions));
            await File.WriteAllLinesAsync(temporary, lines, token);
            File.Move(temporary, path, true);
        }
        finally { _gate.Release(); }
    }
}
