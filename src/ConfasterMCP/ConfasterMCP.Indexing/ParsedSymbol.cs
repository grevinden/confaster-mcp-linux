namespace ConfasterMCP.Indexing;

internal sealed class ParsedSymbol
{
	internal required SymbolInfo Info { get; init; }

	internal required string NameTokens { get; init; }

	internal required string ParamTokens { get; init; }
}
