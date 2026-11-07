using System.Reflection;

namespace ConfasterMCP.TreeSitter;

[Obfuscation(Feature = "all", Exclude = true, ApplyToMembers = true)]
internal struct TSPoint
{
	internal uint Row;

	internal uint Column;
}
