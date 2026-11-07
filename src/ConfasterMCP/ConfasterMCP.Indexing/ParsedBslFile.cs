using System.Collections.Generic;

namespace ConfasterMCP.Indexing;

internal sealed class ParsedBslFile
{
	internal required string Path { get; init; }

	internal required List<ParsedSymbol> Symbols { get; init; }

	internal required List<RegionInfo> Regions { get; init; }
}
