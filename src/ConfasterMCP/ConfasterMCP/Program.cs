using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ConfasterMCP.Indexing;
using ConfasterMCP.Tools;
using ConfasterMCP.TreeSitter;
using ConfasterMCP.Util;

namespace ConfasterMCP;

internal static class Program
{
	private static int Main(string[] args)
	{
		string text = null;
		List<string> list = new List<string>();
		for (int i = 0; i < args.Length - 1; i++)
		{
			if (args[i] == "--export-root")
			{
				text = args[i + 1];
				i++;
			}
			else if (args[i] == "--extension-root")
			{
				string text2 = args[i + 1];
				int num = text2.IndexOf('|');
				string text3 = ((num >= 0) ? text2.Substring(num + 1) : text2);
				if (Directory.Exists(text3))
				{
					list.Add(text3);
				}
				i++;
			}
			else if (args[i] == "--disabled-tool")
			{
				ToolRegistry.DisabledTools.Add(args[i + 1]);
				i++;
			}
		}
		Console.InputEncoding = Encoding.UTF8;
		Console.OutputEncoding = Encoding.UTF8;
		AppDomain.CurrentDomain.UnhandledException += delegate(object _, UnhandledExceptionEventArgs e)
		{
			try
			{
				Exception ex = e.ExceptionObject as Exception;
				Console.Error.WriteLine("MCP_LOG: FATAL unhandled exception (" + (e.IsTerminating ? "terminating" : "non-terminating") + "): " + (ex?.GetType().FullName ?? "unknown") + ": " + (ex?.Message ?? "no message"));
				if (ex != null)
				{
					Console.Error.WriteLine("MCP_LOG: stack: " + ex.StackTrace);
				}
				Console.Error.Flush();
			}
			catch
			{
			}
		};
		if (!BslAstParser.TryInitialize())
		{
			Console.Error.WriteLine("MCP_LOG: CRITICAL: tree-sitter-bsl native library failed to load: " + (BslAstParser.InitError ?? "unknown") + ". Symbol-based search tools (semantic_find, find_symbol, smart_find, get_symbol_context, find_references, impact_analysis, get_function_context, get_module_functions, find_function_in_object, get_object_structure, list_objects) will be unavailable. search_code, 1C help and COM-query tools continue to work. Reinstall Confaster.");
		}
		bool flag = !string.IsNullOrEmpty(text) && Directory.Exists(text);
		if (!flag && list.Count > 0)
		{
			text = list[0];
			list.RemoveAt(0);
			flag = true;
		}
		ToolRegistry toolRegistry;
		if (flag)
		{
			Database db = new Database(Path.Combine(text, ".index", "onec_search.db"));
			FileCache fileCache = new FileCache();
			List<string> extensionRoots = ((list.Count > 0) ? list : null);
			IndexManager indexManager = new IndexManager(db, text, fileCache, extensionRoots);
			FileScanner fileScanner = new FileScanner(db, text, fileCache, extensionRoots);
			toolRegistry = new ToolRegistry(db, text, fileScanner, fileCache, indexManager, extensionRoots);
		}
		else
		{
			toolRegistry = new ToolRegistry();
		}
		McpServer mcpServer = new McpServer(toolRegistry);
		while (true)
		{
			string text4;
			try
			{
				text4 = Console.ReadLine();
			}
			catch
			{
				break;
			}
			if (text4 == null)
			{
				break;
			}
			if (!string.IsNullOrWhiteSpace(text4))
			{
				string text5 = mcpServer.HandleMessage(text4);
				if (text5 != null)
				{
					Console.WriteLine(text5);
					Console.Out.Flush();
				}
			}
		}
		return 0;
	}
}
