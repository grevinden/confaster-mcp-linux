using System;
using System.Collections.Generic;
using System.Text;
using ConfasterMCP.Tools;
using ConfasterMCP.TreeSitter;
using ConfasterMCP.Util;

namespace ConfasterMCP;

internal static class Program
{
	private static int Main(string[] args)
	{
		string mainPath = null;
		List<(string, string)> list = new List<(string, string)>();
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "--no-index-admin")
			{
				ToolRegistry.IndexAdminDisabled = true;
				continue;
			}
			if (i + 1 >= args.Length)
			{
				break;
			}
			if (args[i] == "--export-root")
			{
				mainPath = args[i + 1];
				i++;
			}
			else if (args[i] == "--extension-root")
			{
				var (item, text) = ConfigRoots.ParseExtensionArg(args[i + 1]);
				if (!string.IsNullOrWhiteSpace(text))
				{
					list.Add((item, text));
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
		ConfigRoots configRoots = ConfigRoots.Build(mainPath, list);
		if (configRoots != null)
		{
			foreach (string warning in configRoots.Warnings)
			{
				Console.Error.WriteLine("MCP_LOG: WARNING: " + warning);
			}
			Console.Error.Flush();
		}
		ToolRegistry toolRegistry;
		if (configRoots != null)
		{
			FileCache fileCache = new FileCache();
			toolRegistry = new ToolRegistry(configRoots, fileCache);
		}
		else
		{
			toolRegistry = new ToolRegistry();
		}
		McpServer mcpServer = new McpServer(toolRegistry);
		while (true)
		{
			string text2;
			try
			{
				text2 = Console.ReadLine();
			}
			catch
			{
				break;
			}
			if (text2 == null)
			{
				break;
			}
			if (!string.IsNullOrWhiteSpace(text2))
			{
				string text3 = mcpServer.HandleMessage(text2);
				if (text3 != null)
				{
					Console.WriteLine(text3);
					Console.Out.Flush();
				}
			}
		}
		return 0;
	}
}
