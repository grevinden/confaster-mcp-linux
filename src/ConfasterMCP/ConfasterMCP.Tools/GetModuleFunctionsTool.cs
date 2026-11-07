using System;
using System.Collections.Generic;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class GetModuleFunctionsTool : IConfasterMCPTool
{
	private readonly Database _db;

	private readonly FileScanner? _fileScanner;

	public string Name => "get_module_functions";

	public string Description => "Список всех процедур и функций модуля BSL. Полезно для ориентации в крупном модуле без поиска по тексту.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			module_path = new
			{
				type = "string",
				description = "Путь к модулю или его имя. Форматы: 'CommonModule.МодульИмя', 'CommonModules/МодульИмя/Ext/Module.bsl', просто 'МодульИмя'"
			},
			limit = new
			{
				type = "integer",
				description = "Максимум результатов (по умолчанию 200)",
				@default = 200
			}
		},
		required = new string[1] { "module_path" }
	});

	internal GetModuleFunctionsTool(Database db, FileScanner? fileScanner = null)
	{
		_db = db;
		_fileScanner = fileScanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("module_path", out value) ? (value.GetString() ?? "") : "");
		int value2 = (args.TryGetProperty("limit", out var value3) ? value3.GetInt32() : 200);
		if (string.IsNullOrEmpty(text))
		{
			return "{\"error\": \"module_path is required\"}";
		}
		value2 = Math.Clamp(value2, 1, 500);
		string item = NormalizeModulePath(text);
		List<object> list = new List<object>();
		using Database.OwnedReader ownedReader = _db.Query("\n                SELECT name, kind, start_line, end_line, is_export, is_async, directive, file_path\n                FROM symbols\n                WHERE file_path LIKE @p\n                ORDER BY start_line\n                LIMIT @l", ("@p", item), ("@l", value2));
		string text2 = null;
		while (ownedReader.Read())
		{
			if (text2 == null)
			{
				text2 = ownedReader.GetString(7);
			}
			list.Add(new
			{
				name = ownedReader.GetString(0),
				kind = ownedReader.GetString(1),
				start_line = ownedReader.GetInt32(2),
				end_line = ownedReader.GetInt32(3),
				is_export = (ownedReader.GetInt32(4) != 0),
				is_async = (ownedReader.GetInt32(5) != 0),
				directive = (ownedReader.IsDBNull(6) ? null : ownedReader.GetString(6))
			});
		}
		string absolute_path = ((text2 == null) ? null : _fileScanner?.ResolveFullPath(text2));
		return JsonSerializer.Serialize(new
		{
			module = text,
			file = ((text2 != null) ? PathHelper.ToDisplayPath(text2) : null),
			absolute_path = absolute_path,
			functions = list,
			total = list.Count
		});
	}

	private static string NormalizeModulePath(string input)
	{
		int num = input.IndexOf('.');
		if (num > 0)
		{
			string text = input.Substring(0, num);
			string text2 = input.Substring(num + 1);
			if (PathHelper.XmlTagToTypeFolder.TryGetValue(text, out string value))
			{
				return value + "/" + text2 + "%";
			}
			if (PathHelper.TypeFolderToXmlTag.ContainsKey(text))
			{
				return text + "/" + text2 + "%";
			}
		}
		string text3 = input.Replace('\\', '/');
		if (text3.Contains('/'))
		{
			if (!text3.EndsWith('%'))
			{
				if (!text3.EndsWith(".bsl", StringComparison.OrdinalIgnoreCase))
				{
					return text3 + "%";
				}
				return text3;
			}
			return text3;
		}
		return "%" + input + "%";
	}
}
