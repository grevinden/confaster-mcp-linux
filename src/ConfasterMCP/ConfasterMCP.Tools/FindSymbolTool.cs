using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class FindSymbolTool : IConfasterMCPTool
{
	private readonly Database _db;

	private readonly FileScanner? _fileScanner;

	public string Name => "find_symbol";

	public string Description => "Найти процедуру или функцию по имени в индексе конфигурации 1С. Возвращает файл и номера строк определения.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			query = new
			{
				type = "string",
				description = "Имя процедуры или функции (полное или частичное)"
			},
			exact = new
			{
				type = "boolean",
				description = "Точное совпадение имени",
				@default = false
			},
			limit = new
			{
				type = "integer",
				description = "Максимум результатов (по умолчанию 20)",
				@default = 20
			}
		},
		required = new string[1] { "query" }
	});

	internal FindSymbolTool(Database db, FileScanner? fileScanner = null)
	{
		_db = db;
		_fileScanner = fileScanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("query", out value) ? (value.GetString() ?? "") : "");
		JsonElement value2;
		bool exact = args.TryGetProperty("exact", out value2) && value2.GetBoolean();
		int value3 = (args.TryGetProperty("limit", out var value4) ? value4.GetInt32() : 20);
		if (string.IsNullOrEmpty(text))
		{
			return "{\"error\": \"query is required\"}";
		}
		value3 = Math.Clamp(value3, 1, 100);
		List<SymbolResult> list = FindSymbols(text, exact, value3);
		return JsonSerializer.Serialize(new
		{
			symbols = list,
			total = list.Count
		});
	}

	internal List<SymbolResult> FindSymbols(string query, bool exact, int limit)
	{
		List<SymbolResult> list = new List<SymbolResult>();
		string text = query.ToLowerInvariant();
		if (exact)
		{
			AddResults(list, "SELECT id, file_path, name, kind, start_line, end_line, is_export, is_async, directive FROM symbols WHERE name_lower = @q LIMIT @l", text, limit);
			return list;
		}
		if (query.Length >= 3)
		{
			AddResultsFts(list, EscapeFts5Query(query), limit);
			if (list.Count >= limit)
			{
				return list.Take(limit).ToList();
			}
		}
		if (list.Count < limit)
		{
			AddResults(list, "SELECT id, file_path, name, kind, start_line, end_line, is_export, is_async, directive FROM symbols WHERE name_lower = @q LIMIT @l", text, limit - list.Count);
		}
		if (list.Count < limit)
		{
			AddResults(list, "SELECT id, file_path, name, kind, start_line, end_line, is_export, is_async, directive FROM symbols WHERE name_lower LIKE @q || '%' AND name_lower != @q2 LIMIT @l", text, limit - list.Count, text);
		}
		if (list.Count < limit && query.Length < 3)
		{
			AddResults(list, "SELECT id, file_path, name, kind, start_line, end_line, is_export, is_async, directive FROM symbols WHERE name_lower LIKE '%' || @q || '%' AND name_lower NOT LIKE @q || '%' LIMIT @l", text, limit - list.Count);
		}
		return list;
	}

	private void AddResultsFts(List<SymbolResult> results, string query, int limit)
	{
		HashSet<long> hashSet = new HashSet<long>(results.Select((SymbolResult r) => r.Id));
		try
		{
			using Database.OwnedReader ownedReader = _db.Query("\n                    SELECT s.id, s.file_path, s.name, s.kind, s.start_line, s.end_line, s.is_export, s.is_async, s.directive\n                    FROM symbols_fts fts\n                    JOIN symbols s ON fts.rowid = s.id\n                    WHERE symbols_fts MATCH @q\n                    ORDER BY rank\n                    LIMIT @l", ("@q", query), ("@l", limit));
			while (ownedReader.Read())
			{
				long @int = ownedReader.GetInt64(0);
				if (!hashSet.Contains(@int))
				{
					hashSet.Add(@int);
					results.Add(ReadSymbol(ownedReader));
				}
			}
		}
		catch
		{
		}
	}

	private void AddResults(List<SymbolResult> results, string sql, string queryLower, int limit, string? extraParam = null)
	{
		HashSet<long> hashSet = new HashSet<long>(results.Select((SymbolResult r) => r.Id));
		List<(string, object)> list = new List<(string, object)>
		{
			("@q", queryLower),
			("@l", limit)
		};
		if (extraParam != null)
		{
			list.Add(("@q2", extraParam));
		}
		using Database.OwnedReader ownedReader = _db.Query(sql, list.ToArray());
		while (ownedReader.Read())
		{
			long @int = ownedReader.GetInt64(0);
			if (!hashSet.Contains(@int))
			{
				hashSet.Add(@int);
				results.Add(ReadSymbol(ownedReader));
			}
		}
	}

	private static string EscapeFts5Query(string query)
	{
		return "\"" + query.Replace("\"", "\"\"") + "\"";
	}

	private SymbolResult ReadSymbol(Database.OwnedReader reader)
	{
		string relativePath = reader.GetString(1);
		return new SymbolResult
		{
			Id = reader.GetInt64(0),
			file = PathHelper.ToDisplayPath(relativePath),
			absolute_path = _fileScanner?.ResolveFullPath(relativePath),
			name = reader.GetString(2),
			kind = reader.GetString(3),
			start_line = reader.GetInt32(4),
			end_line = reader.GetInt32(5),
			is_export = (reader.GetInt32(6) != 0),
			is_async = (reader.GetInt32(7) != 0),
			directive = (reader.IsDBNull(8) ? null : reader.GetString(8))
		};
	}
}
