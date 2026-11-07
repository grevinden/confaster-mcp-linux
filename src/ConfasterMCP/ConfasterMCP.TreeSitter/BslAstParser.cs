using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ConfasterMCP.Indexing;

namespace ConfasterMCP.TreeSitter;

[Obfuscation(Feature = "all", Exclude = true, ApplyToMembers = true)]
internal static class BslAstParser
{
	private const string NodeProcedureDef = "procedure_definition";

	private const string NodeFunctionDef = "function_definition";

	private const string NodeMethodCall = "method_call";

	private static readonly byte[] FieldName = Encoding.UTF8.GetBytes("name\0");

	private static readonly byte[] FieldExport = Encoding.UTF8.GetBytes("export\0");

	private static readonly byte[] FieldParameters = Encoding.UTF8.GetBytes("parameters\0");

	private static nint _language;

	private static nint _parser;

	private static readonly object _parserLock = new object();

	private static volatile bool _initAttempted;

	private static volatile bool _initOk;

	private static volatile string? _initError;

	internal static bool IsInitialized => _initOk;

	internal static string? InitError => _initError;

	internal static bool TryInitialize()
	{
		if (_initAttempted)
		{
			return _initOk;
		}
		lock (_parserLock)
		{
			if (_initAttempted)
			{
				return _initOk;
			}
			_initAttempted = true;
			try
			{
				_language = Native.tree_sitter_bsl();
				if (_language == IntPtr.Zero)
				{
					throw new InvalidOperationException("tree_sitter_bsl() returned NULL");
				}
				nint num = Native.ts_parser_new();
				if (num == IntPtr.Zero)
				{
					throw new InvalidOperationException("ts_parser_new() returned NULL");
				}
				if (!Native.ts_parser_set_language(num, _language))
				{
					Native.ts_parser_delete(num);
					throw new InvalidOperationException("ts_parser_set_language failed (ABI mismatch?)");
				}
				_parser = num;
				_initOk = true;
				return true;
			}
			catch (DllNotFoundException ex)
			{
				_initError = "DLL not found: " + ex.Message;
				return false;
			}
			catch (BadImageFormatException ex2)
			{
				_initError = "DLL architecture mismatch (x86/x64): " + ex2.Message;
				return false;
			}
			catch (Exception ex3)
			{
				_initError = ex3.GetType().Name + ": " + ex3.Message;
				return false;
			}
		}
	}

	private static void EnsureInitialized()
	{
		if (!_initOk)
		{
			if (!_initAttempted)
			{
				TryInitialize();
			}
			if (!_initOk)
			{
				throw new InvalidOperationException("tree-sitter not initialized: " + (_initError ?? "unknown"));
			}
		}
	}

	internal static List<SymbolInfo> Parse(byte[] utf8Bytes, string[] lines)
	{
		EnsureInitialized();
		return ParseWithParser(_parser, utf8Bytes, lines, useStaticLock: true);
	}

	internal static List<SymbolInfo> ParseWithParser(nint parser, byte[] utf8Bytes, string[] lines)
	{
		return ParseWithParser(parser, utf8Bytes, lines, useStaticLock: false);
	}

	private static List<SymbolInfo> ParseWithParser(nint parser, byte[] utf8Bytes, string[] lines, bool useStaticLock)
	{
		List<SymbolInfo> list = new List<SymbolInfo>();
		if (utf8Bytes == null || utf8Bytes.Length == 0)
		{
			return list;
		}
		if (parser == IntPtr.Zero)
		{
			throw new InvalidOperationException("TSParser is NULL (init failed or destroyed)");
		}
		GCHandle gCHandle = GCHandle.Alloc(utf8Bytes, GCHandleType.Pinned);
		nint num = IntPtr.Zero;
		try
		{
			if (useStaticLock)
			{
				lock (_parserLock)
				{
					num = Native.ts_parser_parse_string(parser, IntPtr.Zero, gCHandle.AddrOfPinnedObject(), (uint)utf8Bytes.Length);
				}
			}
			else
			{
				num = Native.ts_parser_parse_string(parser, IntPtr.Zero, gCHandle.AddrOfPinnedObject(), (uint)utf8Bytes.Length);
			}
			if (num == IntPtr.Zero)
			{
				return list;
			}
			TSNode node = Native.ts_tree_root_node(num);
			if (Native.ts_node_is_null(node))
			{
				return list;
			}
			Walk(node, utf8Bytes, lines, list);
			return list;
		}
		catch (Exception ex) when (ex is AccessViolationException || ex is SEHException)
		{
			Console.Error.WriteLine($"MCP_LOG: {ex.GetType().Name} в tree-sitter (source {utf8Bytes.Length} bytes): {ex.Message}");
			if (ex.StackTrace != null)
			{
				Console.Error.WriteLine("MCP_LOG: stack: " + ex.StackTrace.Replace("\n", " | "));
			}
			Console.Error.Flush();
			if (useStaticLock)
			{
				TryRecreateParserAfterCrash();
			}
			num = IntPtr.Zero;
			list.Clear();
			throw new InvalidOperationException("tree-sitter native crash (" + ex.GetType().Name + "), file skipped", ex);
		}
		finally
		{
			if (num != IntPtr.Zero)
			{
				try
				{
					Native.ts_tree_delete(num);
				}
				catch
				{
				}
			}
			gCHandle.Free();
		}
	}

