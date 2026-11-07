using System;
using System.Text;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class GetFileContextTool : IConfasterMCPTool
{
	private readonly FileCache _fileCache;

	private readonly FileScanner _fileScanner;

	public string Name => "get_file_context";

	public string Description => "Получить контекст кода из файла конфигурации 1С вокруг указанной строки.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			file = new
			{
				type = "string",
				description = "Путь к файлу: absolute_path из результатов поиска либо путь относительно корня выгрузки"
			},
			line = new
			{
				type = "integer",
				description = "Номер строки (1-based)"
			},
			radius = new
			{
				type = "integer",
				description = "Строк контекста выше и ниже (по умолчанию 40)",
				@default = 40
			},
			configuration = new
			{
				type = "string",
				description = "Для относительного пути: имя расширения или 'основная'. У заимствованных объектов один и тот же путь есть и в основной конфигурации, и в расширении."
			}
		},
		required = new string[2] { "file", "line" }
	});

	internal GetFileContextTool(FileCache fileCache, FileScanner fileScanner)
	{
		_fileCache = fileCache;
		_fileScanner = fileScanner;
	}

	public string Execute(JsonElement args)
	{
		JsonElement value;
		string text = (args.TryGetProperty("file", out value) ? (value.GetString() ?? "") : "");
		JsonElement value2;
		int num = (args.TryGetProperty("line", out value2) ? value2.GetInt32() : 0);
		int value3 = (args.TryGetProperty("radius", out var value4) ? value4.GetInt32() : 40);
		JsonElement value5;
		string name = (args.TryGetProperty("configuration", out value5) ? value5.GetString() : null);
		if (string.IsNullOrEmpty(text) || num <= 0)
		{
			return "{\"error\": \"file and line are required\"}";
		}
		value3 = Math.Clamp(value3, 1, 200);
		ConfigRoots roots = _fileScanner.Roots;
		if (roots.IsUnknownName(name, out ConfigRoot root))
		{
			return ToolJson.UnknownConfiguration(roots, name);
		}
		string text2 = ResolveFilePath(roots, text, root);
		if (text2 == null)
		{
			return ToolJson.Serialize(new
			{
				error = ((root != null) ? ("File not found in " + root.Label + ": " + text) : ("File not found: " + text))
			});
		}
		try
		{
			string[] lines = _fileCache.GetLines(text2);
			int num2 = Math.Max(0, num - 1 - value3);
			int num3 = Math.Min(lines.Length, num - 1 + value3 + 1);
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = num2; i < num3; i++)
			{
				string value6 = ((i == num - 1) ? "> " : "  ");
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 3, stringBuilder2);
				handler.AppendFormatted(value6);
				handler.AppendFormatted(i + 1, 6);
				handler.AppendLiteral(": ");
				handler.AppendFormatted(lines[i]);
				stringBuilder2.AppendLine(ref handler);
			}
			string text3 = roots.KeyOfFullPath(text2);
			return ToolJson.Serialize(new
			{
				file = ((text3 != null) ? ConfigRoots.DisplayPath(text3) : text2),
				configuration = ((text3 != null) ? roots.LabelOf(text3) : null),
				absolute_path = text2,
				target_line = num,
				total_lines = lines.Length,
				from_line = num2 + 1,
				to_line = num3,
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

	internal static string? ResolveFilePath(ConfigRoots roots, string file, ConfigRoot? preferred)
	{
		string text = roots.ResolveUserPath(file, preferred);
		if (text != null)
		{
			return text;
		}
		string text2 = file.Replace('\\', '/');
		if (text2.Contains("/Forms/") && !text2.Contains("/Ext/"))
		{
			int num = text2.LastIndexOf('/');
			if (num > 0)
			{
				string path = text2.Substring(0, num) + "/Ext/Form" + text2.Substring(num);
				string text3 = roots.ResolveUserPath(path, preferred);
				if (text3 != null)
				{
					return text3;
				}
			}
		}
		int num2 = text2.LastIndexOf('/');
		if (num2 > 0)
		{
			string path2 = text2.Substring(0, num2) + "/Ext" + text2.Substring(num2);
			string text4 = roots.ResolveUserPath(path2, preferred);
			if (text4 != null)
			{
				return text4;
			}
		}
		return null;
	}
}
