using System.Text.Json;

namespace ConfasterMCP.Tools;

internal interface IConfasterMCPTool
{
	string Name { get; }

	string Description { get; }

	JsonElement InputSchema { get; }

	string Execute(JsonElement arguments);
}
