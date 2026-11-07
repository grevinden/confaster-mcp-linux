using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class ListObjectsTool : IConfasterMCPTool
{
	private sealed class ObjectRow
	{
		public string fqn { get; init; } = "";

		public string type { get; init; } = "";

		public string name { get; init; } = "";

		public string? configuration { get; init; }

		public bool? adopted { get; init; }

		public string? extended_in { get; init; }
	}

	private readonly Database _db;

	private readonly ConfigRoots _roots;

	public string Name => "list_objects";

	public string Description => "Список объектов конфигурации 1С (справочники, документы, общие модули и т.д.) — основной конфигурации и подключённых расширений. Объект, заимствованный расширением, показывается один раз с полем extended_in.";

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
			configuration = new
			{
				type = "string",
				description = "Только объекты одной конфигурации: имя расширения или 'основная'"
			},
			limit = new
			{
				type = "integer",
				description = "Максимум результатов (по умолчанию 100, максимум 500)",
				@default = 100
			}
		}
	});

	internal ListObjectsTool(Database db, ConfigRoots roots)
	{
		_db = db;
		_roots = roots;
	}

	private static string VisibleObject(string knownIn)
	{
		return $"(o.config IN {knownIn} AND (o.adopted = 0 OR NOT EXISTS (SELECT 1 FROM objects m WHERE m.fqn = o.fqn AND m.adopted = 0 AND m.config IN {knownIn})))";
	}

	private static string ExtendedIn(string knownIn)
	{
		return "(SELECT group_concat(a.config, ', ') FROM objects a WHERE a.fqn = o.fqn AND a.adopted = 1 AND a.config <> o.config AND a.config IN " + knownIn + ")";
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("type", out value) ? value.GetString() : null);
		JsonElement value2;
		string text2 = (args.TryGetProperty("name_filter", out value2) ? value2.GetString() : null);
		JsonElement value3;
		string name = (args.TryGetProperty("configuration", out value3) ? value3.GetString() : null);
		int value4 = (args.TryGetProperty("limit", out var value5) ? value5.GetInt32() : 100);
		value4 = Math.Clamp(value4, 1, 500);
		string text3 = null;
		if (!string.IsNullOrEmpty(text))
		{
			string value6;
			if (PathHelper.TypeFolderToXmlTag.ContainsKey(text))
			{
				text3 = text;
			}
			else if (PathHelper.XmlTagToTypeFolder.TryGetValue(text, out value6))
			{
				text3 = value6;
			}
		}
		if (_roots.IsUnknownName(name, out ConfigRoot root))
		{
			return ToolJson.UnknownConfiguration(_roots, name);
		}
		List<ObjectRow> list = new List<ObjectRow>();
		HashSet<long> seen = new HashSet<long>();
		(string inList, List<(string, object)> parameters) tuple = _roots.KnownConfigsSql();
		string item = tuple.inList;
		List<(string, object)> item2 = tuple.parameters;
		string text4 = ((root != null) ? "o.config = @c" : VisibleObject(item));
		string value7 = ExtendedIn(item);
		List<(string, object)> list2 = new List<(string, object)> { ("@l", value4) };
		list2.AddRange(item2);
		if (root != null)
		{
			list2.Add(("@c", root.Id));
		}
		if (text3 != null)
		{
			text4 += " AND o.type_folder = @t";
			list2.Add(("@t", text3));
		}
		if (!string.IsNullOrEmpty(text2))
		{
			string text5 = text2.ToLowerInvariant();
			List<(string, object)> parameters = new List<(string, object)>(list2) { ("@f", "%" + text5 + "%") };
			Read($"SELECT o.id, o.config, o.fqn, o.type_folder, o.name, o.adopted, {value7}\n                        FROM objects o WHERE o.name_lower LIKE @f AND {text4}\n                        ORDER BY o.type_folder, o.name LIMIT @l", parameters, list, seen);
			if (list.Count < value4 && text2.Length >= 3)
			{
				try
				{
					string item3 = "\"" + text2.Replace("\"", "\"\"") + "\"";
					List<(string, object)> list3 = new List<(string, object)>(list2) { ("@q", item3) };
					list3[0] = ("@l", value4 - list.Count);
					Read($"SELECT o.id, o.config, o.fqn, o.type_folder, o.name, o.adopted, {value7}\n                                FROM synonyms_fts sf\n                                JOIN object_synonyms os ON sf.rowid = os.id\n                                JOIN objects o ON os.object_id = o.id\n                                WHERE synonyms_fts MATCH @q AND {text4}\n                                LIMIT @l", list3, list, seen);
				}
				catch
				{
				}
			}
		}
		else
		{
			Read($"SELECT o.id, o.config, o.fqn, o.type_folder, o.name, o.adopted, {value7}\n                        FROM objects o WHERE {text4}\n                        ORDER BY o.type_folder, o.name LIMIT @l", list2, list, seen);
		}
		return ToolJson.Serialize(new
		{
			objects = list,
			total = list.Count
		});
	}

	private void Read(string sql, List<(string, object)> parameters, List<ObjectRow> objects, HashSet<long> seen)
	{
		using Database.OwnedReader ownedReader = _db.Query(sql, parameters.ToArray());
		while (ownedReader.Read())
		{
			if (seen.Add(ownedReader.GetInt64(0)))
			{
				string id = ownedReader.GetString(1);
				bool hasExtensions = _roots.HasExtensions;
				objects.Add(new ObjectRow
				{
					fqn = ownedReader.GetString(2),
					type = ownedReader.GetString(3),
					name = ownedReader.GetString(4),
					configuration = (hasExtensions ? ConfigRoot.LabelOfId(id) : null),
					adopted = ((ownedReader.GetInt32(5) != 0) ? new bool?(true) : ((bool?)null)),
					extended_in = (ownedReader.IsDBNull(6) ? null : string.Join(", ", ownedReader.GetString(6).Split(", ").Select(ConfigRoot.LabelOfId)))
				});
			}
		}
	}
}
