using System.Collections.Generic;

namespace ConfasterMCP.Indexing;

internal sealed class SymbolInfo
{
	internal required string Name { get; init; }

	internal required string Kind { get; init; }

	internal required int StartLine { get; init; }

	internal required int EndLine { get; init; }

	internal required bool IsExport { get; init; }

	internal required bool IsAsync { get; init; }

	internal string? Directive { get; init; }

	internal string? Annotation { get; init; }

	internal string? AnnotationTarget { get; init; }

	internal string? Parameters { get; init; }

	internal string? CommentHead { get; init; }

	internal List<(string name, int line)> Calls { get; init; } = new List<(string, int)>();
}
