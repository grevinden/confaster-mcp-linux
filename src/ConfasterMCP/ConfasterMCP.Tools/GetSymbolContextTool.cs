using System;
using System.Text;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class GetSymbolContextTool : IConfasterMCPTool
{
	private readonly Database _db;

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
				description = "Путь к файлу: absolute_path из результатов find_symbol (надёжнее) либо file"
			},
			line = new
			{
				type = "integer",
				description = "Номер строки внутри процедуры/функции (start_line из find_symbol)"
			},
			configuration = new
			{
				type = "string",
				description = "Для относительного пути: имя расширения или 'основная' (поле configuration из find_symbol)"
			}
		},
		required = new string[2] { "file", "line" }
	});

	internal GetSymbolContextTool(Database db, FileCache fileCache, FileScanner fileScanner)
	{
		_db = db;
		_fileCache = fileCache;
		_fileScanner = fileScanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("file", out value) ? (value.GetString() ?? "") : "");
		JsonElement value2;
		int num = (args.TryGetProperty("line", out value2) ? value2.GetInt32() : 0);
		JsonElement value3;
		string name = (args.TryGetProperty("configuration", out value3) ? value3.GetString() : null);
		if (string.IsNullOrEmpty(text) || num <= 0)
		{
			return "{\"error\": \"file and line are required\"}";
		}
		ConfigRoots roots = _fileScanner.Roots;
		if (roots.IsUnknownName(name, out ConfigRoot root))
		{
			return ToolJson.UnknownConfiguration(roots, name);
		}
		string text2 = null;
		string text3 = GetFileContextTool.ResolveFilePath(roots, text, root);
		if (text3 != null)
		{
			text2 = roots.KeyOfFullPath(text3);
		}
		Database.OwnedReader ownedReader;
		if (text2 != null)
		{
			ownedReader = _db.Query("\n                    SELECT file_path, name, kind, start_line, end_line\n                    FROM symbols\n                    WHERE file_path = @f AND start_line <= @line AND end_line >= @line\n                    ORDER BY (end_line - start_line) ASC\n                    LIMIT 1", ("@f", text2), ("@line", num));
		}
		else
		{
			string item = "%" + text.Replace("\\", "/").TrimStart('/').TrimStart('%') + "%";
			string text4 = ((root != null) ? ConfigRoots.InRootSql("file_path", root) : roots.KnownKeysSql("file_path"));
			ownedReader = _db.Query("\n                    SELECT file_path, name, kind, start_line, end_line\n                    FROM symbols\n                    WHERE file_path LIKE @f AND start_line <= @line AND end_line >= @line\n                      AND " + text4 + "\n                    ORDER BY (file_path LIKE '@%') ASC, (end_line - start_line) ASC\n                    LIMIT 1", ("@f", item), ("@line", num));
		}
		using (ownedReader)
		{
			if (!ownedReader.Read())
			{
				return new GetFileContextTool(_fileCache, _fileScanner).Execute(args);
			}
			string key = ownedReader.GetString(0);
			string name2 = ownedReader.GetString(1);
			string kind = ownedReader.GetString(2);
			int @int = ownedReader.GetInt32(3);
			int int2 = ownedReader.GetInt32(4);
			string text5 = _fileScanner.ResolveFullPath(key);
			if (text5 == null)
			{
				return JsonSerializer.Serialize(new
				{
					error = "File not found: " + ConfigRoots.DisplayPath(key)
				});
			}
			try
			{
				string[] lines = _fileCache.GetLines(text5);
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
				return ToolJson.Serialize(new
				{
					symbol = new
					{
						name = name2,
						kind = kind,
						start_line = @int,
						end_line = int2
					},
					file = ConfigRoots.DisplayPath(key),
					configuration = roots.LabelOf(key),
					absolute_path = text5,
					intercepted_by = ExtensionLinks.FindInterceptors(_db, roots, key, name2),
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
}
