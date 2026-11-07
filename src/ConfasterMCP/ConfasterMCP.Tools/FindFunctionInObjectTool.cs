using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class FindFunctionInObjectTool : IConfasterMCPTool
{
	private readonly Database _db;

	private readonly FileCache _fileCache;

	private readonly FileScanner _fileScanner;

	public string Name => "find_function_in_object";

	public string Description => "Найти функцию/процедуру внутри конкретного объекта 1С (во всех его модулях — и в основной конфигурации, и в расширениях, заимствовавших объект). Возвращает список функций объекта + код лучшего совпадения.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			@object = new
			{
				type = "string",
				description = "Имя объекта: 'Catalog.СтавкиНДС', 'CommonModule.УчетНДС', 'Document.ЗаказПокупателя'"
			},
			function_hint = new
			{
				type = "string",
				description = "Ключевое слово для фильтрации функций. Если не указано — возвращает все функции объекта."
			},
			configuration = new
			{
				type = "string",
				description = "Только одна конфигурация: имя расширения или 'основная'"
			}
		},
		required = new string[1] { "object" }
	});

	internal FindFunctionInObjectTool(Database db, FileCache fileCache, FileScanner fileScanner)
	{
		_db = db;
		_fileCache = fileCache;
		_fileScanner = fileScanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("object", out value) ? (value.GetString() ?? "") : "");
		JsonElement value2;
		string hint = (args.TryGetProperty("function_hint", out value2) ? value2.GetString() : null);
		JsonElement value3;
		string name = (args.TryGetProperty("configuration", out value3) ? value3.GetString() : null);
		if (string.IsNullOrEmpty(text))
		{
			return "{\"error\": \"object is required\"}";
		}
		ConfigRoots roots = _fileScanner.Roots;
		if (roots.IsUnknownName(name, out ConfigRoot root))
		{
			return ToolJson.UnknownConfiguration(roots, name);
		}
		string text2 = ResolveObjectPath(text);
		string text3 = ((root != null) ? roots.RootLike("file_path", "@p", root) : ConfigRoots.AnyRootLike("file_path", "@p")) + " AND " + roots.KnownKeysSql("file_path");
		List<SymbolResult> list = new List<SymbolResult>();
		string text4 = "\n                SELECT id, file_path, name, kind, start_line, end_line, is_export, is_async, directive, annotation, annotation_target\n                FROM symbols\n                WHERE " + text3;
		List<(string, object)> list2 = new List<(string, object)> { ("@p", text2 + "%") };
		if (!string.IsNullOrEmpty(hint))
		{
			text4 += " AND name_lower LIKE @h";
			list2.Add(("@h", "%" + hint.ToLowerInvariant() + "%"));
		}
		text4 += " ORDER BY (file_path LIKE '@%'), file_path, start_line LIMIT 200";
		using (Database.OwnedReader ownedReader = _db.Query(text4, list2.ToArray()))
		{
			while (ownedReader.Read())
			{
				list.Add(FindSymbolTool.ReadSymbol(_fileScanner, ownedReader));
			}
		}
		ExtensionLinks.FillInterceptors(_db, roots, list);
		string best_match_code = null;
		if (list.Count > 0 && !string.IsNullOrEmpty(hint))
		{
			SymbolResult symbol = (from f in list
				orderby f.name.Equals(hint, StringComparison.OrdinalIgnoreCase) ? 1 : 0 descending, ConfigRoots.IsExtensionKey(f.Key) ? 1 : 0, f.is_export ? 1 : 0 descending
				select f).First();
			best_match_code = ReadCode(symbol);
		}
		return ToolJson.Serialize(new
		{
			@object = text,
			functions = list,
			total = list.Count,
			best_match_code = best_match_code
		});
	}

	private static string ResolveObjectPath(string objectName)
	{
		int num = objectName.IndexOf('.');
		if (num > 0)
		{
			string text = objectName.Substring(0, num);
			string text2 = objectName.Substring(num + 1);
			if (PathHelper.XmlTagToTypeFolder.TryGetValue(text, out string value))
			{
				return value + "/" + text2 + "/";
			}
			if (PathHelper.TypeFolderToXmlTag.ContainsKey(text))
			{
				return text + "/" + text2 + "/";
			}
		}
		return "%/" + objectName + "/";
	}

	private string? ReadCode(SymbolResult symbol)
	{
		try
		{
			string text = _fileScanner.ResolveFullPath(symbol.Key);
			if (text == null)
			{
				return null;
			}
			string[] lines = _fileCache.GetLines(text);
			int num = Math.Max(0, symbol.start_line - 1);
			int num2 = Math.Min(lines.Length, symbol.end_line);
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = num; i < num2; i++)
			{
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, stringBuilder2);
				handler.AppendFormatted(i + 1, 6);
				handler.AppendLiteral(": ");
				handler.AppendFormatted(lines[i]);
				stringBuilder2.AppendLine(ref handler);
			}
			return stringBuilder.ToString();
		}
		catch
		{
			return null;
		}
	}
}
