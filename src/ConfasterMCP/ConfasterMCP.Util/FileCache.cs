using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ConfasterMCP.Util;

internal sealed class FileCache
{
	private sealed class CacheEntry
	{
		internal string[] Lines = Array.Empty<string>();

		internal long LastAccess;
	}

	private readonly int _maxSize;

	private readonly ConcurrentDictionary<string, CacheEntry> _cache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

	private long _accessCounter;

	internal FileCache(int maxSize = 500)
	{
		_maxSize = maxSize;
	}

	internal string[] GetLines(string fullPath)
	{
		if (_cache.TryGetValue(fullPath, out CacheEntry value))
		{
			value.LastAccess = Interlocked.Increment(ref _accessCounter);
			return value.Lines;
		}
		string[] array = EncodingHelper.SplitLines(EncodingHelper.ReadFileText(fullPath));
		CacheEntry value2 = new CacheEntry
		{
			Lines = array,
			LastAccess = Interlocked.Increment(ref _accessCounter)
		};
		_cache[fullPath] = value2;
		if (_cache.Count > _maxSize)
		{
			Evict();
		}
		return array;
	}

	internal string GetText(string fullPath)
	{
		string[] lines = GetLines(fullPath);
		return string.Join("\n", lines);
	}

	internal void Invalidate(string fullPath)
	{
		_cache.TryRemove(fullPath, out CacheEntry _);
	}

	internal void Clear()
	{
		_cache.Clear();
	}

	private void Evict()
	{
		int count = _maxSize / 10;
		foreach (string item in (from kvp in _cache.OrderBy<KeyValuePair<string, CacheEntry>, long>((KeyValuePair<string, CacheEntry> kvp) => kvp.Value.LastAccess).Take(count)
			select kvp.Key).ToList())
		{
			_cache.TryRemove(item, out CacheEntry _);
		}
	}
}
