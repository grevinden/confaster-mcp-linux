using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConfasterMCP.Util;

namespace ConfasterMCP.Indexing;

internal sealed class FileScanner
{
	private readonly Database _db;

	private readonly FileCache _fileCache;

	internal ConfigRoots Roots { get; }

	internal FileScanner(Database db, ConfigRoots roots, FileCache fileCache)
	{
		_db = db;
		Roots = roots;
		_fileCache = fileCache;
	}

	internal string? ResolveFullPath(string key)
	{
		return Roots.ResolveKey(key);
	}

	internal string? LabelOf(string key)
	{
		return Roots.LabelOf(key);
	}

	internal List<SearchResult> Search(string query, int limit = 20, bool isRegex = false, string? scope = null, CancellationToken ct = default(CancellationToken), ConfigRoot? configuration = null, bool exhaustive = false)
	{
		if (string.IsNullOrEmpty(query))
		{
			return new List<SearchResult>();
		}
		limit = Math.Clamp(limit, 1, 100);
		List<SearchResult> list = new List<SearchResult>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		bool flag = configuration == null && Roots.HasExtensions;
		if (!isRegex && scope == null)
		{
			list.AddRange(SymbolGuidedSearch(query, limit, ct, configuration, hashSet, exhaustive, out var callersFound));
			if (list.Count >= limit || (list.Count > 0 && (!exhaustive || callersFound)))
			{
				if (exhaustive && flag && !list.Any((SearchResult r) => ConfigRoots.IsExtensionKey(r.FilePath)))
				{
					list.AddRange(FullScan(query, limit, isRegex, scope, ct, configuration, hashSet, extensionsOnly: true));
				}
				return Pick(list, limit);
			}
		}
		list.AddRange(FullScan(query, limit - list.Count, isRegex, scope, ct, configuration, hashSet));
		return Pick(list, limit);
	}

