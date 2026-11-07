namespace ConfasterMCP.Indexing;

internal sealed class RegionInfo
{
	internal required string Name { get; init; }

	internal required int StartLine { get; init; }

	internal required int EndLine { get; init; }
}
