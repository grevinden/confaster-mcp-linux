using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Semantic;
using ConfasterMCP.TreeSitter;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class SemanticFindTool : IConfasterMCPTool
{
	private readonly Database _db;

	private readonly FileScanner _fileScanner;

	private readonly FileCache _fileCache;

	private readonly SemanticSearch _search;

	public string Name => "semantic_find";

	public string Description => "\ud83d\udd0d ПЕРВЫЙ ИНСТРУМЕНТ при поиске функции по описанию задачи. Семантический поиск по именам функций, комментариям, параметрам. ОБЯЗАТЕЛЕН как первый шаг если не знаешь точное имя функции. Запрос на русском языке: что делает функция, какой объект обрабатывает. Возвращает ТОП-5 функций с score — если score ≥ 0.5 это сильный результат.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			query = new
			{
				type = "string",
				description = "Естественно-языковое описание задачи: 'расчёт остатков товара', 'проверка контрагента по ИНН' и т.д."
			},
			context_objects = new
			{
				type = "array",
				items = new
				{
					type = "string"
				},
				description = "Подсказки — имена объектов 1С (напр. 'Catalog.Контрагенты', 'Document.Накладная'), которые усилят релевантность."
			},
			limit = new
			{
				type = "integer",
				description = "Сколько результатов вернуть (по умолчанию 5, максимум 20)",
				@default = 5
			},
			include_code = new
			{
				type = "boolean",
				description = "Включить тело функции для лучшего результата (по умолчанию true)",
				@default = true
			}
		},
		required = new string[1] { "query" }
	});

	internal SemanticFindTool(Database db, FileScanner fileScanner, FileCache fileCache)
	{
		_db = db;
		_fileScanner = fileScanner;
		_fileCache = fileCache;
		_search = new SemanticSearch(db);
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("query", out value) ? (value.GetString() ?? "") : "");
		if (string.IsNullOrWhiteSpace(text))
		{
			return "{\"error\": \"query is required\"}";
		}
		if (!BslAstParser.IsInitialized)
		{
			return JsonSerializer.Serialize(new
			{
				error = "tree-sitter not initialized — semantic search unavailable",
				details = (BslAstParser.InitError ?? "unknown"),
				hint = "Use search_code with the same query as a fallback",
				results = Array.Empty<object>()
			});
		}
		int limit = 5;
		if (args.TryGetProperty("limit", out var value2) && value2.ValueKind == JsonValueKind.Number)
		{
			limit = Math.Clamp(value2.GetInt32(), 1, 20);
		}
		bool flag = true;
		if (args.TryGetProperty("include_code", out var value3) && value3.ValueKind == JsonValueKind.False)
		{
			flag = false;
		}
		List<string> list = new List<string>();
		if (args.TryGetProperty("context_objects", out var value4) && value4.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in value4.EnumerateArray())
			{
				string text2 = item.GetString();
				if (!string.IsNullOrWhiteSpace(text2))
				{
					list.Add(text2);
				}
			}
		}
		List<SemanticSearch.Result> list2 = _search.Search(text, list, limit, _fileScanner.Roots.KnownKeysSql("s.file_path"));
		if (list2.Count == 0)
		{
			if (_db.QueryScalar("SELECT COUNT(*) FROM symbol_terms") == 0L)
			{
				return JsonSerializer.Serialize(new
				{
					results = Array.Empty<object>(),
					total = 0,
					message = "Индекс семантики пуст. Вызови sync_index и повтори запрос."
				});
			}
			return JsonSerializer.Serialize(new
			{
				results = Array.Empty<object>(),
				total = 0,
				message = "Совпадений не найдено. Попробуй переформулировать запрос или используй search_code."
			});
		}
		List<SymbolResult> list3 = list2.Select((SemanticSearch.Result r) => new SymbolResult
		{
			Key = r.FilePath,
			name = r.Name
		}).ToList();
		ExtensionLinks.FillInterceptors(_db, _fileScanner.Roots, list3);
		List<object> list4 = new List<object>(list2.Count);
		for (int num = 0; num < list2.Count; num++)
		{
			SemanticSearch.Result result = list2[num];
			string code = null;
			if (flag)
			{
				code = ReadCode(result);
			}
			list4.Add(new
			{
				file = ConfigRoots.DisplayPath(result.FilePath),
				configuration = _fileScanner.LabelOf(result.FilePath),
				absolute_path = _fileScanner.ResolveFullPath(result.FilePath),
				name = result.Name,
				kind = result.Kind,
				start_line = result.StartLine,
				end_line = result.EndLine,
				is_export = result.IsExport,
				score = Math.Round(result.FinalScore, 4),
				call_count = result.CallCount,
				caller_diversity = result.CallerDiversity,
				intercepted_by = list3[num].intercepted_by,
				code = code
			});
		}
		return ToolJson.Serialize(new
		{
			results = list4,
			total = list2.Count,
			hint = ((list2[0].FinalScore >= 0.5) ? "Лучший результат имеет score ≥ 0.5 — сильный кандидат." : "Все результаты с низким score — возможно, функция называется иначе. Можно попробовать search_code.")
		});
	}

	private string? ReadCode(SemanticSearch.Result r)
	{
		try
		{
			string text = _fileScanner.ResolveFullPath(r.FilePath);
			if (text == null)
			{
				return null;
			}
			string[] lines = _fileCache.GetLines(text);
			int num = Math.Max(0, r.StartLine - 1);
			int num2 = Math.Min(lines.Length, r.EndLine);
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = num; i < num2; i++)
			{
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, stringBuilder2);
				handler.AppendFormatted(i + 1, 6);
				handler.AppendLiteral(": ");
				handler.AppendFormatted(lines[i]);
				stringBuilder2.AppendLine(ref handler);
			}
			return stringBuilder.ToString();
		}
		catch
		{
			return null;
		}
	}
}
