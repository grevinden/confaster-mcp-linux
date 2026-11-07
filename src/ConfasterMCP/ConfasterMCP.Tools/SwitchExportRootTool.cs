using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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
				description = "Пути к папкам выгрузки расширений",
				items = new
				{
					type = "string"
				}
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
		List<string> list = null;
		if (args.TryGetProperty("extension_roots", out var value2) && value2.ValueKind == JsonValueKind.Array)
		{
			list = new List<string>();
			foreach (JsonElement item in value2.EnumerateArray())
			{
				string text2 = item.GetString();
				if (!string.IsNullOrWhiteSpace(text2) && Directory.Exists(text2))
				{
					list.Add(text2);
				}
			}
			if (list.Count == 0)
			{
				list = null;
			}
		}
		if (string.Equals(_registry.ExportRoot, text, StringComparison.OrdinalIgnoreCase) && ExtensionsEqual(_registry.ExtensionRoots, list))
		{
			return JsonSerializer.Serialize(new
			{
				status = "unchanged",
				export_root = text,
				message = "Уже подключен к этой папке"
			});
		}
		Console.Error.WriteLine("SEARCH_STATUS:switching to " + text);
		var (num, num2, num3) = _registry.Reinitialize(text, list);
		Console.Error.WriteLine($"SEARCH_STATUS:ready:switched, indexed {num} new, {num2} updated, {num3} deleted");
		return JsonSerializer.Serialize(new
		{
			status = "switched",
			export_root = text,
			extension_roots_count = (list?.Count ?? 0),
			added = num,
			updated = num2,
			deleted = num3
		});
	}

	private static bool ExtensionsEqual(List<string>? a, List<string>? b)
	{
		if ((a == null || a.Count == 0) && (b == null || b.Count == 0))
		{
			return true;
		}
		if (a == null || b == null)
		{
			return false;
		}
		if (a.Count != b.Count)
		{
			return false;
		}
		for (int i = 0; i < a.Count; i++)
		{
			if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		return true;
	}
}
