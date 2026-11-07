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
		JsonElement? id = null;
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
			id = value2.Clone();
			JsonElement value3;
			JsonElement? jsonElement = (rootElement.TryGetProperty("params", out value3) ? new JsonElement?(value3) : ((JsonElement?)null));
			JsonElement value4 = id.Value;
			return text switch
			{
				"initialize" => BuildResponse(value4, HandleInitialize()), 
				"tools/list" => BuildResponse(value4, HandleToolsList()), 
				"tools/call" => BuildResponse(value4, HandleToolsCall(jsonElement)), 
				"ping" => BuildResponse(value4, JsonSerializer.SerializeToElement(new { })), 
				_ => BuildErrorResponse(value4, -32601, "Method not found: " + text), 
			};
		}
		catch (JsonException ex)
		{
			return (!id.HasValue) ? BuildErrorResponse(null, -32700, "Parse error") : BuildErrorResponse(id, -32603, ex.Message);
		}
		catch (Exception ex2)
		{
			return BuildErrorResponse(id, -32603, ex2.Message);
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
			Console.Error.WriteLine($"MCP_LOG: tool '{text}' failed: {ex.GetType().Name}: {ex.Message}");
			Console.Error.Flush();
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

	private static string BuildResponse(JsonElement id, JsonElement result)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using (Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(memoryStream))
		{
			utf8JsonWriter.WriteStartObject();
			utf8JsonWriter.WriteString("jsonrpc", "2.0");
			utf8JsonWriter.WritePropertyName("id");
			id.WriteTo(utf8JsonWriter);
			utf8JsonWriter.WritePropertyName("result");
			result.WriteTo(utf8JsonWriter);
			utf8JsonWriter.WriteEndObject();
		}
		return Encoding.UTF8.GetString(memoryStream.ToArray());
	}

	private static string BuildErrorResponse(JsonElement? id, int code, string message)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using (Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(memoryStream))
		{
			utf8JsonWriter.WriteStartObject();
			utf8JsonWriter.WriteString("jsonrpc", "2.0");
			utf8JsonWriter.WritePropertyName("id");
			if (id.HasValue)
			{
				id.Value.WriteTo(utf8JsonWriter);
			}
			else
			{
				utf8JsonWriter.WriteNullValue();
			}
			utf8JsonWriter.WritePropertyName("error");
			utf8JsonWriter.WriteStartObject();
			utf8JsonWriter.WriteNumber("code", code);
			utf8JsonWriter.WriteString("message", message);
			utf8JsonWriter.WriteEndObject();
			utf8JsonWriter.WriteEndObject();
		}
		return Encoding.UTF8.GetString(memoryStream.ToArray());
	}

	private static string EscapeJson(string s)
	{
		return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")
			.Replace("\r", "\\r");
	}
}
