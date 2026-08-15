using System.Collections.Concurrent;
using RobloxPiano.Core.Library;

namespace RobloxPiano.Core.Services;

public class MetadataCache
{
    private readonly ConcurrentDictionary<string, object> _cache = new();

    public void Set<T>(string key, T value) where T : class
    {
        _cache[key] = value;
    }

    public T? Get<T>(string key) where T : class
    {
        return _cache.TryGetValue(key, out var val) ? val as T : null;
    }

    public void Remove(string key)
    {
        _cache.TryRemove(key, out _);
    }

    public void InvalidateAll()
    {
        _cache.Clear();
    }
}
