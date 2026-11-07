using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.TreeSitter;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class StatsTool : IConfasterMCPTool
{
	private readonly Database _db;

	private readonly ConfigRoots _roots;

	private const string ConfigOfPath = "CASE WHEN {0} LIKE '@%/%' THEN substr({0}, 2, instr({0}, '/') - 2) ELSE '' END";

	public string Name => "stats";

	public string Description => "Статистика индекса конфигурации 1С: количество символов, файлов, объектов, вызовов — в целом и отдельно по основной конфигурации и каждому расширению.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new { }
	});

	internal StatsTool(Database db, ConfigRoots roots)
	{
		_db = db;
		_roots = roots;
	}

	public string Execute(JsonElement args)
	{
		long num = _db.QueryScalar("SELECT COUNT(*) FROM symbols");
		long num2 = _db.QueryScalar("SELECT COUNT(*) FROM indexed_files");
		long object_count = _db.QueryScalar("SELECT COUNT(*) FROM objects");
		long call_count = _db.QueryScalar("SELECT COUNT(*) FROM calls");
		long region_count = _db.QueryScalar("SELECT COUNT(*) FROM regions");
		string meta = _db.GetMeta("last_sync_at");
		string meta2 = _db.GetMeta("sync_duration_ms");
		string text = Path.Combine(_roots.IndexRoot, ".index", "onec_search.db");
		long num3 = (File.Exists(text) ? new FileInfo(text).Length : 0);
		string tree_sitter_status = (BslAstParser.IsInitialized ? "ok" : ("failed: " + (BslAstParser.InitError ?? "unknown")));
		Dictionary<string, long> files = CountByKeyRanges("indexed_files", "path", num2);
		Dictionary<string, long> symbols = CountByKeyRanges("symbols", "file_path", num);
		Dictionary<string, long> objects = CountBy("SELECT config, COUNT(*) FROM objects GROUP BY config");
		Dictionary<string, long> intercepts = CountBy("SELECT " + string.Format("CASE WHEN {0} LIKE '@%/%' THEN substr({0}, 2, instr({0}, '/') - 2) ELSE '' END", "file_path") + " AS cfg, COUNT(*) FROM symbols WHERE annotation_target_lower IS NOT NULL GROUP BY cfg");
		IReadOnlyList<string> warnings = _roots.Warnings;
		var configurations = _roots.All.Select((ConfigRoot r) => new
		{
			kind = (r.IsMain ? "main" : "extension"),
			name = (r.IsMain ? null : r.Id),
			label = r.Label,
			path = r.Path,
			name_prefix = r.NamePrefix,
			file_count = Get(files, r.Id),
			symbol_count = Get(symbols, r.Id),
			object_count = Get(objects, r.Id),
			intercepting_methods = (r.IsMain ? ((long?)null) : new long?(Get(intercepts, r.Id)))
		}).ToList();
		return ToolJson.Serialize(new
		{
			symbol_count = num,
			file_count = num2,
			object_count = object_count,
			call_count = call_count,
			region_count = region_count,
			last_sync_at = meta,
			sync_duration_ms = meta2,
			db_size_mb = Math.Round((double)num3 / 1024.0 / 1024.0, 2),
			export_root = (_roots.Main?.Path ?? ""),
			index_root = _roots.IndexRoot,
			configurations = configurations,
			warnings = ((warnings.Count > 0) ? warnings : null),
			tree_sitter_status = tree_sitter_status
		});
	}

	private Dictionary<string, long> CountByKeyRanges(string table, string column, long total)
	{
		Dictionary<string, long> dictionary = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
		try
		{
			dictionary[""] = total - CountRange("@", "A");
			foreach (ConfigRoot extension in _roots.Extensions)
			{
				dictionary[extension.Id] = CountRange("@" + extension.Id + "/", "@" + extension.Id + "0");
			}
		}
		catch
		{
		}
		return dictionary;
		long CountRange(string lo, string hi)
		{
			using Database.OwnedReader ownedReader = _db.Query($"SELECT COUNT(*) FROM {table} WHERE {column} >= @lo AND {column} < @hi", ("@lo", lo), ("@hi", hi));
			return ownedReader.Read() ? ownedReader.GetInt64(0) : 0;
		}
	}

	private Dictionary<string, long> CountBy(string sql)
	{
		Dictionary<string, long> dictionary = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
		try
		{
			using Database.OwnedReader ownedReader = _db.Query(sql);
			while (ownedReader.Read())
			{
				string key = (ownedReader.IsDBNull(0) ? "" : ownedReader.GetString(0));
				dictionary[key] = dictionary.GetValueOrDefault(key) + ownedReader.GetInt64(1);
			}
		}
		catch
		{
		}
		return dictionary;
	}

	private static long Get(Dictionary<string, long> map, string id)
	{
		return map.GetValueOrDefault(id);
	}
}