	private static IEnumerable<SearchResult> Order(IEnumerable<SearchResult> results)
	{
		return results.OrderBy((SearchResult r) => ConfigRoots.IsExtensionKey(r.FilePath) ? 1 : 0).ThenBy<SearchResult, string>((SearchResult r) => r.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy((SearchResult r) => r.LineNumber);
	}

	private static List<SearchResult> Pick(List<SearchResult> results, int limit)
	{
		List<SearchResult> list = Order(results).ToList();
		if (list.Count <= limit)
		{
			return list;
		}
		List<SearchResult> list2 = list.Where((SearchResult r) => !ConfigRoots.IsExtensionKey(r.FilePath)).ToList();
		List<SearchResult> list3 = list.Where((SearchResult r) => ConfigRoots.IsExtensionKey(r.FilePath)).ToList();
		if (list2.Count == 0 || list3.Count == 0)
		{
			return list.Take(limit).ToList();
		}
		int num = Math.Min(list3.Count, Math.Max(1, limit / 4));
		int num2 = Math.Min(list2.Count, limit - num);
		int count = Math.Min(list3.Count, limit - num2);
		return list2.Take(num2).Concat(list3.Take(count)).ToList();
	}

	private List<SearchResult> SymbolGuidedSearch(string query, int limit, CancellationToken ct, ConfigRoot? configuration, HashSet<string> scanned, bool includeCallers, out bool callersFound)
	{
		List<string> list = new List<string>();
		string text = query.ToLowerInvariant();
		callersFound = false;
		string text2 = " AND " + ((configuration != null) ? ConfigRoots.InRootSql("file_path", configuration) : Roots.KnownKeysSql("file_path"));
		using (Database.OwnedReader ownedReader = _db.Query("SELECT DISTINCT file_path FROM symbols WHERE name_lower LIKE @q" + text2 + " LIMIT 100", ("@q", "%" + text + "%")))
		{
			while (ownedReader.Read())
			{
				list.Add(ownedReader.GetString(0));
			}
		}
		if (configuration == null && Roots.HasExtensions)
		{
			using Database.OwnedReader ownedReader2 = _db.Query("SELECT DISTINCT file_path FROM symbols WHERE file_path >= '@' AND file_path < 'A' AND name_lower LIKE @q AND " + Roots.KnownKeysSql("file_path") + " LIMIT 100", ("@q", "%" + text + "%"));
			HashSet<string> hashSet = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
			while (ownedReader2.Read())
			{
				string item = ownedReader2.GetString(0);
				if (hashSet.Add(item))
				{
					list.Add(item);
				}
			}
		}
		if (includeCallers)
		{
			callersFound = AddCallerFiles(query, configuration, list);
		}
		foreach (string item2 in list)
		{
			scanned.Add(item2);
		}
		if (list.Count == 0)
		{
			return new List<SearchResult>();
		}
		List<SearchResult> results = new List<SearchResult>();
		object lockObj = new object();
		Parallel.ForEach(list, new ParallelOptions
		{
			CancellationToken = ct,
			MaxDegreeOfParallelism = Environment.ProcessorCount
		}, delegate(string filePath)
		{
			if (!ct.IsCancellationRequested)
			{
				string text3 = ResolveFullPath(filePath);
				if (text3 != null)
				{
					List<SearchResult> collection = SearchInFile(text3, filePath, query, isRegex: false);
					lock (lockObj)
					{
						results.AddRange(collection);
					}
				}
			}
		});
		return results;
	}

	private bool AddCallerFiles(string query, ConfigRoot? configuration, List<string> files)
	{
		string name = query.Trim();
		int num = name.LastIndexOf('.');
		if (num >= 0)
		{
			name = name.Substring(num + 1);
		}
		if (name.Length == 0 || !name.All((char c) => char.IsLetterOrDigit(c) || c == '_'))
		{
			return false;
		}
		HashSet<string> known = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
		bool found = false;
		string text = " AND " + Roots.KnownKeysSql("s.file_path");
		Collect((configuration != null) ? (" AND " + ConfigRoots.InRootSql("s.file_path", configuration)) : text);
		if (configuration == null && Roots.HasExtensions)
		{
			Collect(" AND s.file_path >= '@' AND s.file_path < 'A'" + text);
		}
		return found;
		void Collect(string extraCond)
		{
			using Database.OwnedReader ownedReader = _db.Query("SELECT DISTINCT s.file_path FROM calls c JOIN symbols s ON s.id = c.caller_id\n                       WHERE c.callee_name_lower = @n" + extraCond + " LIMIT 500", ("@n", name.ToLowerInvariant()));
			while (ownedReader.Read())
			{
				found = true;
				string item = ownedReader.GetString(0);
				if (known.Add(item))
				{
					files.Add(item);
				}
			}
		}
	}

	private List<SearchResult> FullScan(string query, int limit, bool isRegex, string? scope, CancellationToken ct, ConfigRoot? configuration, HashSet<string> alreadyScanned, bool extensionsOnly = false)
	{
		DateTime dateTime = DateTime.UtcNow.AddSeconds(8.0);
		List<SearchResult> list = new List<SearchResult>();
		List<string> list2 = (from f in GetFilesForScan(scope, configuration)
			where !alreadyScanned.Contains(f)
			where !extensionsOnly || ConfigRoots.IsExtensionKey(f)
			select f).ToList();
		if (configuration == null && Roots.HasExtensions)
		{
			List<string> list3 = list2.Where(ConfigRoots.IsExtensionKey).ToList();
			DateTime deadline = (extensionsOnly ? dateTime : DateTime.UtcNow.AddSeconds(3.0));
			if (list3.Count > 0)
			{
				list.AddRange(FullScanFiles(list3, query, limit, isRegex, ct, deadline));
			}
			if (extensionsOnly)
			{
				return list;
			}
			list2 = list2.Where((string f) => !ConfigRoots.IsExtensionKey(f)).ToList();
		}
		list.AddRange(FullScanFiles(list2, query, limit, isRegex, ct, dateTime));
		return list;
	}

	private List<SearchResult> FullScanFiles(List<string> allFiles, string query, int limit, bool isRegex, CancellationToken ct, DateTime deadline)
	{
		List<SearchResult> results = new List<SearchResult>();
		object lockObj = new object();
		int found = 0;
		List<string> files = allFiles.Where((string f) => f.EndsWith(".bsl", StringComparison.OrdinalIgnoreCase)).ToList();
		List<string> files2 = allFiles.Where((string f) => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList();
		Regex regex = null;
		if (isRegex)
		{
			try
			{
				regex = new Regex(query, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2.0));
			}
			catch
			{
				return new List<SearchResult>();
			}
		}
		ScanBatch(files);
		if (Volatile.Read(in found) < limit && DateTime.UtcNow < deadline)
		{
			ScanBatch(files2);
		}
		return results;
		void ScanBatch(List<string> source)
		{
			Parallel.ForEach(source, new ParallelOptions
			{
				CancellationToken = ct,
				MaxDegreeOfParallelism = Environment.ProcessorCount
			}, delegate(string filePath)
			{
				if (!ct.IsCancellationRequested && !(DateTime.UtcNow > deadline) && Volatile.Read(in found) < limit)
				{
					string text = ResolveFullPath(filePath);
					if (text != null)
					{
						List<SearchResult> list = SearchInFile(text, filePath, query, isRegex, regex);
						if (list.Count > 0)
						{
							lock (lockObj)
							{
								results.AddRange(list);
								Interlocked.Add(ref found, list.Count);
							}
						}
					}
				}
			});
		}
	}

	private List<SearchResult> SearchInFile(string fullPath, string key, string query, bool isRegex, Regex? regex = null)
	{
		List<SearchResult> list = new List<SearchResult>();
		string[] lines;
		try
		{
			lines = _fileCache.GetLines(fullPath);
		}
		catch
		{
			return list;
		}
		for (int i = 0; i < lines.Length; i++)
		{
			if ((!isRegex || regex == null) ? lines[i].Contains(query, StringComparison.OrdinalIgnoreCase) : regex.IsMatch(lines[i]))
			{
				int num = Math.Max(0, i - 2);
				int num2 = Math.Min(lines.Length - 1, i + 2);
				List<string> list2 = new List<string>();
				for (int j = num; j <= num2; j++)
				{
					list2.Add($"{j + 1}: {lines[j]}");
				}
				list.Add(new SearchResult
				{
					FilePath = key,
					DisplayPath = ConfigRoots.DisplayPath(key),
					AbsolutePath = fullPath,
					Configuration = LabelOf(key),
					LineNumber = i + 1,
					LineText = lines[i].Trim(),
					Context = string.Join("\n", list2)
				});
				if (list.Count >= 10)
				{
					break;
				}
			}
		}
		return list;
	}

	private bool InRoot(string key, ConfigRoot root)
	{
		return Roots.SplitKey(key).root == root;
	}

	private List<string> GetFilesForScan(string? scope, ConfigRoot? configuration)
	{
		List<string> list = new List<string>();
		if (scope != null)
		{
			string text = scope.Replace('\\', '/').TrimStart('/');
			string text2 = ((configuration != null) ? Roots.RootLike("path", "@s", configuration) : ConfigRoots.AnyRootLike("path", "@s"));
			using Database.OwnedReader ownedReader = _db.Query("SELECT path FROM indexed_files WHERE " + text2, ("@s", text + "%"));
			while (ownedReader.Read())
			{
				list.Add(ownedReader.GetString(0));
			}
		}
		else
		{
			using Database.OwnedReader ownedReader2 = _db.Query("SELECT path FROM indexed_files");
			while (ownedReader2.Read())
			{
				string text3 = ownedReader2.GetString(0);
				if (configuration == null || InRoot(text3, configuration))
				{
					list.Add(text3);
				}
			}
		}
		return list;
	}
}
