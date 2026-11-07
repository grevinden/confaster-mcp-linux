using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ConfasterMCP.Semantic;
using ConfasterMCP.TreeSitter;
using ConfasterMCP.Util;

namespace ConfasterMCP.Indexing;

internal sealed class IndexManager
{
	private readonly Database _db;

	private readonly string _exportRoot;

	private readonly FileCache _fileCache;

	private readonly List<string> _extensionRoots;

	private const int CurrentDomainAliasesVersion = 2;

	private const string DomainAliasesVersionMetaKey = "domain_aliases_version";

	private const string DomainAliasesHashMetaKey = "domain_aliases_hash";

	private const int CurrentMetadataParserVersion = 2;

	private const string MetadataParserVersionMetaKey = "metadata_parser_version";

	internal IndexManager(Database db, string exportRoot, FileCache fileCache, List<string>? extensionRoots = null)
	{
		_db = db;
		_exportRoot = exportRoot;
		_fileCache = fileCache;
		_extensionRoots = extensionRoots ?? new List<string>();
	}

	internal (int added, int updated, int deleted) IncrementalSync()
	{
		(int added, int updated, int deleted) result = IncrementalSyncCore();
		TouchSyncMarker();
		return result;
	}

	private void TouchSyncMarker()
	{
		try
		{
			string text = Path.Combine(_exportRoot, ".index");
			Directory.CreateDirectory(text);
			File.WriteAllText(Path.Combine(text, "last_sync.txt"), DateTime.UtcNow.ToString("O"));
		}
		catch
		{
		}
	}

