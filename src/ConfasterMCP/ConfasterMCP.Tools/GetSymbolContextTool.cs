using System;
using System.Text;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class GetSymbolContextTool : IConfasterMCPTool
{
	private readonly Database _db;

	private readonly string _exportRoot;

	private readonly FileCache _fileCache;

	private readonly FileScanner _fileScanner;

	public string Name => "get_symbol_context";

	public string Description => "Получить полный код процедуры или функции по файлу и номеру строки. Возвращает полное тело символа от начала до конца определения.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			file = new
			{
				type = "string",
				description = "Путь к файлу (из результатов find_symbol)"
			},
			line = new
			{
				type = "integer",
				description = "Номер строки внутри процедуры/функции (start_line из find_symbol)"
			}
		},
		required = new string[2] { "file", "line" }
	});

	internal GetSymbolContextTool(Database db, string exportRoot, FileCache fileCache, FileScanner fileScanner)
	{
		_db = db;
		_exportRoot = exportRoot;
		_fileCache = fileCache;
		_fileScanner = fileScanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("file", out value) ? (value.GetString() ?? "") : "");
		JsonElement value2;
		int num = (args.TryGetProperty("line", out value2) ? value2.GetInt32() : 0);
		if (string.IsNullOrEmpty(text) || num <= 0)
		{
			return "{\"error\": \"file and line are required\"}";
		}
		string item = "%" + text.Replace("\\", "/").TrimStart('/').TrimStart('%') + "%";
		using Database.OwnedReader ownedReader = _db.Query("\n                SELECT file_path, name, kind, start_line, end_line\n                FROM symbols\n                WHERE file_path LIKE @f AND start_line <= @line AND end_line >= @line\n                ORDER BY (end_line - start_line) ASC\n                LIMIT 1", ("@f", item), ("@line", num));
		if (!ownedReader.Read())
		{
			return new GetFileContextTool(_exportRoot, _fileCache, _fileScanner).Execute(args);
		}
		string text2 = ownedReader.GetString(0);
		string name = ownedReader.GetString(1);
		string kind = ownedReader.GetString(2);
		int @int = ownedReader.GetInt32(3);
		int int2 = ownedReader.GetInt32(4);
		string text3 = _fileScanner.ResolveFullPath(text2);
		if (text3 == null)
		{
			return JsonSerializer.Serialize(new
			{
				error = "File not found: " + text2
			});
		}
		try
		{
			string[] lines = _fileCache.GetLines(text3);
			int num2 = Math.Max(0, @int - 1);
			int num3 = Math.Min(lines.Length, int2);
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = num2; i < num3; i++)
			{
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, stringBuilder2);
				handler.AppendFormatted(i + 1, 6);
				handler.AppendLiteral(": ");
				handler.AppendFormatted(lines[i]);
				stringBuilder2.AppendLine(ref handler);
			}
			return JsonSerializer.Serialize(new
			{
				symbol = new
				{
					name = name,
					kind = kind,
					start_line = @int,
					end_line = int2
				},
				file = PathHelper.ToDisplayPath(text2),
				absolute_path = text3,
				code = stringBuilder.ToString()
			});
		}
		catch (Exception ex)
		{
			return JsonSerializer.Serialize(new
			{
				error = "Cannot read file: " + ex.Message
			});
		}
	}
}
