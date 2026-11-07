using System;
using System.IO;
using System.Text;
using System.Text.Json;
using ConfasterMCP.Tools;

namespace ConfasterMCP;

internal sealed class McpServer
{
	private readonly ToolRegistry _toolRegistry;

	internal McpServer(ToolRegistry toolRegistry)
	{
		_toolRegistry = toolRegistry;
	}

	internal string? HandleMessage(string jsonLine)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(jsonLine);
			JsonElement rootElement = jsonDocument.RootElement;
			JsonElement value;
			string text = (rootElement.TryGetProperty("method", out value) ? (value.GetString() ?? "") : "");
			if (!rootElement.TryGetProperty("id", out var value2))
			{
				return null;
			}
			int id = ((value2.ValueKind == JsonValueKind.Number) ? value2.GetInt32() : 0);
			JsonElement value3;
			JsonElement? jsonElement = (rootElement.TryGetProperty("params", out value3) ? new JsonElement?(value3) : ((JsonElement?)null));
			return text switch
			{
				"initialize" => BuildResponse(id, HandleInitialize()), 
				"tools/list" => BuildResponse(id, HandleToolsList()), 
				"tools/call" => BuildResponse(id, HandleToolsCall(jsonElement)), 
				"ping" => BuildResponse(id, JsonSerializer.SerializeToElement(new { })), 
				_ => BuildErrorResponse(id, -32601, "Method not found: " + text), 
			};
		}
		catch (JsonException)
		{
			return BuildErrorResponse(0, -32700, "Parse error");
		}
		catch (Exception ex2)
		{
			return BuildErrorResponse(0, -32603, ex2.Message);
		}
	}

	private JsonElement HandleInitialize()
	{
		return JsonSerializer.SerializeToElement(new
		{
			protocolVersion = "2024-11-05",
			capabilities = new
			{
				tools = new { }
			},
			serverInfo = new
			{
				name = "1c-search",
				version = "1.0.0"
			}
		});
	}

	private JsonElement HandleToolsList()
	{
		return JsonSerializer.SerializeToElement(new
		{
			tools = _toolRegistry.GetAllDefinitions()
		});
	}

	private JsonElement HandleToolsCall(JsonElement? @params)
	{
		if (!@params.HasValue)
		{
			return BuildToolError("Missing params");
		}
		JsonElement value = @params.Value;
		JsonElement value2;
		string text = (value.TryGetProperty("name", out value2) ? (value2.GetString() ?? "") : "");
		JsonElement value3;
		JsonElement arguments = (value.TryGetProperty("arguments", out value3) ? value3 : default(JsonElement));
		if (string.IsNullOrEmpty(text))
		{
			return BuildToolError("Missing tool name");
		}
		IConfasterMCPTool tool = _toolRegistry.GetTool(text);
		if (tool == null)
		{
			return BuildToolError("Unknown tool: " + text);
		}
		try
		{
			string text2 = tool.Execute(arguments);
			return JsonSerializer.SerializeToElement(new
			{
				content = new[]
				{
					new
					{
						type = "text",
						text = text2
					}
				}
			});
		}
		catch (Exception ex)
		{
			return BuildToolError(ex.Message);
		}
	}

	private static JsonElement BuildToolError(string message)
	{
		return JsonSerializer.SerializeToElement(new
		{
			content = new[]
			{
				new
				{
					type = "text",
					text = "{\"error\": \"" + EscapeJson(message) + "\"}"
				}
			},
			isError = true
		});
	}

	private static string BuildResponse(int id, JsonElement result)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using (Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter((Stream)memoryStream, default(JsonWriterOptions)))
		{
			utf8JsonWriter.WriteStartObject();
			utf8JsonWriter.WriteString("jsonrpc", "2.0");
			utf8JsonWriter.WriteNumber("id", id);
			utf8JsonWriter.WritePropertyName("result");
			result.WriteTo(utf8JsonWriter);
			utf8JsonWriter.WriteEndObject();
		}
		return Encoding.UTF8.GetString(memoryStream.ToArray());
	}

	private static string BuildErrorResponse(int id, int code, string message)
	{
		return JsonSerializer.Serialize(new
		{
			jsonrpc = "2.0",
			id = id,
			error = new { code, message }
		});
	}

	private static string EscapeJson(string s)
	{
		return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")
			.Replace("\r", "\\r");
	}
}
