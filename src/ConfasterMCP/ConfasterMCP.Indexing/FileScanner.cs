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

	private readonly string _exportRoot;

	private readonly FileCache _fileCache;

	private readonly PathResolver _pathResolver;

	internal FileScanner(Database db, string exportRoot, FileCache fileCache, List<string>? extensionRoots = null)
	{
		_db = db;
		_exportRoot = exportRoot;
		_fileCache = fileCache;
		_pathResolver = new PathResolver(exportRoot, extensionRoots);
	}

	internal string? ResolveFullPath(string relativePath)
	{
		return _pathResolver.Resolve(relativePath);
	}

	internal List<SearchResult> Search(string query, int limit = 20, bool isRegex = false, string? scope = null, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrEmpty(query))
		{
			return new List<SearchResult>();
		}
		limit = Math.Clamp(limit, 1, 100);
		if (!isRegex && scope == null)
		{
			List<SearchResult> list = SymbolGuidedSearch(query, limit, ct);
			if (list.Count > 0)
			{
				return list;
			}
		}
		return FullScan(query, limit, isRegex, scope, ct);
	}

	private List<SearchResult> SymbolGuidedSearch(string query, int limit, CancellationToken ct)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string text = query.ToLowerInvariant();
		using Database.OwnedReader ownedReader = _db.Query("SELECT DISTINCT file_path FROM symbols WHERE name_lower LIKE @q LIMIT 100", ("@q", "%" + text + "%"));
		while (ownedReader.Read())
		{
			hashSet.Add(ownedReader.GetString(0));
		}
		if (hashSet.Count == 0)
		{
			return new List<SearchResult>();
		}
		List<SearchResult> results = new List<SearchResult>();
		object lockObj = new object();
		Parallel.ForEach(hashSet, new ParallelOptions
		{
			CancellationToken = ct,
			MaxDegreeOfParallelism = Environment.ProcessorCount
		}, delegate(string filePath)
		{
			if (!ct.IsCancellationRequested)
			{
				string text2 = _pathResolver.Resolve(filePath);
				if (text2 != null)
				{
					List<SearchResult> collection = SearchInFile(text2, filePath, query, isRegex: false);
					lock (lockObj)
					{
						results.AddRange(collection);
					}
				}
			}
		});
		return (from r in results
			orderby r.FilePath, r.LineNumber
			select r).Take(limit).ToList();
	}

	private List<SearchResult> FullScan(string query, int limit, bool isRegex, string? scope, CancellationToken ct)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(8.0);
		List<SearchResult> results = new List<SearchResult>();
		object lockObj = new object();
		int found = 0;
		List<string> filesForScan = GetFilesForScan(scope);
		List<string> files = filesForScan.Where((string f) => f.EndsWith(".bsl", StringComparison.OrdinalIgnoreCase)).ToList();
		List<string> files2 = filesForScan.Where((string f) => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList();
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
		return (from r in results
			orderby r.FilePath, r.LineNumber
			select r).Take(limit).ToList();
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
					string text = _pathResolver.Resolve(filePath);
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

	private List<SearchResult> SearchInFile(string fullPath, string relativePath, string query, bool isRegex, Regex? regex = null)
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
					FilePath = relativePath,
					DisplayPath = PathHelper.ToDisplayPath(relativePath),
					AbsolutePath = fullPath,
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

	private List<string> GetFilesForScan(string? scope)
	{
		List<string> list = new List<string>();
		if (scope != null)
		{
			string text = scope.Replace('\\', '/');
			using Database.OwnedReader ownedReader = _db.Query("SELECT path FROM indexed_files WHERE path LIKE @s", ("@s", text + "%"));
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
				list.Add(ownedReader2.GetString(0));
			}
		}
		return list;
	}
}
