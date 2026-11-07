using System.Text.Json;
using ConfasterMCP.Help;
using Microsoft.Data.Sqlite;

namespace ConfasterMCP.Tools;

internal sealed class Get1cHelpTopicTool : IConfasterMCPTool
{
	private readonly HelpDatabase _db;

	private readonly HelpIndexer _indexer;

	public string Name => "get_1c_help_topic";

	public string Description => "Получить полное содержимое темы из справки 1С по идентификатору (topic_id из результатов search_1c_help).";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			topic_id = new
			{
				type = "string",
				description = "Идентификатор темы из результатов поиска"
			}
		},
		required = new string[1] { "topic_id" }
	});

	internal Get1cHelpTopicTool(HelpDatabase db, HelpIndexer indexer)
	{
		_db = db;
		_indexer = indexer;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("topic_id", out value) ? (value.GetString() ?? "") : "");
		if (string.IsNullOrEmpty(text))
		{
			return "{\"error\": \"topic_id is required\"}";
		}
		string text2;
		string text3;
		try
		{
			(text2, text3) = _db.GetTopic(text);
		}
		catch (SqliteException)
		{
			_indexer.RepairAndReindex();
			return JsonSerializer.Serialize(new
			{
				error = "Индекс справки повреждён. Запущено восстановление, повторите запрос через 1-2 минуты."
			});
		}
		if (text2 == null || text3 == null)
		{
			return JsonSerializer.Serialize(new
			{
				error = "Тема не найдена: " + text
			});
		}
		return JsonSerializer.Serialize(new
		{
			title = text2,
			content = text3
		});
	}
}
