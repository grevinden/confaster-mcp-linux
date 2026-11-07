using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ConfasterMCP.Indexing;

namespace ConfasterMCP.Tools;

internal sealed class SearchCodeTool : IConfasterMCPTool
{
	private readonly FileScanner _scanner;

	public string Name => "search_code";

	public string Description => "Поиск по тексту кода 1С (BSL/XML). ⚠\ufe0f СТОП: если ищешь функцию по описанию задачи и не знаешь её имя — сначала вызови semantic_find (он специально создан для этого и работает за 1 вызов). search_code используй только когда semantic_find не помог или ты знаешь конкретный текстовый паттерн.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			query = new
			{
				type = "string",
				description = "Поисковый запрос — имя процедуры, функции или любой текст"
			},
			limit = new
			{
				type = "integer",
				description = "Максимум результатов (по умолчанию 20, максимум 100)",
				@default = 20
			},
			regex = new
			{
				type = "boolean",
				description = "Использовать регулярное выражение",
				@default = false
			},
			scope = new
			{
				type = "string",
				description = "Ограничить поиск объектом 1С: 'CommonModules/МодульИмя', 'Documents/ДокИмя' и т.д."
			}
		},
		required = new string[1] { "query" }
	});

	internal SearchCodeTool(FileScanner scanner)
	{
		_scanner = scanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("query", out value) ? (value.GetString() ?? "") : "");
		JsonElement value2;
		int limit = (args.TryGetProperty("limit", out value2) ? value2.GetInt32() : 20);
		JsonElement value3;
		bool isRegex = args.TryGetProperty("regex", out value3) && value3.GetBoolean();
		JsonElement value4;
		string scope = (args.TryGetProperty("scope", out value4) ? value4.GetString() : null);
		if (string.IsNullOrEmpty(text))
		{
			return "{\"error\": \"query is required\"}";
		}
		List<SearchResult> list = _scanner.Search(text, limit, isRegex, scope);
		return JsonSerializer.Serialize(new
		{
			results = list.Select((SearchResult r) => new
			{
				file = r.DisplayPath,
				absolute_path = r.AbsolutePath,
				line = r.LineNumber,
				text = r.LineText,
				context = r.Context
			}),
			total = list.Count
		});
	}
}
