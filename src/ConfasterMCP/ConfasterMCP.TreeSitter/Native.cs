using System.Reflection;
using System.Runtime.InteropServices;

namespace ConfasterMCP.TreeSitter;

[Obfuscation(Feature = "all", Exclude = true, ApplyToMembers = true)]
internal static class Native
{
	private const string DllName = "tree-sitter-bsl";

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern nint tree_sitter_bsl();

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern nint ts_parser_new();

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern void ts_parser_delete(nint parser);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.U1)]
	internal static extern bool ts_parser_set_language(nint parser, nint language);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern nint ts_parser_parse_string(nint parser, nint oldTree, nint utf8Bytes, uint length);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern void ts_tree_delete(nint tree);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern TSNode ts_tree_root_node(nint tree);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern nint ts_node_type(TSNode node);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.U1)]
	internal static extern bool ts_node_is_null(TSNode node);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.U1)]
	internal static extern bool ts_node_is_named(TSNode node);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern uint ts_node_child_count(TSNode node);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern TSNode ts_node_child(TSNode node, uint index);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern uint ts_node_named_child_count(TSNode node);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern TSNode ts_node_named_child(TSNode node, uint index);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern TSNode ts_node_child_by_field_name(TSNode node, nint name, uint nameLength);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern uint ts_node_start_byte(TSNode node);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern uint ts_node_end_byte(TSNode node);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern TSPoint ts_node_start_point(TSNode node);

	[DllImport("tree-sitter-bsl", CallingConvention = CallingConvention.Cdecl)]
	internal static extern TSPoint ts_node_end_point(TSNode node);
}
