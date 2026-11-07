namespace ConfasterMCP.Indexing;

internal sealed class SearchResult
{
	internal required string FilePath { get; init; }

	internal required string DisplayPath { get; init; }

	internal required string AbsolutePath { get; init; }

	internal required int LineNumber { get; init; }

	internal required string LineText { get; init; }

	internal required string Context { get; init; }
}
