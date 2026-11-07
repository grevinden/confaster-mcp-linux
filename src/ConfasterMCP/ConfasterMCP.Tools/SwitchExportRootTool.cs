using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class SwitchExportRootTool : IConfasterMCPTool
{
	private readonly ToolRegistry _registry;

	public string Name => "switch_export_root";

	public string Description => "Переключает MCP-сервер на другую папку выгрузки конфигурации 1С. Переинициализирует БД индекса, кеш и все инструменты.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			export_root = new
			{
				type = "string",
				description = "Полный путь к папке выгрузки конфигурации"
			},
			extension_roots = new
			{
				type = "array",
				description = "Выгрузки расширений: путь к папке либо \"ИмяРасширения|путь\"",
				items = new
				{
					type = "string"
				}
			},
			sync = new
			{
				type = "boolean",
				description = "false — только переключиться, индекс синхронизировать отдельным sync_index (по умолчанию true)"
			}
		},
		required = new string[1] { "export_root" }
	});

	internal SwitchExportRootTool(ToolRegistry registry)
	{
		_registry = registry;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("export_root", out value) ? (value.GetString() ?? "") : "");
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new ArgumentException("export_root не указан");
		}
		if (!Directory.Exists(text))
		{
			throw new DirectoryNotFoundException("Папка не существует: " + text);
		}
		List<(string, string)> list = new List<(string, string)>();
		if (args.TryGetProperty("extension_roots", out var value2) && value2.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in value2.EnumerateArray())
			{
				string value3 = item.GetString();
				if (!string.IsNullOrWhiteSpace(value3))
				{
					list.Add(ConfigRoots.ParseExtensionArg(value3));
				}
			}
		}
		ConfigRoots configRoots = ConfigRoots.Build(text, list) ?? throw new DirectoryNotFoundException("Папка не существует: " + text);
		foreach (string warning in configRoots.Warnings)
		{
			Console.Error.WriteLine("MCP_LOG: WARNING: " + warning);
		}
		if (_registry.Roots != null && _registry.Roots.SameSetAs(configRoots))
		{
			_registry.Roots.AdoptBuildWarnings(configRoots);
			return JsonSerializer.Serialize(new
			{
				status = "unchanged",
				export_root = text,
				message = "Уже подключен к этой папке"
			});
		}
		Console.Error.WriteLine("SEARCH_STATUS:switching to " + text);
		JsonElement value4;
		bool flag = !args.TryGetProperty("sync", out value4) || value4.ValueKind != JsonValueKind.False;
		int num;
		int num2;
		int num3;
		try
		{
			(num, num2, num3) = _registry.Reinitialize(configRoots, flag);
		}
		catch (IndexBusyException ex)
		{
			return JsonSerializer.Serialize(new
			{
				status = "switched",
				export_root = text,
				extension_roots_count = configRoots.Extensions.Count(),
				index_status = "busy",
				message = ex.Message
			});
		}
		if (!flag)
		{
			Console.Error.WriteLine("SEARCH_STATUS:switched, index sync pending");
			return JsonSerializer.Serialize(new
			{
				status = "switched",
				export_root = text,
				extension_roots_count = configRoots.Extensions.Count(),
				index_status = "pending"
			});
		}
		Console.Error.WriteLine($"SEARCH_STATUS:ready:switched, indexed {num} new, {num2} updated, {num3} deleted");
		return JsonSerializer.Serialize(new
		{
			status = "switched",
			export_root = text,
			extension_roots_count = configRoots.Extensions.Count(),
			added = num,
			updated = num2,
			deleted = num3
		});
	}
}
