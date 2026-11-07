using System.Reflection;

namespace ConfasterMCP.TreeSitter;

[Obfuscation(Feature = "all", Exclude = true, ApplyToMembers = true)]
internal struct TSNode
{
	internal uint Context0;

	internal uint Context1;

	internal uint Context2;

	internal uint Context3;

	internal nint Id;

	internal nint Tree;
}
