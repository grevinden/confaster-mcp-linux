using System.Text.Json;
using ConfasterMCP.Help;
using Microsoft.Data.Sqlite;

namespace ConfasterMCP.Tools;

internal sealed class List1cHelpVersionsTool : IConfasterMCPTool
{
	private readonly HelpDatabase _db;

	private readonly HelpIndexer _indexer;

	public string Name => "list_1c_help_versions";

	public string Description => "Получить статус индексации справки 1С и статистику.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new { }
	});

	internal List1cHelpVersionsTool(HelpDatabase db, HelpIndexer indexer)
	{
		_db = db;
		_indexer = indexer;
	}

	public string Execute(JsonElement args)
	{
		bool isIndexing = _indexer.IsIndexing;
		string meta;
		string meta2;
		string meta3;
		try
		{
			meta = _db.GetMeta("indexed_version");
			meta2 = _db.GetMeta("topic_count");
			meta3 = _db.GetMeta("indexed_at");
		}
		catch (SqliteException)
		{
			_indexer.RepairAndReindex();
			return JsonSerializer.Serialize(new
			{
				error = "Индекс справки повреждён. Запущено восстановление.",
				is_indexing = true,
				platform_path = HelpIndexer.FindPlatformPath()
			});
		}
		return JsonSerializer.Serialize(new
		{
			indexed_version = meta,
			topic_count = meta2,
			indexed_at = meta3,
			is_indexing = isIndexing,
			platform_path = HelpIndexer.FindPlatformPath()
		});
	}
}