	private (int added, int updated, int deleted) IncrementalSyncCore()
	{
		Stopwatch sw = Stopwatch.StartNew();
		Dictionary<string, (long mtime, long size)> currentFiles = ScanFileSystem();
		Dictionary<string, (long, long)> indexedFiles = _db.GetIndexedFiles();
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		List<string> deletedFiles = new List<string>();
		foreach (KeyValuePair<string, (long, long)> item3 in currentFiles)
		{
			item3.Deconstruct(out var key, out var value);
			(long, long) tuple = value;
			string text = key;
			var (num, num2) = tuple;
			if (!indexedFiles.TryGetValue(text, out var value2))
			{
				list.Add(text);
			}
			else if (value2.Item1 != num || value2.Item2 != num2)
			{
				list2.Add(text);
			}
		}
		foreach (string key2 in indexedFiles.Keys)
		{
			if (!currentFiles.ContainsKey(key2))
			{
				deletedFiles.Add(key2);
			}
		}
		if (list.Count == 0 && list2.Count == 0 && deletedFiles.Count == 0)
		{
			bool flag = _db.QueryScalar("SELECT COUNT(*) FROM objects") == 0L || _db.GetMeta("metadata_parser_version") != 2.ToString(CultureInfo.InvariantCulture);
			string text2 = 2.ToString(CultureInfo.InvariantCulture);
			bool flag2 = _db.GetMeta("domain_aliases_version") != text2 || _db.GetMeta("domain_aliases_hash") != ComputeDictionaryHash();
			if (flag || flag2)
			{
				lock (_db.WriteLock)
				{
					_db.BeginTransaction();
					try
					{
						if (flag)
						{
							RebuildMetadata();
						}
						if (flag2)
						{
							EnsureDomainAliasesUpToDate();
						}
						_db.Commit();
					}
					catch
					{
						_db.Rollback();
						throw;
					}
				}
			}
			sw.Stop();
			return (added: 0, updated: 0, deleted: 0);
		}
		int parsedBslFiles = 0;
		int totalSymbols = 0;
		int totalCalls = 0;
		int totalRegions = 0;
		int num3 = 0;
		List<string> list3 = new List<string>(list.Count + list2.Count);
		list3.AddRange(list);
		list3.AddRange(list2);
		List<string> list4 = new List<string>();
		foreach (string item4 in list3)
		{
			if (item4.EndsWith(".bsl", StringComparison.OrdinalIgnoreCase))
			{
				list4.Add(item4);
			}
		}
		HashSet<string> changedSet = new HashSet<string>(list2, StringComparer.OrdinalIgnoreCase);
		List<string> nonBslToIndex = new List<string>();
		foreach (string item5 in list3)
		{
			if (!item5.EndsWith(".bsl", StringComparison.OrdinalIgnoreCase))
			{
				nonBslToIndex.Add(item5);
			}
		}
		Console.Error.WriteLine($"MCP_LOG: sync start: {list.Count} new, {list2.Count} changed, {deletedFiles.Count} deleted ({list4.Count} .bsl to parse)");
		Console.Error.Flush();
		lock (_db.WriteLock)
		{
			InTx(delegate
			{
				foreach (string item6 in deletedFiles)
				{
					_db.DeleteSymbolsByFile(item6);
					_db.DeleteRegionsByFile(item6);
					_db.DeleteIndexedFile(item6);
					string text3 = ResolveFullPath(item6);
					if (text3 != null)
					{
						_fileCache.Invalidate(text3);
					}
				}
			});
			for (int num4 = 0; num4 < nonBslToIndex.Count; num4 += 500)
			{
				int end = Math.Min(num4 + 500, nonBslToIndex.Count);
				int from = num4;
				InTx(delegate
				{
					for (int i = from; i < end; i++)
					{
						string text3 = nonBslToIndex[i];
						string text4 = ResolveFullPath(text3);
						if (text4 != null)
						{
							var (mtime, size) = currentFiles[text3];
							_db.UpsertIndexedFile(text3, mtime, size);
							_fileCache.Invalidate(text4);
						}
					}
				});
			}
			int num5 = 0;
			for (int num6 = 0; num6 < list4.Count; num6 += 500)
			{
				int num7 = Math.Min(num6 + 500, list4.Count);
				List<string> batchPaths = list4.GetRange(num6, num7 - num6);
				ConcurrentQueue<ParsedBslFile> parsedBatch = ParseBatchParallel(batchPaths);
				num3 += batchPaths.Count - parsedBatch.Count;
				InTx(delegate
				{
					foreach (string item7 in batchPaths)
					{
						if (changedSet.Contains(item7))
						{
							_db.DeleteSymbolsByFile(item7);
							_db.DeleteRegionsByFile(item7);
							string text3 = ResolveFullPath(item7);
							if (text3 != null)
							{
								_fileCache.Invalidate(text3);
							}
						}
					}
					foreach (ParsedBslFile item8 in parsedBatch)
					{
						foreach (ParsedSymbol symbol in item8.Symbols)
						{
							SymbolInfo info = symbol.Info;
							long num8 = _db.InsertSymbol(item8.Path, info.Name, info.Kind, info.StartLine, info.EndLine, info.IsExport, info.IsAsync, info.Directive);
							_db.InsertSymbolTerms(num8, symbol.NameTokens, info.CommentHead ?? "", symbol.ParamTokens);
							foreach (var (calleeName, lineNumber) in info.Calls)
							{
								_db.InsertCall(num8, calleeName, lineNumber);
							}
							totalCalls += info.Calls.Count;
						}
						totalSymbols += item8.Symbols.Count;
						foreach (RegionInfo region in item8.Regions)
						{
							_db.InsertRegion(item8.Path, region.Name, region.StartLine, region.EndLine);
						}
						totalRegions += item8.Regions.Count;
						int num9 = parsedBslFiles;
						parsedBslFiles = num9 + 1;
					}
					foreach (string item9 in batchPaths)
					{
						if (ResolveFullPath(item9) != null)
						{
							var (mtime, size) = currentFiles[item9];
							_db.UpsertIndexedFile(item9, mtime, size);
						}
					}
				});
				num5 += batchPaths.Count;
				Console.Error.WriteLine($"MCP_LOG: indexing... {num5}/{list4.Count} .bsl, {totalSymbols} symbols so far");
				Console.Error.Flush();
			}
			PostStep("metadata", RebuildMetadata);
			PostStep("symbol_weights", delegate
			{
				_db.RebuildSymbolWeights();
			});
			PostStep("domain_aliases", delegate
			{
				EnsureDomainAliasesUpToDate();
			});
			sw.Stop();
			PostStep("meta-counters", delegate
			{
				_db.SetMeta("last_sync_at", DateTime.UtcNow.ToString("O"));
				_db.SetMeta("sync_duration_ms", sw.ElapsedMilliseconds.ToString());
				_db.SetMeta("symbol_count", _db.QueryScalar("SELECT COUNT(*) FROM symbols").ToString());
				_db.SetMeta("file_count", _db.QueryScalar("SELECT COUNT(*) FROM indexed_files").ToString());
			});
		}
		Console.Error.WriteLine($"MCP_LOG: sync complete: {list.Count} new, {list2.Count} changed, {deletedFiles.Count} deleted | parsed {parsedBslFiles} .bsl files ({totalSymbols} symbols, {totalCalls} calls, {totalRegions} regions, {num3} errors) in {sw.ElapsedMilliseconds} ms");
		Console.Error.Flush();
		return (added: list.Count, updated: list2.Count, deleted: deletedFiles.Count);
		void InTx(Action work)
		{
			_db.BeginTransaction();
			try
			{
				work();
				_db.Commit();
			}
			catch
			{
				_db.Rollback();
				throw;
			}
		}
		ConcurrentQueue<ParsedBslFile> ParseBatchParallel(List<string> source)
		{
			ConcurrentQueue<ParsedBslFile> parsedBatch2 = new ConcurrentQueue<ParsedBslFile>();
			Parallel.ForEach(source, new ParallelOptions
			{
				MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
			}, () => BslAstParser.CreateParser(), delegate(string path, ParallelLoopState _, nint parser)
			{
				string text3 = ResolveFullPath(path);
				if (text3 == null)
				{
					return parser;
				}
				try
				{
					(byte[] utf8Bytes, string[] lines) tuple3 = EncodingHelper.ReadFileForParsing(text3);
					byte[] item = tuple3.utf8Bytes;
					string[] item2 = tuple3.lines;
					List<SymbolInfo> obj2 = ((parser != IntPtr.Zero) ? BslAstParser.ParseWithParser(parser, item, item2) : BslSymbolParser.Parse(item, item2));
					List<RegionInfo> regions = BslSymbolParser.ParseRegions(item2);
					List<ParsedSymbol> list5 = new List<ParsedSymbol>(obj2.Count);
					foreach (SymbolInfo item10 in obj2)
					{
						string nameTokens = string.Join(' ', Tokenizer.TokenizeIdentifier(item10.Name));
						string paramTokens = ((item10.Parameters != null) ? string.Join(' ', Tokenizer.TokenizeIdentifier(item10.Parameters)) : "");
						list5.Add(new ParsedSymbol
						{
							Info = item10,
							NameTokens = nameTokens,
							ParamTokens = paramTokens
						});
					}
					parsedBatch2.Enqueue(new ParsedBslFile
					{
						Path = path,
						Symbols = list5,
						Regions = regions
					});
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"MCP_LOG: parse failed on {path}: {ex.GetType().Name}: {ex.Message}, skipped");
					if (ex.InnerException != null)
					{
						Console.Error.WriteLine("MCP_LOG: inner: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
					}
					Console.Error.Flush();
					if (parser != IntPtr.Zero && ex is InvalidOperationException && ex.Message.Contains("native crash"))
					{
						parser = BslAstParser.CreateParser();
					}
				}
				return parser;
			}, delegate(nint parser)
			{
				BslAstParser.DestroyParser(parser);
			});
			return parsedBatch2;
		}
		void PostStep(string name, Action work)
		{
			try
			{
				InTx(work);
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"MCP_LOG: sync post-step '{name}' failed: {ex.GetType().Name}: {ex.Message} " + "— частичный индекс сохранён, шаг будет достроен при следующей синхронизации");
				Console.Error.Flush();
			}
		}
	}

	private void EnsureDomainAliasesUpToDate()
	{
		string meta = _db.GetMeta("domain_aliases_version");
		string meta2 = _db.GetMeta("domain_aliases_hash");
		string text = 2.ToString(CultureInfo.InvariantCulture);
		string text2 = ComputeDictionaryHash();
		if (meta == text && meta2 == text2)
		{
			return;
		}
		_db.Execute("DELETE FROM domain_aliases");
		int num = 0;
		foreach (DomainDictionary.DomainAlias entry in DomainDictionary.Entries)
		{
			if (!string.IsNullOrEmpty(entry.term) && !string.IsNullOrEmpty(entry.alias))
			{
				_db.InsertDomainAlias(entry.term.ToLowerInvariant(), entry.alias, entry.weight);
				num++;
			}
		}
		_db.SetMeta("domain_aliases_version", text);
		_db.SetMeta("domain_aliases_hash", text2);
		string value = ((meta != text && meta2 != text2) ? $"version {meta ?? "<none>"}→{text} + content hash {meta2 ?? "<none>"}→{text2}" : ((!(meta != text)) ? ("content hash " + (meta2 ?? "<none>") + "→" + text2) : ("version " + (meta ?? "<none>") + "→" + text)));
		Console.Error.WriteLine($"MCP_LOG: domain_aliases migrated ({value}, {num} entries)");
	}

	private static string ComputeDictionaryHash()
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (DomainDictionary.DomainAlias item in DomainDictionary.Entries.OrderBy<DomainDictionary.DomainAlias, string>((DomainDictionary.DomainAlias e) => e.term, StringComparer.Ordinal).ThenBy<DomainDictionary.DomainAlias, string>((DomainDictionary.DomainAlias e) => e.alias, StringComparer.Ordinal).ThenBy((DomainDictionary.DomainAlias e) => e.weight))
		{
			stringBuilder.Append(item.term).Append('|').Append(item.alias)
				.Append('|')
				.Append(item.weight.ToString("R", CultureInfo.InvariantCulture))
				.Append('\n');
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stringBuilder.ToString())), 0, 8);
	}

	private void RebuildMetadata()
	{
		_db.ClearObjects();
		InsertMetadataFromRoot(_exportRoot);
		foreach (string extensionRoot in _extensionRoots)
		{
			InsertMetadataFromRoot(extensionRoot);
		}
		_db.SetMeta("metadata_parser_version", 2.ToString(CultureInfo.InvariantCulture));
	}

	private void InsertMetadataFromRoot(string root)
	{
		if (!Directory.Exists(root))
		{
			return;
		}
		foreach (ObjectMetadata item in MetadataParser.ScanExportRoot(root))
		{
			_db.InsertObject(item.Fqn, item.TypeFolder, item.Name, item.ExportPath);
			foreach (string synonym in item.Synonyms)
			{
				_db.InsertSynonym(item.Fqn, synonym);
			}
			foreach (ObjectItemInfo item2 in item.Items)
			{
				string parentName = null;
				string typeInfo = null;
				if (item2.Kind == "ts_attribute")
				{
					parentName = item2.ExtraInfo;
				}
				else
				{
					typeInfo = item2.ExtraInfo;
				}
				_db.InsertObjectItem(item.Fqn, item2.Kind, item2.Name, parentName, typeInfo);
			}
		}
	}

	private string? ResolveFullPath(string relativePath)
	{
		string path = relativePath.Replace('/', Path.DirectorySeparatorChar);
		string text = Path.Combine(_exportRoot, path);
		if (File.Exists(text))
		{
			return text;
		}
		foreach (string extensionRoot in _extensionRoots)
		{
			string text2 = Path.Combine(extensionRoot, path);
			if (File.Exists(text2))
			{
				return text2;
			}
		}
		return null;
	}

	private Dictionary<string, (long mtime, long size)> ScanFileSystem()
	{
		Dictionary<string, (long, long)> result = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
		ScanRoot(_exportRoot, result);
		foreach (string extensionRoot in _extensionRoots)
		{
			ScanRoot(extensionRoot, result);
		}
		return result;
	}

	private static void ScanRoot(string root, Dictionary<string, (long mtime, long size)> result)
	{
		if (!Directory.Exists(root))
		{
			return;
		}
		foreach (string item in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
		{
			string extension = Path.GetExtension(item);
			if (extension.Equals(".bsl", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
			{
				string relativePath = PathHelper.GetRelativePath(item, root);
				if (!relativePath.StartsWith(".index", StringComparison.OrdinalIgnoreCase))
				{
					FileInfo fileInfo = new FileInfo(item);
					result[relativePath] = (fileInfo.LastWriteTimeUtc.Ticks, fileInfo.Length);
				}
			}
		}
	}
}
