using System;
using System.IO;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.TreeSitter;

namespace ConfasterMCP.Tools;

internal sealed class StatsTool : IConfasterMCPTool
{
	private readonly Database _db;

	private readonly string _exportRoot;

	public string Name => "stats";

	public string Description => "Статистика индекса конфигурации 1С: количество символов, файлов, объектов, вызовов.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new { }
	});

	internal StatsTool(Database db, string exportRoot)
	{
		_db = db;
		_exportRoot = exportRoot;
	}

	public string Execute(JsonElement args)
	{
		long symbol_count = _db.QueryScalar("SELECT COUNT(*) FROM symbols");
		long file_count = _db.QueryScalar("SELECT COUNT(*) FROM indexed_files");
		long object_count = _db.QueryScalar("SELECT COUNT(*) FROM objects");
		long call_count = _db.QueryScalar("SELECT COUNT(*) FROM calls");
		long region_count = _db.QueryScalar("SELECT COUNT(*) FROM regions");
		string meta = _db.GetMeta("last_sync_at");
		string meta2 = _db.GetMeta("sync_duration_ms");
		string text = Path.Combine(_exportRoot, ".index", "onec_search.db");
		long num = (File.Exists(text) ? new FileInfo(text).Length : 0);
		string tree_sitter_status = (BslAstParser.IsInitialized ? "ok" : ("failed: " + (BslAstParser.InitError ?? "unknown")));
		return JsonSerializer.Serialize(new
		{
			symbol_count = symbol_count,
			file_count = file_count,
			object_count = object_count,
			call_count = call_count,
			region_count = region_count,
			last_sync_at = meta,
			sync_duration_ms = meta2,
			db_size_mb = Math.Round((double)num / 1024.0 / 1024.0, 2),
			export_root = _exportRoot,
			tree_sitter_status = tree_sitter_status
		});
	}
}
