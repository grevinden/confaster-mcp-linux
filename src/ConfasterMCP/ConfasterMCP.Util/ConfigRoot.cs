namespace ConfasterMCP.Util;

internal sealed class ConfigRoot
{
	internal required string Id { get; init; }

	internal required string Path { get; init; }

	internal bool IsMain => Id.Length == 0;

	internal string? NamePrefix { get; init; }

	internal bool IdUncertain { get; init; }

	internal bool AutoDetected { get; init; }

	internal string Label => LabelOfId(Id);

	internal static string LabelOfId(string id)
	{
		if (id.Length != 0)
		{
			return "расширение " + id;
		}
		return "основная конфигурация";
	}
}