	internal static nint CreateParser()
	{
		if (!_initOk)
		{
			TryInitialize();
		}
		if (!_initOk || _language == IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		nint num = Native.ts_parser_new();
		if (num == IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		if (!Native.ts_parser_set_language(num, _language))
		{
			try
			{
				Native.ts_parser_delete(num);
			}
			catch
			{
			}
			return IntPtr.Zero;
		}
		return num;
	}

	internal static void DestroyParser(nint parser)
	{
		if (parser == IntPtr.Zero)
		{
			return;
		}
		try
		{
			Native.ts_parser_delete(parser);
		}
		catch
		{
		}
	}

	private static void TryRecreateParserAfterCrash()
	{
		lock (_parserLock)
		{
			try
			{
				nint num = Native.ts_parser_new();
				if (num == IntPtr.Zero)
				{
					_initOk = false;
					_initError = "ts_parser_new returned NULL after native crash";
					Console.Error.WriteLine("MCP_LOG: tree-sitter parser НЕ удалось пересоздать (ts_parser_new=NULL) — символьные tools отключены");
					Console.Error.Flush();
				}
				else if (!Native.ts_parser_set_language(num, _language))
				{
					try
					{
						Native.ts_parser_delete(num);
					}
					catch
					{
					}
					_initOk = false;
					_initError = "ts_parser_set_language failed after native crash";
					Console.Error.WriteLine("MCP_LOG: tree-sitter parser НЕ удалось пересоздать (set_language failed) — символьные tools отключены");
					Console.Error.Flush();
				}
				else
				{
					_parser = num;
					Console.Error.WriteLine("MCP_LOG: tree-sitter parser пересоздан после native crash, индексация продолжается");
					Console.Error.Flush();
				}
			}
			catch (Exception ex)
			{
				_initOk = false;
				_initError = "parser recreation failed: " + ex.Message;
				Console.Error.WriteLine($"MCP_LOG: tree-sitter parser НЕ удалось пересоздать: {ex.GetType().Name}: {ex.Message} — символьные tools отключены");
				Console.Error.Flush();
			}
		}
	}

	private static void Walk(TSNode node, byte[] source, string[] lines, List<SymbolInfo> result)
	{
		string text = MarshalCString(Native.ts_node_type(node));
		if (text == "procedure_definition" || text == "function_definition")
		{
			TryAddSymbol(node, source, lines, text == "procedure_definition", result);
			return;
		}
		uint num = Native.ts_node_child_count(node);
		for (uint num2 = 0u; num2 < num; num2++)
		{
			TSNode node2 = Native.ts_node_child(node, num2);
			if (!Native.ts_node_is_null(node2))
			{
				Walk(node2, source, lines, result);
			}
		}
	}

	private static void TryAddSymbol(TSNode node, byte[] source, string[] lines, bool isProcedure, List<SymbolInfo> result)
	{
		TSNode node2 = FieldChild(node, FieldName);
		if (Native.ts_node_is_null(node2))
		{
			return;
		}
		string text = NodeText(node2, source);
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		bool isExport = !Native.ts_node_is_null(FieldChild(node, FieldExport));
		TSNode node3 = FieldChild(node, FieldParameters);
		string text2 = null;
		if (!Native.ts_node_is_null(node3))
		{
			string text3 = NodeText(node3, source).Trim();
			if (text3.Length >= 2 && text3[0] == '(')
			{
				string text4 = text3;
				if (text4[text4.Length - 1] == ')')
				{
					text3 = text3.Substring(1, text3.Length - 2);
				}
			}
			text2 = text3.Trim();
			if (text2.Length == 0)
			{
				text2 = null;
			}
		}
		bool isAsync = DetectAsyncPrefix(node, source);
		int num = (int)(Native.ts_node_start_point(node).Row + 1);
		int endLine = (int)(Native.ts_node_end_point(node).Row + 1);
		string directive = ExtractDirective(lines, num);
		string commentHead = ExtractCommentHead(lines, num);
		List<(string, int)> calls = new List<(string, int)>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		uint num2 = Native.ts_node_child_count(node);
		for (uint num3 = 0u; num3 < num2; num3++)
		{
			TSNode node4 = Native.ts_node_child(node, num3);
			if (!Native.ts_node_is_null(node4))
			{
				CollectCallsInBody(node4, source, seen, calls);
			}
		}
		result.Add(new SymbolInfo
		{
			Name = text,
			Kind = (isProcedure ? "procedure" : "function"),
			StartLine = num,
			EndLine = endLine,
			IsExport = isExport,
			IsAsync = isAsync,
			Directive = directive,
			Parameters = text2,
			CommentHead = commentHead,
			Calls = calls
		});
	}

	private static void CollectCallsInBody(TSNode node, byte[] source, HashSet<string> seen, List<(string, int)> calls)
	{
		switch (MarshalCString(Native.ts_node_type(node)))
		{
		case "function_definition":
			return;
		case "method_call":
		{
			TSNode node2 = FieldChild(node, FieldName);
			if (!Native.ts_node_is_null(node2))
			{
				string text = NodeText(node2, source).Trim();
				if (text.Length >= 2 && !BslKeywords.IsKeyword(text) && seen.Add(text))
				{
					int item = (int)(Native.ts_node_start_point(node).Row + 1);
					calls.Add((text, item));
				}
			}
			break;
		}
		}
		uint num = Native.ts_node_child_count(node);
		for (uint num2 = 0u; num2 < num; num2++)
		{
			TSNode node3 = Native.ts_node_child(node, num2);
			if (!Native.ts_node_is_null(node3))
			{
				CollectCallsInBody(node3, source, seen, calls);
			}
		}
	}

	private static TSNode FieldChild(TSNode parent, byte[] fieldNameUtf8WithNul)
	{
		GCHandle gCHandle = GCHandle.Alloc(fieldNameUtf8WithNul, GCHandleType.Pinned);
		try
		{
			return Native.ts_node_child_by_field_name(parent, gCHandle.AddrOfPinnedObject(), (uint)(fieldNameUtf8WithNul.Length - 1));
		}
		finally
		{
			gCHandle.Free();
		}
	}

	private static string NodeText(TSNode node, byte[] source)
	{
		uint num = Native.ts_node_start_byte(node);
		uint num2 = Native.ts_node_end_byte(node);
		if (num2 <= num || num2 > source.Length)
		{
			return "";
		}
		return Encoding.UTF8.GetString(source, (int)num, (int)(num2 - num));
	}

	private static string MarshalCString(nint ptr)
	{
		object obj;
		if (ptr != IntPtr.Zero)
		{
			obj = Marshal.PtrToStringAnsi(ptr);
			if (obj == null)
			{
				return "";
			}
		}
		else
		{
			obj = "";
		}
		return (string)obj;
	}

	private static bool DetectAsyncPrefix(TSNode procNode, byte[] source)
	{
		uint num = Native.ts_node_start_byte(procNode);
		uint num2 = Native.ts_node_end_byte(procNode);
		if (num2 <= num || num >= source.Length)
		{
			return false;
		}
		uint val = (uint)source.Length - num;
		uint num3 = Math.Min(Math.Min(num2 - num, val), 64u);
		if (num3 == 0)
		{
			return false;
		}
		string text = Encoding.UTF8.GetString(source, (int)num, (int)num3).TrimStart();
		if (!text.StartsWith("Асинх", StringComparison.OrdinalIgnoreCase))
		{
			return text.StartsWith("Async", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static string? ExtractDirective(string[] lines, int startLine1Based)
	{
		int num = startLine1Based - 2;
		if (num >= lines.Length)
		{
			num = lines.Length - 1;
		}
		while (num >= 0)
		{
			string text = lines[num].Trim();
			if (text.Length == 0)
			{
				num--;
				continue;
			}
			if (text.StartsWith("//"))
			{
				return null;
			}
			if (text.StartsWith('&'))
			{
				int i;
				for (i = 1; i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'); i++)
				{
				}
				if (i <= 1)
				{
					return null;
				}
				return text.Substring(1, i - 1);
			}
			return null;
		}
		return null;
	}

	private static string? ExtractCommentHead(string[] lines, int startLine1Based)
	{
		List<string> list = new List<string>();
		int num = startLine1Based - 2;
		if (num >= lines.Length)
		{
			num = lines.Length - 1;
		}
		while (num >= 0 && list.Count < 5)
		{
			string text = lines[num].Trim();
			if (text.Length == 0)
			{
				num--;
				continue;
			}
			if (text.StartsWith("&"))
			{
				num--;
				continue;
			}
			if (!text.StartsWith("//"))
			{
				break;
			}
			list.Add(text.TrimStart('/').Trim());
			num--;
		}
		if (list.Count == 0)
		{
			return null;
		}
		list.Reverse();
		return string.Join(' ', list);
	}
}
