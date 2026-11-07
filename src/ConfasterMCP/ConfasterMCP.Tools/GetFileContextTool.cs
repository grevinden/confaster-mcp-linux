using System;
using System.Text;
using System.Text.Json;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class GetFileContextTool : IConfasterMCPTool
{
	private readonly string _exportRoot;

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
				description = "Путь к файлу относительно корня конфигурации"
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
			}
		},
		required = new string[2] { "file", "line" }
	});

	internal GetFileContextTool(string exportRoot, FileCache fileCache, FileScanner fileScanner)
	{
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
		int value3 = (args.TryGetProperty("radius", out var value4) ? value4.GetInt32() : 40);
		if (string.IsNullOrEmpty(text) || num <= 0)
		{
			return "{\"error\": \"file and line are required\"}";
		}
		value3 = Math.Clamp(value3, 1, 200);
		string text2 = ResolveFilePath(text);
		if (text2 == null)
		{
			return JsonSerializer.Serialize(new
			{
				error = "File not found: " + text
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
				string value5 = ((i == num - 1) ? "> " : "  ");
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 3, stringBuilder2);
				handler.AppendFormatted(value5);
				handler.AppendFormatted(i + 1, 6);
				handler.AppendLiteral(": ");
				handler.AppendFormatted(lines[i]);
				stringBuilder2.AppendLine(ref handler);
			}
			return JsonSerializer.Serialize(new
			{
				file = PathHelper.ToDisplayPath(PathHelper.GetRelativePath(text2, _exportRoot)),
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

	private string? ResolveFilePath(string file)
	{
		string text = _fileScanner.ResolveFullPath(file);
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
				string relativePath = text2.Substring(0, num) + "/Ext/Form" + text2.Substring(num);
				string text3 = _fileScanner.ResolveFullPath(relativePath);
				if (text3 != null)
				{
					return text3;
				}
			}
		}
		int num2 = text2.LastIndexOf('/');
		if (num2 > 0)
		{
			string relativePath2 = text2.Substring(0, num2) + "/Ext" + text2.Substring(num2);
			string text4 = _fileScanner.ResolveFullPath(relativePath2);
			if (text4 != null)
			{
				return text4;
			}
		}
		return null;
	}
}
