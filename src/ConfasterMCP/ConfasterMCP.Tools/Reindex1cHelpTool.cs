using System.Text.Json;
using ConfasterMCP.Help;

namespace ConfasterMCP.Tools;

internal sealed class Reindex1cHelpTool : IConfasterMCPTool
{
	private readonly HelpIndexer _indexer;

	public string Name => "reindex_1c_help";

	public string Description => "Принудительно пересоздать индекс справки 1С:Предприятие.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new { }
	});

	internal Reindex1cHelpTool(HelpIndexer indexer)
	{
		_indexer = indexer;
	}

	public string Execute(JsonElement args)
	{
		if (_indexer.IsIndexing)
		{
			return JsonSerializer.Serialize(new
			{
				message = "Индексация уже выполняется. Подождите завершения."
			});
		}
		_indexer.ForceReindex();
		return JsonSerializer.Serialize(new
		{
			message = "Переиндексация справки запущена. Это может занять 1-3 минуты."
		});
	}
}
