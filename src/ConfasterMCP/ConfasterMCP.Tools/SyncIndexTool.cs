using System;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class SyncIndexTool : IConfasterMCPTool
{
	private readonly ToolRegistry _registry;

	private readonly IndexManager _indexManager;

	public string Name => "sync_index";

	public string Description => "Инкрементальная синхронизация индекса: обновляет только изменённые файлы.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new { }
	});

	internal SyncIndexTool(ToolRegistry registry, IndexManager indexManager)
	{
		_registry = registry;
		_indexManager = indexManager;
	}

	public string Execute(JsonElement args)
	{
		int num;
		int num2;
		int num3;
		try
		{
			ConfigRoots configRoots = _registry.RefreshRoots();
			if (configRoots == null)
			{
				(num, num2, num3) = _indexManager.IncrementalSync();
			}
			else
			{
				Console.Error.WriteLine("MCP_LOG: extension names changed — reinitializing roots");
				Console.Error.Flush();
				(num, num2, num3) = _registry.Reinitialize(configRoots);
			}
		}
		catch (IndexBusyException ex)
		{
			return JsonSerializer.Serialize(new
			{
				status = "busy",
				message = ex.Message
			});
		}
		return JsonSerializer.Serialize(new
		{
			added = num,
			updated = num2,
			deleted = num3,
			message = ((num == 0 && num2 == 0 && num3 == 0) ? "Индекс актуален, изменений не найдено" : $"Синхронизация завершена: {num} добавлено, {num2} обновлено, {num3} удалено")
		});
	}
}
