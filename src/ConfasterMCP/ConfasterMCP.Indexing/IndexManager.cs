using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConfasterMCP.Semantic;
using ConfasterMCP.TreeSitter;
using ConfasterMCP.Util;

namespace ConfasterMCP.Indexing;

internal sealed class IndexManager
{
	private sealed record MetadataPlan(bool Full, List<ConfigRoot> Roots, List<string> RemovedConfigs);

	private sealed class ScanEnumerator : FileSystemEnumerator<(string fullPath, long mtime, long size)>
	{
		private readonly HashSet<string> _skipDirs;

		internal int SkippedErrors { get; private set; }

		internal int LastError { get; private set; }

		internal ScanEnumerator(string root, EnumerationOptions options, HashSet<string> skipDirs)
			: base(root, options)
		{
			_skipDirs = skipDirs;
		}

		protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
		{
			if (MemoryExtensions.Equals(entry.FileName, ".index", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (_skipDirs.Count > 0 && _skipDirs.Contains(entry.ToFullPath().TrimEnd('\\', '/')))
			{
				return false;
			}
			return true;
		}

		protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
		{
			if (entry.IsDirectory)
			{
				return false;
			}
			ReadOnlySpan<char> extension = Path.GetExtension(entry.FileName);
			if (!MemoryExtensions.Equals(extension, ".bsl", StringComparison.OrdinalIgnoreCase))
			{
				return MemoryExtensions.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}

		protected override (string fullPath, long mtime, long size) TransformEntry(ref FileSystemEntry entry)
		{
			return (fullPath: entry.ToFullPath(), mtime: entry.LastWriteTimeUtc.UtcDateTime.Ticks, size: entry.Length);
		}

		protected override bool ContinueOnError(int error)
		{
			SkippedErrors++;
			LastError = error;
			return true;
		}
	}

	private readonly Database _db;

	private readonly ConfigRoots _roots;

	private readonly string _exportRoot;

	private readonly FileCache _fileCache;

	private readonly bool _ownsIndex;

	private readonly HashSet<ConfigRoot> _unreadableRoots = new HashSet<ConfigRoot>();

	private const string OwnerRootsMetaKey = "owner_roots";

	private HashSet<ConfigRoot> _activeRoots = new HashSet<ConfigRoot>();

	private const int CurrentDomainAliasesVersion = 2;

	private const string DomainAliasesVersionMetaKey = "domain_aliases_version";

	private const string DomainAliasesHashMetaKey = "domain_aliases_hash";

	private const int CurrentMetadataParserVersion = 3;

	private const string MetadataParserVersionMetaKey = "metadata_parser_version";

	private const string MetadataPendingRootsMetaKey = "metadata_pending_roots";

	private const string MetadataBrokenRootsMetaKey = "metadata_broken_roots";

	private static readonly TimeSpan BrokenMetadataRetryInterval = TimeSpan.FromHours(6.0);

	private const string MetadataRootsMetaKey = "metadata_roots";

	private bool KeepUnknownConfigs => _activeRoots.Any((ConfigRoot r) => r.IdUncertain);

	internal IndexManager(Database db, ConfigRoots roots, FileCache fileCache, bool ownsIndex = true)
	{
		_db = db;
		_roots = roots;
		_exportRoot = roots.IndexRoot;
		_fileCache = fileCache;
		_ownsIndex = ownsIndex;
	}

	internal (int added, int updated, int deleted) IncrementalSync()
	{
		using Mutex mutex = new Mutex(initiallyOwned: false, IndexMutexName());
		bool flag = false;
		try
		{
			try
			{
				flag = mutex.WaitOne(TimeSpan.FromSeconds(5.0));
			}
			catch (AbandonedMutexException)
			{
				flag = true;
			}
			if (!flag)
			{
				throw new IndexBusyException(_ownsIndex);
			}
			(int added, int updated, int deleted) result = IncrementalSyncCore();
			TouchSyncMarker();
			return result;
		}
		finally
		{
			if (flag)
			{
				mutex.ReleaseMutex();
			}
		}
	}

	private string IndexMutexName()
	{
		byte[] inArray = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(_exportRoot).ToUpperInvariant()));
		return "Local\\ConfasterMCP-index-" + Convert.ToHexString(inArray, 0, 12);
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

	private HashSet<ConfigRoot> ResolveActiveRoots()
	{
		if (_ownsIndex)
		{
			if (_db.GetMeta("owner_roots") != _roots.Signature)
			{
				lock (_db.WriteLock)
				{
					_db.SetMeta("owner_roots", _roots.Signature);
				}
			}
			return new HashSet<ConfigRoot>(_roots.All);
		}
		string meta = _db.GetMeta("owner_roots");
		if (string.IsNullOrEmpty(meta))
		{
			return new HashSet<ConfigRoot>(_roots.All);
		}
		HashSet<string> hashSet = new HashSet<string>(meta.Split('|'), StringComparer.Ordinal);
		HashSet<ConfigRoot> hashSet2 = new HashSet<ConfigRoot>();
		foreach (ConfigRoot item in _roots.All)
		{
			if (hashSet.Contains(ConfigRoots.SignatureEntry(item)))
			{
				hashSet2.Add(item);
			}
			else
			{
				Console.Error.WriteLine("MCP_LOG: root " + item.Path + " is not in the Confaster root set — skipped by this process");
			}
		}
		Console.Error.Flush();
		return hashSet2;
	}

	private (int added, int updated, int deleted) IncrementalSyncCore()
	{
		Stopwatch sw = Stopwatch.StartNew();
		_activeRoots = ResolveActiveRoots();
		bool complete;
		Dictionary<string, (long mtime, long size)> currentFiles = ScanFileSystem(out complete);
		Dictionary<string, (long, long)> indexedFiles = _db.GetIndexedFiles();
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		List<string> deletedFiles = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(currentFiles.Keys, StringComparer.Ordinal);
		HashSet<string> rekeyed = new HashSet<string>(StringComparer.Ordinal);
		bool keepUnknownConfigs = KeepUnknownConfigs;
		foreach (KeyValuePair<string, (long, long)> item4 in currentFiles)
		{
			item4.Deconstruct(out var key, out var value);
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
			if (hashSet.Contains(key2))
			{
				continue;
			}
			if (currentFiles.ContainsKey(key2))
			{
				rekeyed.Add(key2);
				deletedFiles.Add(key2);
				continue;
			}
			ConfigRoot item = _roots.SplitKey(key2).root;
			if ((_ownsIndex || (item != null && _activeRoots.Contains(item))) && (!(item == null && keepUnknownConfigs) || !ConfigRoots.IsExtensionKey(key2)))
			{
				deletedFiles.Add(key2);
			}
		}
		if (!complete && deletedFiles.Count > 0)
		{
			int num3 = deletedFiles.RemoveAll((string p) => !rekeyed.Contains(p) && (_unreadableRoots.Contains(_roots.SplitKey(p).root) || ResolveFullPath(p) != null));
			if (num3 > 0)
			{
				Console.Error.WriteLine($"MCP_LOG: scan incomplete — {num3} files not listed but present on disk, kept in index");
				Console.Error.Flush();
			}
		}
		if (list.Count == 0 && list2.Count == 0 && deletedFiles.Count == 0)
		{
			MetadataPlan metadataPlan = PlanMetadata(Array.Empty<string>());
			string text2 = 2.ToString(CultureInfo.InvariantCulture);
			bool flag = _db.GetMeta("domain_aliases_version") != text2 || _db.GetMeta("domain_aliases_hash") != ComputeDictionaryHash();
			if (metadataPlan != null || flag)
			{
				lock (_db.WriteLock)
				{
					_db.BeginTransaction();
					try
					{
						if (metadataPlan != null)
						{
							ExecuteMetadataPlan(metadataPlan);
						}
						if (flag)
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
		int num4 = 0;
		List<string> list3 = new List<string>(list.Count + list2.Count);
		list3.AddRange(list);
		list3.AddRange(list2);
		List<string> list4 = new List<string>();
		foreach (string item5 in list3)
		{
			if (item5.EndsWith(".bsl", StringComparison.OrdinalIgnoreCase))
			{
				list4.Add(item5);
			}
		}
		HashSet<string> changedSet = new HashSet<string>(list2, StringComparer.OrdinalIgnoreCase);
		List<string> nonBslToIndex = new List<string>();
		foreach (string item6 in list3)
		{
			if (!item6.EndsWith(".bsl", StringComparison.OrdinalIgnoreCase))
			{
				nonBslToIndex.Add(item6);
			}
		}
		Console.Error.WriteLine($"MCP_LOG: sync start: {list.Count} new, {list2.Count} changed, {deletedFiles.Count} deleted ({list4.Count} .bsl to parse)");
		Console.Error.Flush();
		lock (_db.WriteLock)
		{
			InTx(delegate
			{
				foreach (string item7 in deletedFiles)
				{
					_db.DeleteSymbolsByFile(item7);
					_db.DeleteRegionsByFile(item7);
					_db.DeleteIndexedFile(item7);
					string text3 = ResolveFullPath(item7);
					if (text3 != null)
					{
						_fileCache.Invalidate(text3);
					}
				}
			});
			for (int num5 = 0; num5 < nonBslToIndex.Count; num5 += 500)
			{
				int end = Math.Min(num5 + 500, nonBslToIndex.Count);
				int from = num5;
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
			int num6 = 0;
			for (int num7 = 0; num7 < list4.Count; num7 += 500)
			{
				int num8 = Math.Min(num7 + 500, list4.Count);
				List<string> batchPaths = list4.GetRange(num7, num8 - num7);
				ConcurrentQueue<ParsedBslFile> parsedBatch = ParseBatchParallel(batchPaths);
				num4 += batchPaths.Count - parsedBatch.Count;
				InTx(delegate
				{
					foreach (string item8 in batchPaths)
					{
						if (changedSet.Contains(item8))
						{
							_db.DeleteSymbolsByFile(item8);
							_db.DeleteRegionsByFile(item8);
							string text3 = ResolveFullPath(item8);
							if (text3 != null)
							{
								_fileCache.Invalidate(text3);
							}
						}
					}
					foreach (ParsedBslFile item9 in parsedBatch)
					{
						foreach (ParsedSymbol symbol in item9.Symbols)
						{
							SymbolInfo info = symbol.Info;
							long num9 = _db.InsertSymbol(item9.Path, info.Name, info.Kind, info.StartLine, info.EndLine, info.IsExport, info.IsAsync, info.Directive, info.Annotation, info.AnnotationTarget);
							_db.InsertSymbolTerms(num9, symbol.NameTokens, info.CommentHead ?? "", symbol.ParamTokens);
							foreach (var (calleeName, lineNumber) in info.Calls)
							{
								_db.InsertCall(num9, calleeName, lineNumber);
							}
							totalCalls += info.Calls.Count;
						}
						totalSymbols += item9.Symbols.Count;
						foreach (RegionInfo region in item9.Regions)
						{
							_db.InsertRegion(item9.Path, region.Name, region.StartLine, region.EndLine);
						}
						totalRegions += item9.Regions.Count;
						int num10 = parsedBslFiles;
						parsedBslFiles = num10 + 1;
					}
					foreach (string item10 in batchPaths)
					{
						if (ResolveFullPath(item10) != null)
						{
							var (mtime, size) = currentFiles[item10];
							_db.UpsertIndexedFile(item10, mtime, size);
						}
					}
				});
				num6 += batchPaths.Count;
				Console.Error.WriteLine($"MCP_LOG: indexing... {num6}/{list4.Count} .bsl, {totalSymbols} symbols so far");
				Console.Error.Flush();
			}
			IEnumerable<string> metadataTouched = list.Concat(deletedFiles).Concat(list2.Where((string p) => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)));
			PostStep("metadata", delegate
			{
				MetadataPlan metadataPlan2 = PlanMetadata(metadataTouched);
				if (metadataPlan2 != null)
				{
					ExecuteMetadataPlan(metadataPlan2);
				}
			});
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
		Console.Error.WriteLine($"MCP_LOG: sync complete: {list.Count} new, {list2.Count} changed, {deletedFiles.Count} deleted | parsed {parsedBslFiles} .bsl files ({totalSymbols} symbols, {totalCalls} calls, {totalRegions} regions, {num4} errors) in {sw.ElapsedMilliseconds} ms");
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
					byte[] item2 = tuple3.utf8Bytes;
					string[] item3 = tuple3.lines;
					List<SymbolInfo> obj2 = ((parser != IntPtr.Zero) ? BslAstParser.ParseWithParser(parser, item2, item3) : BslSymbolParser.Parse(item2, item3));
					List<RegionInfo> regions = BslSymbolParser.ParseRegions(item3);
					List<ParsedSymbol> list5 = new List<ParsedSymbol>(obj2.Count);
					foreach (SymbolInfo item11 in obj2)
					{
						string nameTokens = string.Join(' ', Tokenizer.TokenizeIdentifier(item11.Name));
						string paramTokens = ((item11.Parameters != null) ? string.Join(' ', Tokenizer.TokenizeIdentifier(item11.Parameters)) : "");
						list5.Add(new ParsedSymbol
						{
							Info = item11,
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

	private MetadataPlan? PlanMetadata(IEnumerable<string> touchedKeys)
	{
		string text = (_ownsIndex ? _db.GetMeta("metadata_roots") : null);
		if (_db.QueryScalar("SELECT COUNT(*) FROM objects") == 0L || _db.GetMeta("metadata_parser_version") != 3.ToString(CultureInfo.InvariantCulture) || (_ownsIndex && text == null))
		{
			return new MetadataPlan(Full: true, new List<ConfigRoot>(), new List<string>());
		}
		HashSet<ConfigRoot> hashSet = new HashSet<ConfigRoot>(PendingMetadataRetry());
		foreach (string touchedKey in touchedKeys)
		{
			ConfigRoot item = _roots.SplitKey(touchedKey).root;
			if (item != null && _activeRoots.Contains(item))
			{
				hashSet.Add(item);
			}
		}
		List<string> list = new List<string>();
		if (_ownsIndex && !KeepUnknownConfigs)
		{
			HashSet<string> currentIds = new HashSet<string>(_roots.All.Select((ConfigRoot r) => r.Id), StringComparer.Ordinal);
			list.AddRange(from id in LoadObjectConfigs()
				where !currentIds.Contains(id)
				select id);
		}
		if (_ownsIndex && text != _roots.Signature)
		{
			HashSet<string> hashSet2 = new HashSet<string>(text.Split('|'), StringComparer.Ordinal);
			foreach (ConfigRoot activeRoot in _activeRoots)
			{
				if (!hashSet2.Contains(ConfigRoots.SignatureEntry(activeRoot)))
				{
					hashSet.Add(activeRoot);
				}
			}
			return new MetadataPlan(Full: false, hashSet.ToList(), list);
		}
		if (hashSet.Count != 0 || list.Count != 0)
		{
			return new MetadataPlan(Full: false, hashSet.ToList(), list);
		}
		return null;
	}

	private void ExecuteMetadataPlan(MetadataPlan plan)
	{
		if (plan.Full)
		{
			RebuildMetadata();
		}
		else
		{
			RebuildMetadata(plan.Roots, _ownsIndex ? plan.RemovedConfigs : null);
		}
	}

	private void RebuildMetadata(IReadOnlyCollection<ConfigRoot>? only = null, IReadOnlyCollection<string>? removedConfigs = null)
	{
		List<(ConfigRoot, List<ObjectMetadata>)> list = new List<(ConfigRoot, List<ObjectMetadata>)>();
		HashSet<string> failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<ConfigRoot> list2 = new List<ConfigRoot>();
		List<ConfigRoot> list3 = new List<ConfigRoot>();
		foreach (ConfigRoot item4 in only ?? _activeRoots)
		{
			if (_unreadableRoots.Contains(item4))
			{
				failed.Add(item4.Id);
				list2.Add(item4);
				continue;
			}
			try
			{
				List<ObjectMetadata> item = (Directory.Exists(item4.Path) ? MetadataParser.ScanExportRoot(_roots, item4) : new List<ObjectMetadata>());
				list.Add((item4, item));
			}
			catch (Exception ex) when (!item4.IsMain)
			{
				failed.Add(item4.Id);
				list2.Add(item4);
				list3.Add(item4);
				_roots.AddScanWarning($"Не удалось прочитать метаданные расширения «{item4.Id}» ({item4.Path}): {ex.Message}. " + "Объекты расширения в индексе не обновлены.");
				Console.Error.WriteLine("MCP_LOG: WARNING: metadata scan " + item4.Path + " failed: " + ex.Message);
				Console.Error.Flush();
			}
		}
		HashSet<string> knownIds = new HashSet<string>(_roots.All.Select((ConfigRoot r) => r.Id), StringComparer.Ordinal);
		object obj;
		if (only == null)
		{
			obj = (_ownsIndex ? (from id in LoadObjectConfigs()
				where !KeepUnknownConfigs || knownIds.Contains(id)
				select id) : _activeRoots.Select((ConfigRoot r) => r.Id));
		}
		else
		{
			IEnumerable<string> enumerable = removedConfigs;
			enumerable = enumerable ?? Array.Empty<string>();
			obj = enumerable;
		}
		IEnumerable<string> source = (IEnumerable<string>)obj;
		_db.ClearObjects(source.Where((string id) => !failed.Contains(id)).Concat(list.Select<(ConfigRoot, List<ObjectMetadata>), string>(((ConfigRoot root, List<ObjectMetadata> objects) s) => s.root.Id)).Distinct());
		foreach (var (root, objects) in list)
		{
			InsertMetadata(root, objects);
		}
		if (only == null)
		{
			_db.SetMeta("metadata_parser_version", 3.ToString(CultureInfo.InvariantCulture));
		}
		if (!_ownsIndex)
		{
			if (only == null)
			{
				_db.SetMeta("metadata_roots", "");
			}
			return;
		}
		HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);
		if (only != null)
		{
			pending.UnionWith(LoadPendingMetadataRoots());
		}
		foreach (var item5 in list)
		{
			ConfigRoot item2 = item5.Item1;
			pending.Remove(ConfigRoots.SignatureEntry(item2));
		}
		foreach (ConfigRoot item6 in list2)
		{
			pending.Add(ConfigRoots.SignatureEntry(item6));
		}
		if (only == null || removedConfigs != null)
		{
			_db.SetMeta("metadata_roots", _roots.Signature);
		}
		_db.SetMeta("metadata_pending_roots", string.Join("|", pending.OrderBy<string, string>((string s) => s, StringComparer.Ordinal)));
		Dictionary<string, long> broken = LoadBrokenMetadataRoots();
		foreach (var item7 in list)
		{
			ConfigRoot item3 = item7.Item1;
			broken.Remove(ConfigRoots.SignatureEntry(item3));
		}
		foreach (ConfigRoot item8 in list2)
		{
			broken.Remove(ConfigRoots.SignatureEntry(item8));
		}
		long ticks = DateTime.UtcNow.Ticks;
		foreach (ConfigRoot item9 in list3)
		{
			broken[ConfigRoots.SignatureEntry(item9)] = ticks;
		}
		broken.Keys.Where((string k) => !pending.Contains(k)).ToList().ForEach(delegate(string k)
		{
			broken.Remove(k);
		});
		_db.SetMeta("metadata_broken_roots", string.Join("\n", from p in broken.OrderBy<KeyValuePair<string, long>, string>((KeyValuePair<string, long> p) => p.Key, StringComparer.Ordinal)
			select p.Key + "\t" + p.Value.ToString(CultureInfo.InvariantCulture)));
	}

	private Dictionary<string, long> LoadBrokenMetadataRoots()
	{
		Dictionary<string, long> dictionary = new Dictionary<string, long>(StringComparer.Ordinal);
		string meta = _db.GetMeta("metadata_broken_roots");
		if (string.IsNullOrEmpty(meta))
		{
			return dictionary;
		}
		string[] array = meta.Split('\n');
		foreach (string text in array)
		{
			int num = text.LastIndexOf('\t');
			if (num > 0 && long.TryParse(text.Substring(num + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				dictionary[text.Substring(0, num)] = result;
			}
		}
		return dictionary;
	}

	private HashSet<string> LoadPendingMetadataRoots()
	{
		string meta = _db.GetMeta("metadata_pending_roots");
		if (!string.IsNullOrEmpty(meta))
		{
			return new HashSet<string>(meta.Split('|'), StringComparer.Ordinal);
		}
		return new HashSet<string>(StringComparer.Ordinal);
	}

	private List<ConfigRoot> PendingMetadataRetry()
	{
		if (!_ownsIndex)
		{
			return new List<ConfigRoot>();
		}
		HashSet<string> pending = LoadPendingMetadataRoots();
		if (pending.Count == 0)
		{
			return new List<ConfigRoot>();
		}
		Dictionary<string, long> broken = LoadBrokenMetadataRoots();
		long retryBefore = (DateTime.UtcNow - BrokenMetadataRetryInterval).Ticks;
		long value;
		return (from r in _activeRoots
			where pending.Contains(ConfigRoots.SignatureEntry(r)) && !_unreadableRoots.Contains(r)
			where !broken.TryGetValue(ConfigRoots.SignatureEntry(r), out value) || value < retryBefore
			select r).ToList();
	}

	private List<string> LoadObjectConfigs()
	{
		List<string> list = new List<string>();
		using Database.OwnedReader ownedReader = _db.Query("SELECT DISTINCT config FROM objects");
		while (ownedReader.Read())
		{
			list.Add(ownedReader.GetString(0));
		}
		return list;
	}

	private void InsertMetadata(ConfigRoot root, List<ObjectMetadata> objects)
	{
		foreach (ObjectMetadata @object in objects)
		{
			long num = _db.InsertObject(root.Id, @object.Fqn, @object.TypeFolder, @object.Name, @object.ExportPath, @object.Adopted);
			if (num == 0L)
			{
				continue;
			}
			foreach (string synonym in @object.Synonyms)
			{
				_db.InsertSynonym(num, synonym);
			}
			foreach (ObjectItemInfo item in @object.Items)
			{
				string parentName = null;
				string typeInfo = null;
				if (item.Kind == "ts_attribute")
				{
					parentName = item.ExtraInfo;
				}
				else
				{
					typeInfo = item.ExtraInfo;
				}
				_db.InsertObjectItem(num, item.Kind, item.Name, parentName, typeInfo, item.Adopted);
			}
		}
	}

	private string? ResolveFullPath(string key)
	{
		return _roots.ResolveKey(key);
	}

	private Dictionary<string, (long mtime, long size)> ScanFileSystem(out bool complete)
	{
		Dictionary<string, (long, long)> result = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
		complete = true;
		_unreadableRoots.Clear();
		_roots.ClearScanWarnings();
		foreach (ConfigRoot activeRoot in _activeRoots)
		{
			complete &= ScanRoot(activeRoot, result);
		}
		return result;
	}

	private bool ScanRoot(ConfigRoot configRoot, Dictionary<string, (long mtime, long size)> result)
	{
		string path = configRoot.Path;
		if (!Directory.Exists(path))
		{
			return SkipUnreadableRoot(configRoot, "папка недоступна или не существует");
		}
		EnumerationOptions options = new EnumerationOptions
		{
			RecurseSubdirectories = true,
			AttributesToSkip = FileAttributes.None,
			IgnoreInaccessible = false
		};
		using ScanEnumerator scanEnumerator = new ScanEnumerator(path, options, _roots.NestedRootsOf(configRoot));
		int num = 0;
		while (scanEnumerator.MoveNext())
		{
			(string fullPath, long mtime, long size) current = scanEnumerator.Current;
			string item = current.fullPath;
			long item2 = current.mtime;
			long item3 = current.size;
			string relativePath = PathHelper.GetRelativePath(item, path);
			result[_roots.ToKey(configRoot, relativePath)] = (item2, item3);
			num++;
		}
		if (scanEnumerator.SkippedErrors == 0)
		{
			return true;
		}
		string message = new Win32Exception(scanEnumerator.LastError).Message;
		if (num == 0)
		{
			return SkipUnreadableRoot(configRoot, message);
		}
		Console.Error.WriteLine($"MCP_LOG: scan {path}: skipped {scanEnumerator.SkippedErrors} unreadable items (last error {scanEnumerator.LastError}: {message}), indexed the rest");
		Console.Error.Flush();
		_roots.AddScanWarning($"Часть папок выгрузки {path} прочитать не удалось ({message}; пропущено: {scanEnumerator.SkippedErrors}). Код из них в поиске не обновлён. " + "Проверьте права доступа к папке выгрузки и нажмите «Обновить индексы».");
		return false;
	}

	private bool SkipUnreadableRoot(ConfigRoot configRoot, string reason)
	{
		string path = configRoot.Path;
		if (configRoot.IsMain || _roots.All.Count <= 1)
		{
			throw new IOException("Не удалось прочитать папку выгрузки " + path + ": " + reason);
		}
		_roots.AddScanWarning($"Не удалось прочитать папку выгрузки расширения «{configRoot.Id}» ({path}): {reason}. " + "Расширение не обновлено в индексе: в поиске остаются данные прошлой индексации (если она была). Проверьте путь или выгрузите расширение в файлы.");
		Console.Error.WriteLine("MCP_LOG: WARNING: scan " + path + " failed: " + reason);
		Console.Error.Flush();
		_unreadableRoots.Add(configRoot);
		return false;
	}
}
