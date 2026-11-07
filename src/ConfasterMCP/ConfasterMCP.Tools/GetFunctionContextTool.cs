using System.Collections.Generic;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class GetFunctionContextTool : IConfasterMCPTool
{
	private readonly Database _db;

	private readonly FileScanner? _fileScanner;

	public string Name => "get_function_context";

	public string Description => "Граф вызовов функции или процедуры: что она вызывает и кто её вызывает. Помогает понять зависимости и контекст использования.";

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

	internal GetFunctionContextTool(Database db, FileScanner? fileScanner = null)
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
		List<object> list = new List<object>();
		using (Database.OwnedReader ownedReader = _db.Query("SELECT file_path, name, kind, start_line, end_line FROM symbols WHERE name_lower = @n", ("@n", item)))
		{
			while (ownedReader.Read())
			{
				string relativePath = ownedReader.GetString(0);
				list.Add(new
				{
					file = PathHelper.ToDisplayPath(relativePath),
					absolute_path = _fileScanner?.ResolveFullPath(relativePath),
					name = ownedReader.GetString(1),
					kind = ownedReader.GetString(2),
					start_line = ownedReader.GetInt32(3),
					end_line = ownedReader.GetInt32(4)
				});
			}
		}
		List<object> list2 = new List<object>();
		using (Database.OwnedReader ownedReader2 = _db.Query("\n                SELECT DISTINCT c.callee_name\n                FROM calls c\n                JOIN symbols s ON c.caller_id = s.id\n                WHERE s.name_lower = @n", ("@n", item)))
		{
			while (ownedReader2.Read())
			{
				list2.Add(new
				{
					name = ownedReader2.GetString(0)
				});
			}
		}
		List<object> list3 = new List<object>();
		using (Database.OwnedReader ownedReader3 = _db.Query("\n                SELECT DISTINCT s.name, s.file_path, c.line_number\n                FROM calls c\n                JOIN symbols s ON c.caller_id = s.id\n                WHERE c.callee_name_lower = @n\n                LIMIT 50", ("@n", item)))
		{
			while (ownedReader3.Read())
			{
				string relativePath2 = ownedReader3.GetString(1);
				list3.Add(new
				{
					name = ownedReader3.GetString(0),
					file = PathHelper.ToDisplayPath(relativePath2),
					absolute_path = _fileScanner?.ResolveFullPath(relativePath2),
					line = ownedReader3.GetInt32(2)
				});
			}
		}
		return JsonSerializer.Serialize(new
		{
			function_name = text,
			definitions = list,
			callees = list2,
			callers = list3,
			callees_count = list2.Count,
			callers_count = list3.Count
		});
	}
}
