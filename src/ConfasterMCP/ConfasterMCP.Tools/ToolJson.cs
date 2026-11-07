using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal static class ToolJson
{
	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	internal static string Serialize(object value)
	{
		return JsonSerializer.Serialize(value, Options);
	}

	internal static string UnknownConfiguration(ConfigRoots roots, string? name)
	{
		return Serialize(new
		{
			error = "Конфигурация не найдена: " + name,
			available = roots.All.Select((ConfigRoot x) => x.Label)
		});
	}
}
