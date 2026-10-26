using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EzClassify;

internal sealed class ContextResponseCache
{
    private sealed class Entry
    {
        internal readonly string Key;
        internal readonly CancellationTokenSource Source = new();
        internal readonly TaskCompletionSource<object> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal LinkedListNode<Entry>? Node;
        internal DateTimeOffset Expires;
        internal int Waiters;

        internal Entry(string key) => Key = key;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _order = new();
    private const int Limit = 500;

    /// <summary>
    /// The clock entry lifetimes are measured on. Only the cache reads it, so a
    /// test moves it to age entries without waiting; transport timers such as
    /// the request timeout keep running on the real clock.
    /// </summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Callers currently waiting on a request that has not settled, across all
    /// entries. A test reads it to know every caller of a concurrent step has
    /// joined the in-flight entry rather than assuming it.
    /// </summary>
    internal int PendingCallers
    {
        get
        {
            lock (_gate) return _entries.Values.Where(entry => !entry.Completion.Task.IsCompleted).Sum(entry => entry.Waiters);
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
        }
    }

    internal async Task<T> GetAsync<T>(
        string key,
        TimeSpan ttl,
        bool pinned,
        Func<CancellationToken, Task<T>> call,
        CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        Entry entry;
        var started = false;
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var found)
                && found.Completion.Task.Status == TaskStatus.RanToCompletion
                && !pinned
                && Clock() >= found.Expires)
            {
                Remove(found);
                found = null;
            }
            if (found is null)
            {
                entry = new Entry(key);
                entry.Node = _order.AddFirst(entry);
                _entries[key] = entry;
                started = true;
                while (_entries.Count > Limit)
                    Remove(_order.Last?.Value ?? throw new InvalidOperationException("Cache order is empty."));
            }
            else
            {
                entry = found;
                _order.Remove(entry.Node ?? throw new InvalidOperationException("Cache entry has no order node."));
                entry.Node = _order.AddFirst(entry);
            }
            if (!entry.Completion.Task.IsCompleted)
                entry.Waiters++;
        }
        if (started)
            _ = RunAsync(entry, ttl, call);
        if (entry.Completion.Task.IsCompleted)
            return (T)await entry.Completion.Task.ConfigureAwait(false);

        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (caller.Register(() => canceled.TrySetResult(true)))
        {
            var completed = await Task.WhenAny(entry.Completion.Task, canceled.Task).ConfigureAwait(false);
            if (completed == canceled.Task)
            {
                var abort = false;
                lock (_gate)
                {
                    if (!entry.Completion.Task.IsCompleted)
                    {
                        entry.Waiters--;
                        if (entry.Waiters == 0)
                        {
                            Remove(entry);
                            abort = true;
                        }
                    }
                }
                if (abort)
                {
                    try { entry.Source.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
                caller.ThrowIfCancellationRequested();
            }
        }
        return (T)await entry.Completion.Task.ConfigureAwait(false);
    }

    private async Task RunAsync<T>(Entry entry, TimeSpan ttl, Func<CancellationToken, Task<T>> call)
    {
        try
        {
            var result = await call(entry.Source.Token).ConfigureAwait(false);
            lock (_gate)
            {
                entry.Expires = Clock().Add(ttl);
                entry.Completion.TrySetResult(result is null
                    ? throw new InvalidOperationException("A cached response cannot be null.")
                    : result);
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                Remove(entry);
                if (entry.Waiters == 0) entry.Completion.TrySetCanceled();
                else entry.Completion.TrySetException(error);
            }
        }
        finally
        {
            entry.Source.Dispose();
        }
    }

    private void Remove(Entry entry)
    {
        if (!_entries.TryGetValue(entry.Key, out var current) || current != entry)
            return;
        _entries.Remove(entry.Key);
        _order.Remove(entry.Node ?? throw new InvalidOperationException("Cache entry has no order node."));
    }

    internal static string Canonicalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, document.RootElement);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
