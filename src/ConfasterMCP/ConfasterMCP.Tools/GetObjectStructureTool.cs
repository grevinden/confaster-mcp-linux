using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class GetObjectStructureTool : IConfasterMCPTool
{
	private sealed record ObjectRow(long Id, string Config, string Fqn, string TypeFolder, string Name, bool Adopted);

	private readonly Database _db;

	private readonly FileScanner _fileScanner;

	public string Name => "get_object_structure";

	public string Description => "Получить полную структуру объекта конфигурации 1С: реквизиты, табличные части, формы, команды, модули. Если объект заимствован расширением, в блоке extensions — что расширение добавило и модули расширения.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			@object = new
			{
				type = "string",
				description = "Имя объекта или полный идентификатор (например: ПриходнаяНакладная или Document.ПриходнаяНакладная)"
			},
			configuration = new
			{
				type = "string",
				description = "Конфигурация, которая интересует в первую очередь: имя расширения или 'основная'. Полная структура объекта всегда наверху, данные выбранного расширения — первыми в extensions."
			}
		},
		required = new string[1] { "object" }
	});

	internal GetObjectStructureTool(Database db, FileScanner fileScanner)
	{
		_db = db;
		_fileScanner = fileScanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("object", out value) ? (value.GetString() ?? "") : "");
		JsonElement value2;
		string name = (args.TryGetProperty("configuration", out value2) ? value2.GetString() : null);
		if (string.IsNullOrEmpty(text))
		{
			return "{\"error\": \"object is required\"}";
		}
		ConfigRoots roots = _fileScanner.Roots;
		if (roots.IsUnknownName(name, out ConfigRoot preferred))
		{
			return ToolJson.UnknownConfiguration(roots, name);
		}
		List<ObjectRow> list = FindObjects(text);
		if (list.Count == 0)
		{
			return JsonSerializer.Serialize(new
			{
				error = "Объект не найден: " + text
			});
		}
		ObjectRow objectRow = (from x in list
			orderby x.Adopted ? 1 : 0, (!IsPreferred(x)) ? 1 : 0, (x.Config.Length != 0) ? 1 : 0
			select x).First();
		Dictionary<string, object> dictionary = new Dictionary<string, object>
		{
			["fqn"] = objectRow.Fqn,
			["type"] = objectRow.TypeFolder,
			["name"] = objectRow.Name
		};
		if (roots.HasExtensions)
		{
			dictionary["configuration"] = ConfigRoot.LabelOfId(objectRow.Config);
		}
		if (objectRow.Adopted)
		{
			dictionary["adopted"] = true;
		}
		string key;
		object value3;
		foreach (KeyValuePair<string, object> item in CollectItems(objectRow.Id, onlyOwn: false))
		{
			item.Deconstruct(out key, out value3);
			string key2 = key;
			object value4 = value3;
			dictionary[key2] = value4;
		}
		List<object> list2 = new List<object>();
		foreach (ObjectRow item2 in from x in LoadByFqn(objectRow.Fqn)
			orderby (!IsPreferred(x)) ? 1 : 0
			select x)
		{
			if (item2.Id == objectRow.Id)
			{
				continue;
			}
			Dictionary<string, object> dictionary2 = CollectItems(item2.Id, item2.Adopted);
			Dictionary<string, object> dictionary3 = new Dictionary<string, object>
			{
				["configuration"] = ConfigRoot.LabelOfId(item2.Config),
				["adopted"] = item2.Adopted
			};
			foreach (KeyValuePair<string, object> item3 in dictionary2)
			{
				item3.Deconstruct(out key, out value3);
				string key3 = key;
				object value5 = value3;
				dictionary3[key3] = value5;
			}
			list2.Add(dictionary3);
		}
		if (list2.Count > 0)
		{
			dictionary["extensions"] = list2;
		}
		return ToolJson.Serialize(dictionary);
		bool IsPreferred(ObjectRow x)
		{
			if (preferred != null)
			{
				return string.Equals(x.Config, preferred.Id, StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}
	}

	private Dictionary<string, object> CollectItems(long objectId, bool onlyOwn)
	{
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
		using (Database.OwnedReader ownedReader = _db.Query("SELECT kind, name, parent_name, type_info, adopted FROM object_items WHERE object_id = @o ORDER BY kind, name", ("@o", objectId)))
		{
			while (ownedReader.Read())
			{
				string text = ownedReader.GetString(0);
				string text2 = ownedReader.GetString(1);
				string tabular_section = (ownedReader.IsDBNull(2) ? null : ownedReader.GetString(2));
				string text3 = (ownedReader.IsDBNull(3) ? null : ownedReader.GetString(3));
				bool flag = ownedReader.GetInt32(4) != 0;
				bool flag2;
				switch (text)
				{
				case "module":
				case "form_module":
				case "command_module":
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				bool flag3 = flag2;
				if (!(onlyOwn && flag) || flag3)
				{
					switch (text)
					{
					case "attribute":
						list.Add(new
						{
							name = text2,
							type = text3
						});
						break;
					case "tabular_section":
						list2.Add(new
						{
							name = text2
						});
						break;
					case "ts_attribute":
						list2.Add(new
						{
							name = text2,
							tabular_section = tabular_section
						});
						break;
					case "form":
						list3.Add(text2);
						break;
					case "form_module":
						list4.Add(new
						{
							form = text2,
							path = ConfigRoots.DisplayPath(text3 ?? ""),
							absolute_path = ResolveAbs(text3)
						});
						break;
					case "module":
						list5.Add(new
						{
							type = text2,
							path = ConfigRoots.DisplayPath(text3 ?? ""),
							absolute_path = ResolveAbs(text3)
						});
						break;
					case "command":
						list6.Add(text2);
						break;
					case "command_module":
						list7.Add(new
						{
							command = text2,
							path = ConfigRoots.DisplayPath(text3 ?? ""),
							absolute_path = ResolveAbs(text3)
						});
						break;
					case "dimension":
						list8.Add(text2);
						break;
					case "resource":
						list9.Add(text2);
						break;
					case "template":
						list10.Add(text2);
						break;
					}
				}
			}
		}
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		string text4 = (onlyOwn ? "added_" : "");
		if (list.Count > 0)
		{
			dictionary[text4 + "attributes"] = list;
		}
		if (list2.Count > 0)
		{
			dictionary[text4 + "tabular_sections"] = list2;
		}
		if (list3.Count > 0)
		{
			dictionary["forms"] = list3;
		}
		if (list6.Count > 0)
		{
			dictionary[text4 + "commands"] = list6;
		}
		if (list8.Count > 0)
		{
			dictionary[text4 + "dimensions"] = list8;
		}
		if (list9.Count > 0)
		{
			dictionary[text4 + "resources"] = list9;
		}
		if (list10.Count > 0)
		{
			dictionary["templates"] = list10;
		}
		if (list4.Count > 0)
		{
			dictionary["form_modules"] = list4;
		}
		if (list5.Count > 0)
		{
			dictionary["modules"] = list5;
		}
		if (list7.Count > 0)
		{
			dictionary["command_modules"] = list7;
		}
		return dictionary;
	}

	private string? ResolveAbs(string? key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return null;
		}
		return _fileScanner.ResolveFullPath(key);
	}

	private List<ObjectRow> LoadByFqn(string fqn)
	{
		List<ObjectRow> list = new List<ObjectRow>();
		using Database.OwnedReader ownedReader = _db.Query("SELECT id, config, fqn, type_folder, name, adopted FROM objects WHERE fqn = @f ORDER BY config", ("@f", fqn));
		ConfigRoots roots = _fileScanner.Roots;
		while (ownedReader.Read())
		{
			string text = ownedReader.GetString(1);
			if (roots.IsKnownConfig(text))
			{
				list.Add(new ObjectRow(ownedReader.GetInt64(0), text, ownedReader.GetString(2), ownedReader.GetString(3), ownedReader.GetString(4), ownedReader.GetInt32(5) != 0));
			}
		}
		return list;
	}

	private List<ObjectRow> FindObjects(string objectName)
	{
		string text = FindObjectFqn(objectName);
		if (text != null)
		{
			return LoadByFqn(text);
		}
		return new List<ObjectRow>();
	}

	private string? FindObjectFqn(string objectName)
	{
		(string, List<(string, object)>) tuple = _fileScanner.Roots.KnownConfigsSql();
		string item = tuple.Item1;
		List<(string, object)> knownParams = tuple.Item2;
		string text = " AND config IN " + item;
		using (Database.OwnedReader ownedReader = _db.Query("SELECT fqn FROM objects WHERE fqn = @n" + text + " LIMIT 1", P(new(string, object)[1] { ("@n", objectName) })))
		{
			if (ownedReader.Read())
			{
				return ownedReader.GetString(0);
			}
		}
		string text2 = objectName.ToLowerInvariant();
		int num = objectName.IndexOf('.');
		if (num > 0)
		{
			string text3 = objectName.Substring(0, num);
			string item2 = objectName.Substring(num + 1).ToLowerInvariant();
			string text4 = null;
			string value;
			if (PathHelper.TypeFolderToXmlTag.ContainsKey(text3))
			{
				text4 = text3;
			}
			else if (PathHelper.XmlTagToTypeFolder.TryGetValue(text3, out value))
			{
				text4 = value;
			}
			if (text4 != null)
			{
				using Database.OwnedReader ownedReader2 = _db.Query("SELECT fqn FROM objects WHERE type_folder = @t AND name_lower = @n" + text + " ORDER BY adopted LIMIT 1", P(new(string, object)[2]
				{
					("@t", text4),
					("@n", item2)
				}));
				if (ownedReader2.Read())
				{
					return ownedReader2.GetString(0);
				}
			}
		}
		using (Database.OwnedReader ownedReader3 = _db.Query("SELECT fqn FROM objects WHERE name_lower = @n" + text + " ORDER BY adopted LIMIT 1", P(new(string, object)[1] { ("@n", text2) })))
		{
			if (ownedReader3.Read())
			{
				return ownedReader3.GetString(0);
			}
		}
		using (Database.OwnedReader ownedReader4 = _db.Query("SELECT fqn FROM objects WHERE name_lower LIKE @n" + text + " ORDER BY adopted LIMIT 1", P(new(string, object)[1] { ("@n", "%" + text2 + "%") })))
		{
			if (ownedReader4.Read())
			{
				return ownedReader4.GetString(0);
			}
		}
		return null;
		(string, object)[] P(params (string, object)[] p)
		{
			return p.Concat(knownParams).ToArray();
		}
	}
}
