using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class GetModuleFunctionsTool : IConfasterMCPTool
{
	private readonly Database _db;

	private readonly FileScanner _fileScanner;

	private const int MaxModulesWithVersions = 30;

	public string Name => "get_module_functions";

	public string Description => "Список всех процедур и функций модуля BSL. Полезно для ориентации в крупном модуле без поиска по тексту. Если модуль есть и в основной конфигурации, и в расширении (заимствованный объект), версии расширений перечислены в extension_versions.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			module_path = new
			{
				type = "string",
				description = "Путь к модулю или его имя. Форматы: 'CommonModule.МодульИмя', 'CommonModules/МодульИмя/Ext/Module.bsl', absolute_path, просто 'МодульИмя'"
			},
			configuration = new
			{
				type = "string",
				description = "Только модуль одной конфигурации: имя расширения или 'основная'"
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

	internal GetModuleFunctionsTool(Database db, FileScanner fileScanner)
	{
		_db = db;
		_fileScanner = fileScanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("module_path", out value) ? (value.GetString() ?? "") : "");
		JsonElement value2;
		string name = (args.TryGetProperty("configuration", out value2) ? value2.GetString() : null);
		JsonElement value3;
		int limit = (args.TryGetProperty("limit", out value3) ? value3.GetInt32() : 200);
		if (string.IsNullOrEmpty(text))
		{
			return "{\"error\": \"module_path is required\"}";
		}
		limit = Math.Clamp(limit, 1, 500);
		ConfigRoots roots = _fileScanner.Roots;
		if (roots.IsUnknownName(name, out ConfigRoot root))
		{
			return ToolJson.UnknownConfiguration(roots, name);
		}
		Dictionary<string, List<object>> byFile = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> truncatedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> order = new List<string>();
		bool flag = false;
		string text2 = roots.KnownKeysSql("file_path");
		if (Path.IsPathRooted(text))
		{
			string text3 = roots.KeyOfFullPath(text);
			if (text3 != null)
			{
				string rel = ConfigRoots.RelativeOf(text3);
				List<string> list = ((root != null) ? new List<string> { roots.ToKey(root, rel) } : roots.All.Select((ConfigRoot r) => roots.ToKey(r, rel)).Distinct<string>(StringComparer.Ordinal).ToList());
				if (root == null && !list.Contains(text3))
				{
					list.Add(text3);
				}
				ReadExactKeys(list);
				goto IL_04f2;
			}
		}
		string item = NormalizeModulePath(text);
		string value4 = ((root != null) ? roots.RootLike("file_path", "@p", root) : ConfigRoots.AnyRootLike("file_path", "@p")) + " AND " + text2;
		int num = 2 * limit + 2;
		if (root == null || root.IsMain)
		{
			flag |= ReadRows($"SELECT {"name, kind, start_line, end_line, is_export, is_async, directive, file_path, annotation, annotation_target"} FROM symbols WHERE {value4} AND file_path NOT LIKE '@%' ORDER BY file_path, start_line LIMIT @l", new(string, object)[2]
			{
				("@p", item),
				("@l", num)
			}) >= num;
		}
		if (order.Count == 0 && (root == null || !root.IsMain))
		{
			flag |= ReadRows($"SELECT {"name, kind, start_line, end_line, is_export, is_async, directive, file_path, annotation, annotation_target"} FROM symbols WHERE {value4} AND file_path LIKE '@%' ORDER BY file_path, start_line LIMIT @l", new(string, object)[2]
			{
				("@p", item),
				("@l", num)
			}) >= num;
		}
		else if (order.Count > 0 && root == null && roots.HasExtensions)
		{
			List<string> list2 = order.Select(ConfigRoots.RelativeOf).Distinct<string>(StringComparer.OrdinalIgnoreCase).Take(30)
				.SelectMany((string relativePath) => roots.Extensions.Select((ConfigRoot r) => roots.ToKey(r, relativePath)))
				.ToList();
			ReadExactKeys(list2);
			string value5 = string.Join(", ", list2.Select((string _, int i) => "@k" + i));
			List<(string, object)> source = list2.Select((string k, int i) => ((string, object))("@k" + i, k)).ToList();
			int num2 = limit + 1;
			flag |= ReadRows($"SELECT {"name, kind, start_line, end_line, is_export, is_async, directive, file_path, annotation, annotation_target"} FROM symbols WHERE {value4} AND file_path LIKE '@%' AND file_path NOT IN ({value5}) ORDER BY file_path, start_line LIMIT @l", source.Append(("@p", item)).Append(("@l", num2)).ToArray()) >= num2;
		}
		goto IL_04f2;
		IL_04f2:
		if (order.Count == 0)
		{
			return ToolJson.Serialize(new
			{
				module = text,
				functions = Array.Empty<object>(),
				total = 0
			});
		}
		order = order.OrderBy((string k) => ConfigRoots.IsExtensionKey(k) ? 1 : 0).ToList();
		string text4 = order[0];
		string firstRel = ConfigRoots.RelativeOf(text4);
		List<string> sameModule = (from k in order.Skip(1)
			where string.Equals(ConfigRoots.RelativeOf(k), firstRel, StringComparison.OrdinalIgnoreCase)
			select k).ToList();
		Dictionary<string, List<string>> dictionary = (from k in order.Where(ConfigRoots.IsExtensionKey)
			where !sameModule.Contains(k)
			select k).GroupBy<string, string>(ConfigRoots.RelativeOf, StringComparer.OrdinalIgnoreCase).ToDictionary<IGrouping<string, string>, string, List<string>>((IGrouping<string, string> g) => g.Key, (IGrouping<string, string> g) => g.ToList(), StringComparer.OrdinalIgnoreCase);
		HashSet<string> hashSet = new HashSet<string>(order.Where((string k) => !ConfigRoots.IsExtensionKey(k)).Select(ConfigRoots.RelativeOf), StringComparer.OrdinalIgnoreCase);
		List<string> list3 = new List<string>();
		int num3 = limit;
		int num4 = 0;
		foreach (string item2 in from k in order.Skip(1)
			where !sameModule.Contains(k)
			select k)
		{
			bool flag2 = ConfigRoots.IsExtensionKey(item2);
			string text5 = ConfigRoots.RelativeOf(item2);
			if (flag2 && hashSet.Contains(text5))
			{
				continue;
			}
			if (num3 <= 0)
			{
				num4++;
				continue;
			}
			if (byFile[item2].Count > num3)
			{
				byFile[item2] = byFile[item2].Take(num3).ToList();
				truncatedFiles.Add(item2);
			}
			num3 -= byFile[item2].Count;
			list3.Add(item2);
			if (!flag2 && dictionary.TryGetValue(text5, out var value6))
			{
				list3.AddRange(value6);
			}
		}
		List<object> list4 = sameModule.Select(Block).ToList();
		List<object> list5 = list3.Select(Block).ToList();
		return ToolJson.Serialize(new
		{
			module = text,
			file = ConfigRoots.DisplayPath(text4),
			configuration = roots.LabelOf(text4),
			absolute_path = _fileScanner.ResolveFullPath(text4),
			functions = byFile[text4],
			truncated = (truncatedFiles.Contains(text4) ? new bool?(true) : ((bool?)null)),
			extension_versions = ((list4.Count > 0) ? list4 : null),
			other_modules = ((list5.Count > 0) ? list5 : null),
			other_modules_omitted = ((num4 > 0) ? new int?(num4) : ((int?)null)),
			truncated_note = ((truncatedFiles.Count > 0 || num4 > 0 || flag) ? ($"Показано не более {limit} методов на модуль" + (flag ? ", под маску попали и другие модули — они не показаны" : "") + "; для полного списка увеличьте limit (до 500) или укажите модуль точнее.") : null),
			total = byFile[text4].Count + sameModule.Sum((string k) => byFile[k].Count) + list3.Sum((string k) => byFile[k].Count)
		});
		object Block(string k)
		{
			return new
			{
				configuration = roots.LabelOf(k),
				file = ConfigRoots.DisplayPath(k),
				absolute_path = _fileScanner.ResolveFullPath(k),
				functions = byFile[k],
				truncated = (truncatedFiles.Contains(k) ? new bool?(true) : ((bool?)null))
			};
		}
		void ReadExactKeys(List<string> keys)
		{
			foreach (string key in keys)
			{
				ReadRows("SELECT name, kind, start_line, end_line, is_export, is_async, directive, file_path, annotation, annotation_target FROM symbols WHERE file_path = @k ORDER BY start_line LIMIT @l", new(string, object)[2]
				{
					("@k", key),
					("@l", limit + 1)
				});
			}
		}
		int ReadRows(string sql, params (string, object)[] parameters)
		{
			int num5 = 0;
			using Database.OwnedReader ownedReader = _db.Query(sql, parameters);
			while (ownedReader.Read())
			{
				num5++;
				string text6 = ownedReader.GetString(7);
				if (!byFile.TryGetValue(text6, out List<object> value7))
				{
					value7 = new List<object>();
					byFile[text6] = value7;
					order.Add(text6);
				}
				if (value7.Count >= limit)
				{
					truncatedFiles.Add(text6);
				}
				else
				{
					string annotation = (ownedReader.IsDBNull(8) ? null : ownedReader.GetString(8));
					string intercepts = (ownedReader.IsDBNull(9) ? null : ownedReader.GetString(9));
					value7.Add(new
					{
						name = ownedReader.GetString(0),
						kind = ownedReader.GetString(1),
						start_line = ownedReader.GetInt32(2),
						end_line = ownedReader.GetInt32(3),
						is_export = (ownedReader.GetInt32(4) != 0),
						is_async = (ownedReader.GetInt32(5) != 0),
						directive = (ownedReader.IsDBNull(6) ? null : ownedReader.GetString(6)),
						annotation = annotation,
						intercepts = intercepts
					});
				}
			}
			return num5;
		}
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
				return value + "/" + text2 + "/%";
			}
			if (PathHelper.TypeFolderToXmlTag.ContainsKey(text))
			{
				return text + "/" + text2 + "/%";
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
