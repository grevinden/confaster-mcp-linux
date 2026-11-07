using System.Collections.Generic;

namespace ConfasterMCP.Indexing;

internal sealed class ObjectMetadata
{
	internal required string Fqn { get; init; }

	internal required string TypeFolder { get; init; }

	internal required string Name { get; init; }

	internal string? ExportPath { get; init; }

	internal bool Adopted { get; set; }

	internal List<string> Synonyms { get; } = new List<string>();

	internal List<ObjectItemInfo> Items { get; } = new List<ObjectItemInfo>();
}
