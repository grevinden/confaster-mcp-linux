using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ConfasterMCP.Indexing;

namespace ConfasterMCP.Tools;

internal sealed class FindReferencesTool : IConfasterMCPTool
{
	private readonly FileScanner _scanner;

	public string Name => "find_references";

	public string Description => "Найти все вхождения символа (процедуры, функции, переменной) в коде конфигурации. Показывает где и как используется символ.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			symbol = new
			{
				type = "string",
				description = "Имя символа для поиска"
			},
			limit = new
			{
				type = "integer",
				description = "Максимум результатов (по умолчанию 50)",
				@default = 50
			}
		},
		required = new string[1] { "symbol" }
	});

	internal FindReferencesTool(FileScanner scanner)
	{
		_scanner = scanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("symbol", out value) ? (value.GetString() ?? "") : "");
		int value2 = (args.TryGetProperty("limit", out var value3) ? value3.GetInt32() : 50);
		if (string.IsNullOrEmpty(text))
		{
			return "{\"error\": \"symbol is required\"}";
		}
		value2 = Math.Clamp(value2, 1, 200);
		List<SearchResult> list = _scanner.Search(text, value2);
		var list2 = (from r in list
			group r by r.DisplayPath into g
			select new
			{
				file = g.Key,
				absolute_path = g.First().AbsolutePath,
				count = g.Count(),
				references = from r in g.Take(5)
					select new
					{
						line = r.LineNumber,
						text = r.LineText
					}
			} into g
			orderby g.count descending
			select g).ToList();
		return JsonSerializer.Serialize(new
		{
			symbol = text,
			files = list2,
			total_files = list2.Count,
			total_references = list.Count
		});
	}
}
