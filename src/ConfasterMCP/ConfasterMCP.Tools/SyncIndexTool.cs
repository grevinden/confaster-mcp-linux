using System.Text.Json;
using ConfasterMCP.Indexing;

namespace ConfasterMCP.Tools;

internal sealed class SyncIndexTool : IConfasterMCPTool
{
	private readonly IndexManager _indexManager;

	public string Name => "sync_index";

	public string Description => "Инкрементальная синхронизация индекса: обновляет только изменённые файлы.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new { }
	});

	internal SyncIndexTool(IndexManager indexManager)
	{
		_indexManager = indexManager;
	}

	public string Execute(JsonElement args)
	{
		var (num, num2, num3) = _indexManager.IncrementalSync();
		return JsonSerializer.Serialize(new
		{
			added = num,
			updated = num2,
			deleted = num3,
			message = ((num == 0 && num2 == 0 && num3 == 0) ? "Индекс актуален, изменений не найдено" : $"Синхронизация завершена: {num} добавлено, {num2} обновлено, {num3} удалено")
		});
	}
}
