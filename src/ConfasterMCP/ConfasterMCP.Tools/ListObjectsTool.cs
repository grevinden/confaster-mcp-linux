using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class ListObjectsTool : IConfasterMCPTool
{
	private readonly Database _db;

	public string Name => "list_objects";

	public string Description => "Список объектов конфигурации 1С (справочники, документы, общие модули и т.д.).";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			type = new
			{
				type = "string",
				description = "Фильтр по типу: Catalog, Document, CommonModule, InformationRegister и т.д."
			},
			name_filter = new
			{
				type = "string",
				description = "Фильтр по части имени объекта (или бизнес-имени/синониму)"
			},
			limit = new
			{
				type = "integer",
				description = "Максимум результатов (по умолчанию 100, максимум 500)",
				@default = 100
			}
		}
	});

	internal ListObjectsTool(Database db)
	{
		_db = db;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("type", out value) ? value.GetString() : null);
		JsonElement value2;
		string text2 = (args.TryGetProperty("name_filter", out value2) ? value2.GetString() : null);
		int value3 = (args.TryGetProperty("limit", out var value4) ? value4.GetInt32() : 100);
		value3 = Math.Clamp(value3, 1, 500);
		string text3 = null;
		if (!string.IsNullOrEmpty(text))
		{
			string value5;
			if (PathHelper.TypeFolderToXmlTag.ContainsKey(text))
			{
				text3 = text;
			}
			else if (PathHelper.XmlTagToTypeFolder.TryGetValue(text, out value5))
			{
				text3 = value5;
			}
		}
		List<object> list = new List<object>();
		if (!string.IsNullOrEmpty(text2))
		{
			string text4 = text2.ToLowerInvariant();
			string text5 = "SELECT fqn, type_folder, name FROM objects WHERE name_lower LIKE @f";
			if (text3 != null)
			{
				text5 += " AND type_folder = @t";
			}
			text5 += " ORDER BY type_folder, name LIMIT @l";
			List<(string, object)> list2 = new List<(string, object)>
			{
				("@f", "%" + text4 + "%"),
				("@l", value3)
			};
			if (text3 != null)
			{
				list2.Add(("@t", text3));
			}
			using Database.OwnedReader ownedReader = _db.Query(text5, list2.ToArray());
			while (ownedReader.Read())
			{
				list.Add(new
				{
					fqn = ownedReader.GetString(0),
					type = ownedReader.GetString(1),
					name = ownedReader.GetString(2)
				});
			}
			if (list.Count < value3 && text2.Length >= 3)
			{
				try
				{
					HashSet<string> hashSet = new HashSet<string>(list.Select((object o) => (((dynamic)o).fqn as string) ?? ""));
					string item = "\"" + text2.Replace("\"", "\"\"") + "\"";
					string text6 = "SELECT o.fqn, o.type_folder, o.name\n                            FROM synonyms_fts sf\n                            JOIN object_synonyms os ON sf.rowid = os.id\n                            JOIN objects o ON os.object_fqn = o.fqn\n                            WHERE synonyms_fts MATCH @q";
					if (text3 != null)
					{
						text6 += " AND o.type_folder = @t";
					}
					text6 += " LIMIT @l";
					List<(string, object)> list3 = new List<(string, object)>
					{
						("@q", item),
						("@l", value3 - list.Count)
					};
					if (text3 != null)
					{
						list3.Add(("@t", text3));
					}
					using Database.OwnedReader ownedReader2 = _db.Query(text6, list3.ToArray());
					while (ownedReader2.Read())
					{
						string text7 = ownedReader2.GetString(0);
						if (!hashSet.Contains(text7))
						{
							list.Add(new
							{
								fqn = text7,
								type = ownedReader2.GetString(1),
								name = ownedReader2.GetString(2)
							});
						}
					}
				}
				catch
				{
				}
			}
		}
		else
		{
			string text8 = "SELECT fqn, type_folder, name FROM objects";
			if (text3 != null)
			{
				text8 += " WHERE type_folder = @t";
			}
			text8 += " ORDER BY type_folder, name LIMIT @l";
			List<(string, object)> list4 = new List<(string, object)> { ("@l", value3) };
			if (text3 != null)
			{
				list4.Add(("@t", text3));
			}
			using Database.OwnedReader ownedReader3 = _db.Query(text8, list4.ToArray());
			while (ownedReader3.Read())
			{
				list.Add(new
				{
					fqn = ownedReader3.GetString(0),
					type = ownedReader3.GetString(1),
					name = ownedReader3.GetString(2)
				});
			}
		}
		return JsonSerializer.Serialize(new
		{
			objects = list,
			total = list.Count
		});
	}
}
