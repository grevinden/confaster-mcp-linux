using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class SmartFindTool : IConfasterMCPTool
{
	private readonly Database _db;

	private readonly string _exportRoot;

	private readonly FileCache _fileCache;

	private readonly FileScanner _fileScanner;

	public string Name => "smart_find";

	public string Description => "Умный поиск функции/процедуры: находит символ и возвращает полный код за один вызов. Используй ВМЕСТО search_code когда знаешь имя функции.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			query = new
			{
				type = "string",
				description = "Имя функции или процедуры (полное или частичное)"
			},
			include_code = new
			{
				type = "boolean",
				description = "Включить полный код лучшего совпадения",
				@default = true
			}
		},
		required = new string[1] { "query" }
	});

	internal SmartFindTool(Database db, string exportRoot, FileCache fileCache, FileScanner fileScanner)
	{
		_db = db;
		_exportRoot = exportRoot;
		_fileCache = fileCache;
		_fileScanner = fileScanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string query = (args.TryGetProperty("query", out value) ? (value.GetString() ?? "") : "");
		JsonElement value2;
		bool flag = !args.TryGetProperty("include_code", out value2) || value2.GetBoolean();
		if (string.IsNullOrEmpty(query))
		{
			return "{\"error\": \"query is required\"}";
		}
		FindSymbolTool findSymbolTool = new FindSymbolTool(_db, _fileScanner);
		List<SymbolResult> list = findSymbolTool.FindSymbols(query, exact: true, 5);
		if (list.Count == 0)
		{
			list = findSymbolTool.FindSymbols(query, exact: false, 10);
		}
		if (list.Count == 0)
		{
			return JsonSerializer.Serialize(new
			{
				symbols = Array.Empty<object>(),
				code = (string)null,
				message = "Символ не найден"
			});
		}
		SymbolResult symbolResult = (from s in list
			orderby s.name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 1 : 0 descending, s.is_export ? 1 : 0 descending
			select s).First();
		string code = null;
		if (flag)
		{
			code = ReadSymbolCode(symbolResult);
		}
		return JsonSerializer.Serialize(new
		{
			symbols = list,
			best = new { symbolResult.file, symbolResult.absolute_path, symbolResult.name, symbolResult.kind, symbolResult.start_line, symbolResult.end_line },
			code = code
		});
	}

	private string? ReadSymbolCode(SymbolResult symbol)
	{
		try
		{
			string item = symbol.name.ToLowerInvariant();
			using Database.OwnedReader ownedReader = _db.Query("SELECT file_path, start_line, end_line FROM symbols WHERE name_lower = @n AND start_line = @sl LIMIT 1", ("@n", item), ("@sl", symbol.start_line));
			if (!ownedReader.Read())
			{
				return null;
			}
			string relativePath = ownedReader.GetString(0);
			int @int = ownedReader.GetInt32(1);
			int int2 = ownedReader.GetInt32(2);
			string text = _fileScanner.ResolveFullPath(relativePath);
			if (text == null)
			{
				return null;
			}
			string[] lines = _fileCache.GetLines(text);
			int num = Math.Max(0, @int - 1);
			int num2 = Math.Min(lines.Length, int2);
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
