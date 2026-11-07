using System.Collections.Generic;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class GetObjectStructureTool : IConfasterMCPTool
{
	private readonly Database _db;

	private readonly FileScanner? _fileScanner;

	public string Name => "get_object_structure";

	public string Description => "Получить полную структуру объекта конфигурации 1С: реквизиты, табличные части, формы, команды, модули.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			@object = new
			{
				type = "string",
				description = "Имя объекта или полный идентификатор (например: ПриходнаяНакладная или Document.ПриходнаяНакладная)"
			}
		},
		required = new string[1] { "object" }
	});

	internal GetObjectStructureTool(Database db, FileScanner? fileScanner = null)
	{
		_db = db;
		_fileScanner = fileScanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("object", out value) ? (value.GetString() ?? "") : "");
		if (string.IsNullOrEmpty(text))
		{
			return "{\"error\": \"object is required\"}";
		}
		string text2 = FindObjectFqn(text);
		if (text2 == null)
		{
			return JsonSerializer.Serialize(new
			{
				error = "Объект не найден: " + text
			});
		}
		List<object> list = new List<object>();
		List<object> list2 = new List<object>();
		List<string> list3 = new List<string>();
		List<object> list4 = new List<object>();
		List<object> list5 = new List<object>();
		List<string> list6 = new List<string>();
		List<object> list7 = new List<object>();
		List<string> list8 = new List<string>();
		List<string> list9 = new List<string>();
		List<string> list10 = new List<string>();
		string value2 = "";
		string value3 = "";
		using (Database.OwnedReader ownedReader = _db.Query("SELECT type_folder, name FROM objects WHERE fqn = @f", ("@f", text2)))
		{
			if (ownedReader.Read())
			{
				value2 = ownedReader.GetString(0);
				value3 = ownedReader.GetString(1);
			}
		}
		using Database.OwnedReader ownedReader2 = _db.Query("SELECT kind, name, parent_name, type_info FROM object_items WHERE object_fqn = @f ORDER BY kind, name", ("@f", text2));
		while (ownedReader2.Read())
		{
			string text3 = ownedReader2.GetString(0);
			string text4 = ownedReader2.GetString(1);
			string tabular_section = (ownedReader2.IsDBNull(2) ? null : ownedReader2.GetString(2));
			string text5 = (ownedReader2.IsDBNull(3) ? null : ownedReader2.GetString(3));
			switch (text3)
			{
			case "attribute":
				list.Add(new
				{
					name = text4,
					type = text5
				});
				break;
			case "tabular_section":
				list2.Add(new
				{
					name = text4
				});
				break;
			case "ts_attribute":
				list2.Add(new
				{
					name = text4,
					tabular_section = tabular_section
				});
				break;
			case "form":
				list3.Add(text4);
				break;
			case "form_module":
				list4.Add(new
				{
					form = text4,
					path = PathHelper.ToDisplayPath(text5 ?? ""),
					absolute_path = ResolveAbs(text5)
				});
				break;
			case "module":
				list5.Add(new
				{
					type = text4,
					path = PathHelper.ToDisplayPath(text5 ?? ""),
					absolute_path = ResolveAbs(text5)
				});
				break;
			case "command":
				list6.Add(text4);
				break;
			case "command_module":
				list7.Add(new
				{
					command = text4,
					path = PathHelper.ToDisplayPath(text5 ?? ""),
					absolute_path = ResolveAbs(text5)
				});
				break;
			case "dimension":
				list8.Add(text4);
				break;
			case "resource":
				list9.Add(text4);
				break;
			case "template":
				list10.Add(text4);
				break;
			}
		}
		Dictionary<string, object> dictionary = new Dictionary<string, object>
		{
			["fqn"] = text2,
			["type"] = value2,
			["name"] = value3
		};
		if (list.Count > 0)
		{
			dictionary["attributes"] = list;
		}
		if (list2.Count > 0)
		{
			dictionary["tabular_sections"] = list2;
		}
		if (list3.Count > 0)
		{
			dictionary["forms"] = list3;
		}
		if (list4.Count > 0)
		{
			dictionary["form_modules"] = list4;
		}
		if (list5.Count > 0)
		{
			dictionary["modules"] = list5;
		}
		if (list6.Count > 0)
		{
			dictionary["commands"] = list6;
		}
		if (list7.Count > 0)
		{
			dictionary["command_modules"] = list7;
		}
		if (list8.Count > 0)
		{
			dictionary["dimensions"] = list8;
		}
		if (list9.Count > 0)
		{
			dictionary["resources"] = list9;
		}
		if (list10.Count > 0)
		{
			dictionary["templates"] = list10;
		}
		return JsonSerializer.Serialize(dictionary);
	}

	private string? ResolveAbs(string? relativePath)
	{
		if (string.IsNullOrEmpty(relativePath))
		{
			return null;
		}
		return _fileScanner?.ResolveFullPath(relativePath);
	}

	private string? FindObjectFqn(string objectName)
	{
		using (Database.OwnedReader ownedReader = _db.Query("SELECT fqn FROM objects WHERE fqn = @n", ("@n", objectName)))
		{
			if (ownedReader.Read())
			{
				return ownedReader.GetString(0);
			}
		}
		string text = objectName.ToLowerInvariant();
		int num = objectName.IndexOf('.');
		if (num > 0)
		{
			string text2 = objectName.Substring(0, num);
			string item = objectName.Substring(num + 1).ToLowerInvariant();
			string text3 = null;
			string value;
			if (PathHelper.TypeFolderToXmlTag.ContainsKey(text2))
			{
				text3 = text2;
			}
			else if (PathHelper.XmlTagToTypeFolder.TryGetValue(text2, out value))
			{
				text3 = value;
			}
			if (text3 != null)
			{
				using Database.OwnedReader ownedReader2 = _db.Query("SELECT fqn FROM objects WHERE type_folder = @t AND name_lower = @n LIMIT 1", ("@t", text3), ("@n", item));
				if (ownedReader2.Read())
				{
					return ownedReader2.GetString(0);
				}
			}
		}
		using (Database.OwnedReader ownedReader3 = _db.Query("SELECT fqn FROM objects WHERE name_lower = @n LIMIT 1", ("@n", text)))
		{
			if (ownedReader3.Read())
			{
				return ownedReader3.GetString(0);
			}
		}
		using (Database.OwnedReader ownedReader4 = _db.Query("SELECT fqn FROM objects WHERE name_lower LIKE @n LIMIT 1", ("@n", "%" + text + "%")))
		{
			if (ownedReader4.Read())
			{
				return ownedReader4.GetString(0);
			}
		}
		return null;
	}
}
