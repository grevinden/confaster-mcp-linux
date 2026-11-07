using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using ConfasterMCP.Help;
using Microsoft.Data.Sqlite;

namespace ConfasterMCP.Tools;

internal sealed class Search1cHelpTool : IConfasterMCPTool
{
	private readonly HelpDatabase _db;

	private readonly HelpIndexer _indexer;

	public string Name => "search_1c_help";

	public string Description => "Полнотекстовый поиск по официальной справке платформы 1С:Предприятие 8.3. Ищет по всем разделам: встроенный язык, объектная модель, язык запросов.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			query = new
			{
				type = "string",
				description = "Поисковый запрос (название метода, объекта, функции)"
			},
			limit = new
			{
				type = "integer",
				description = "Максимальное количество результатов (по умолчанию 5)",
				@default = 5
			},
			category = new
			{
				type = "string",
				description = "Раздел справки: syntax — объектная модель, query — язык запросов, language — встроенный язык, all — все разделы",
				@enum = new string[4] { "syntax", "query", "language", "all" }
			}
		},
		required = new string[1] { "query" }
	});

	internal Search1cHelpTool(HelpDatabase db, HelpIndexer indexer)
	{
		_db = db;
		_indexer = indexer;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("query", out value) ? (value.GetString() ?? "") : "");
		int value2 = (args.TryGetProperty("limit", out var value3) ? value3.GetInt32() : 5);
		JsonElement value4;
		string category = (args.TryGetProperty("category", out value4) ? value4.GetString() : null);
		if (string.IsNullOrEmpty(text))
		{
			return "{\"error\": \"query is required\"}";
		}
		value2 = Math.Clamp(value2, 1, 20);
		List<HelpSearchResult> list;
		try
		{
			list = _db.Search(text, value2, category);
		}
		catch (SqliteException)
		{
			_indexer.RepairAndReindex();
			return JsonSerializer.Serialize(new
			{
				error = "Индекс справки повреждён. Запущено восстановление, повторите запрос через 1-2 минуты."
			});
		}
		if (list.Count == 0)
		{
			return JsonSerializer.Serialize(new
			{
				message = "По запросу ничего не найдено в справке 1С.",
				results = Array.Empty<object>()
			});
		}
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(35, 1, stringBuilder2);
		handler.AppendLiteral("Найдено ");
		handler.AppendFormatted(list.Count);
		handler.AppendLiteral(" результатов в справке 1С:\n");
		stringBuilder3.AppendLine(ref handler);
		for (int i = 0; i < list.Count; i++)
		{
			HelpSearchResult helpSearchResult = list[i];
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(6, 2, stringBuilder2);
			handler.AppendFormatted(i + 1);
			handler.AppendLiteral(". **");
			handler.AppendFormatted(helpSearchResult.Title);
			handler.AppendLiteral("**");
			stringBuilder4.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
			handler.AppendLiteral("   topic_id: ");
			handler.AppendFormatted(helpSearchResult.TopicId);
			stringBuilder5.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(3, 1, stringBuilder2);
			handler.AppendLiteral("   ");
			handler.AppendFormatted(helpSearchResult.Excerpt);
			stringBuilder6.AppendLine(ref handler);
			stringBuilder.AppendLine();
		}
		return JsonSerializer.Serialize(new
		{
			text = stringBuilder.ToString(),
			total = list.Count
		});
	}
}
