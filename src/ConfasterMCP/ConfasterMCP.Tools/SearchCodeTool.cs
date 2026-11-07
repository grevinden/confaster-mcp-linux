using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class SearchCodeTool : IConfasterMCPTool
{
	private readonly FileScanner _scanner;

	public string Name => "search_code";

	public string Description => "Поиск по тексту кода 1С (BSL/XML) в основной конфигурации и подключённых расширениях. ⚠\ufe0f СТОП: если ищешь функцию по описанию задачи и не знаешь её имя — сначала вызови semantic_find (он специально создан для этого и работает за 1 вызов). search_code используй только когда semantic_find не помог или ты знаешь конкретный текстовый паттерн.";

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
			},
			configuration = new
			{
				type = "string",
				description = "Искать только в одной конфигурации: имя расширения или 'основная'. По умолчанию — везде."
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
		JsonElement value5;
		string name = (args.TryGetProperty("configuration", out value5) ? value5.GetString() : null);
		if (string.IsNullOrEmpty(text))
		{
			return "{\"error\": \"query is required\"}";
		}
		if (_scanner.Roots.IsUnknownName(name, out ConfigRoot root))
		{
			return ToolJson.UnknownConfiguration(_scanner.Roots, name);
		}
		FileScanner scanner = _scanner;
		ConfigRoot configuration = root;
		List<SearchResult> list = scanner.Search(text, limit, isRegex, scope, default(CancellationToken), configuration);
		return ToolJson.Serialize(new
		{
			results = list.Select((SearchResult x) => new
			{
				file = x.DisplayPath,
				configuration = x.Configuration,
				absolute_path = x.AbsolutePath,
				line = x.LineNumber,
				text = x.LineText,
				context = x.Context
			}),
			total = list.Count
		});
	}
}
