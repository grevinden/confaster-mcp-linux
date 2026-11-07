using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class GetFunctionContextTool : IConfasterMCPTool
{
	private readonly Database _db;

	private readonly FileScanner _fileScanner;

	public string Name => "get_function_context";

	public string Description => "Граф вызовов функции или процедуры: что она вызывает и кто её вызывает. Помогает понять зависимости и контекст использования. Показывает и методы расширений, перехватывающие функцию (&Перед/&После/&Вместо).";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			function_name = new
			{
				type = "string",
				description = "Имя функции или процедуры"
			}
		},
		required = new string[1] { "function_name" }
	});

	internal GetFunctionContextTool(Database db, FileScanner fileScanner)
	{
		_db = db;
		_fileScanner = fileScanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("function_name", out value) ? (value.GetString() ?? "") : "");
		if (string.IsNullOrEmpty(text))
		{
			return "{\"error\": \"function_name is required\"}";
		}
		string item = text.ToLowerInvariant();
		ConfigRoots roots = _fileScanner.Roots;
		List<SymbolResult> list = new List<SymbolResult>();
		using (Database.OwnedReader ownedReader = _db.Query("SELECT id, file_path, name, kind, start_line, end_line, is_export, is_async, directive, annotation, annotation_target FROM symbols WHERE name_lower = @n AND " + roots.KnownKeysSql("file_path"), ("@n", item)))
		{
			while (ownedReader.Read())
			{
				list.Add(FindSymbolTool.ReadSymbol(_fileScanner, ownedReader));
			}
		}
		HashSet<string> definitionModules = new HashSet<string>(from d in list
			where !ConfigRoots.IsExtensionKey(d.Key)
			select d.Key, StringComparer.OrdinalIgnoreCase);
		bool interceptorsByNameOnly = roots.HasExtensions && definitionModules.Count == 0;
		List<ExtensionLinks.Interceptor> list2 = ExtensionLinks.FindByTarget(_db, roots, text, (string rel) => interceptorsByNameOnly || definitionModules.Contains(rel), 50);
		List<object> list3 = new List<object>();
		using (Database.OwnedReader ownedReader2 = _db.Query("\n                SELECT DISTINCT c.callee_name\n                FROM calls c\n                JOIN symbols s ON c.caller_id = s.id\n                WHERE s.name_lower = @n AND " + roots.KnownKeysSql("s.file_path"), ("@n", item)))
		{
			while (ownedReader2.Read())
			{
				list3.Add(new
				{
					name = ownedReader2.GetString(0)
				});
			}
		}
		List<object> list4 = new List<object>();
		using (Database.OwnedReader ownedReader3 = _db.Query("\n                SELECT DISTINCT s.name, s.file_path, c.line_number\n                FROM calls c\n                JOIN symbols s ON c.caller_id = s.id\n                WHERE c.callee_name_lower = @n AND " + roots.KnownKeysSql("s.file_path") + "\n                LIMIT 50", ("@n", item)))
		{
			while (ownedReader3.Read())
			{
				string key = ownedReader3.GetString(1);
				list4.Add(new
				{
					name = ownedReader3.GetString(0),
					file = ConfigRoots.DisplayPath(key),
					configuration = roots.LabelOf(key),
					absolute_path = _fileScanner.ResolveFullPath(key),
					line = ownedReader3.GetInt32(2)
				});
			}
		}
		return ToolJson.Serialize(new
		{
			function_name = text,
			definitions = list,
			intercepted_by = ((list2.Count > 0) ? list2 : null),
			intercepted_by_note = ((list2.Count > 0 && interceptorsByNameOnly) ? "Определение метода в основной конфигурации не найдено, перехваты подобраны только по имени — они могут относиться к одноимённым методам других объектов." : null),
			callees = list3,
			callers = list4,
			callees_count = list3.Count,
			callers_count = list4.Count
		});
	}
}
