using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using ConfasterMCP.Indexing;

namespace ConfasterMCP.Tools;

internal sealed class ImpactAnalysisTool : IConfasterMCPTool
{
	private readonly FileScanner _scanner;

	public string Name => "impact_analysis";

	public string Description => "Анализ влияния: показывает какие модули и файлы используют данный объект или символ. Помогает понять последствия изменений.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			@object = new
			{
				type = "string",
				description = "Имя объекта или символа для анализа влияния"
			}
		},
		required = new string[1] { "object" }
	});

	internal ImpactAnalysisTool(FileScanner scanner)
	{
		_scanner = scanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("object", out value) ? (value.GetString() ?? "") : "");
		if (string.IsNullOrEmpty(text))
		{
			return "{\"error\": \"object is required\"}";
		}
		List<SearchResult> list = _scanner.Search(text, 100, isRegex: false, null, default(CancellationToken), null, exhaustive: true);
		var list2 = (from r in list
			group r by r.FilePath into g
			select new
			{
				file = g.First().DisplayPath,
				configuration = g.First().Configuration,
				absolute_path = g.First().AbsolutePath,
				count = g.Count(),
				examples = from r in g.Take(3)
					select $"строка {r.LineNumber}: {r.LineText}"
			} into g
			orderby g.count descending
			select g).ToList();
		return ToolJson.Serialize(new
		{
			@object = text,
			impacted_files = list2,
			total_files = list2.Count,
			total_references = list.Count
		});
	}
}
