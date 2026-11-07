using System.Collections.Generic;

namespace ConfasterMCP.Tools;

internal sealed class SymbolResult
{
	internal long Id { get; init; }

	internal string Key { get; init; } = "";

	public string file { get; init; } = "";

	public string? configuration { get; init; }

	public string? absolute_path { get; init; }

	public string name { get; init; } = "";

	public string kind { get; init; } = "";

	public int start_line { get; init; }

	public int end_line { get; init; }

	public bool is_export { get; init; }

	public bool is_async { get; init; }

	public string? directive { get; init; }

	public object? extends { get; init; }

	public List<ExtensionLinks.Interceptor>? intercepted_by { get; set; }
}
