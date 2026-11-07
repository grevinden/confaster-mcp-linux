using System;
using System.Collections.Generic;
using System.IO;
using ConfasterMCP.Help;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal sealed class ToolRegistry
{
	internal static readonly HashSet<string> DisabledTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, IConfasterMCPTool> _tools = new Dictionary<string, IConfasterMCPTool>(StringComparer.OrdinalIgnoreCase);

	private readonly FileCache _fileCache;

	private HelpDatabase? _helpDb;

	private HelpIndexer? _helpIndexer;

	private Database _db;

	private string _exportRoot;

	private List<string>? _extensionRoots;

	internal string ExportRoot => _exportRoot;

	internal List<string>? ExtensionRoots => _extensionRoots;

	internal ToolRegistry(Database db, string exportRoot, FileScanner fileScanner, FileCache fileCache, IndexManager indexManager, List<string>? extensionRoots = null)
	{
		_db = db;
		_exportRoot = exportRoot;
		_fileCache = fileCache;
		_extensionRoots = extensionRoots;
		RegisterTools(db, exportRoot, fileScanner, fileCache, indexManager);
		InitHelpTools();
	}

	internal ToolRegistry()
	{
		_db = null;
		_exportRoot = "";
		_fileCache = null;
		InitHelpTools();
	}

	private void InitHelpTools()
	{
		if (HelpIndexer.FindPlatformPath() != null)
		{
			try
			{
				_helpDb = new HelpDatabase();
				_helpIndexer = new HelpIndexer(_helpDb);
				RegisterHelpTools(_helpDb, _helpIndexer);
				_helpIndexer.EnsureIndexed();
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("HELP_STATUS:init_error:" + ex.Message);
			}
		}
	}

	internal (int added, int updated, int deleted) Reinitialize(string newExportRoot, List<string>? extensionRoots = null)
	{
		lock (_db.WriteLock)
		{
			_db.Dispose();
		}
		_fileCache.Clear();
		_exportRoot = newExportRoot;
		_extensionRoots = extensionRoots;
		string dbPath = Path.Combine(newExportRoot, ".index", "onec_search.db");
		_db = new Database(dbPath);
		FileScanner fileScanner = new FileScanner(_db, newExportRoot, _fileCache, extensionRoots);
		IndexManager indexManager = new IndexManager(_db, newExportRoot, _fileCache, extensionRoots);
		_tools.Clear();
		RegisterTools(_db, newExportRoot, fileScanner, _fileCache, indexManager);
		if (_helpDb != null && _helpIndexer != null)
		{
			RegisterHelpTools(_helpDb, _helpIndexer);
		}
		return indexManager.IncrementalSync();
	}

	private void RegisterTools(Database db, string exportRoot, FileScanner fileScanner, FileCache fileCache, IndexManager indexManager)
	{
		Register(new SemanticFindTool(db, fileScanner, fileCache));
		Register(new SearchCodeTool(fileScanner));
		Register(new FindSymbolTool(db, fileScanner));
		Register(new SmartFindTool(db, exportRoot, fileCache, fileScanner));
		Register(new GetSymbolContextTool(db, exportRoot, fileCache, fileScanner));
		Register(new GetFileContextTool(exportRoot, fileCache, fileScanner));
		Register(new ListObjectsTool(db));
		Register(new GetObjectStructureTool(db, fileScanner));
		Register(new FindReferencesTool(fileScanner));
		Register(new ImpactAnalysisTool(fileScanner));
		Register(new GetFunctionContextTool(db, fileScanner));
		Register(new GetModuleFunctionsTool(db, fileScanner));
		Register(new FindFunctionInObjectTool(db, exportRoot, fileCache, fileScanner));
		Register(new StatsTool(db, exportRoot));
		Register(new SyncIndexTool(indexManager));
		Register(new BenchmarkTool(this));
		Register(new SwitchExportRootTool(this));
	}

	private void RegisterHelpTools(HelpDatabase helpDb, HelpIndexer helpIndexer)
	{
		Register(new Search1cHelpTool(helpDb, helpIndexer));
		Register(new Get1cHelpTopicTool(helpDb, helpIndexer));
		Register(new List1cHelpVersionsTool(helpDb, helpIndexer));
		Register(new Reindex1cHelpTool(helpIndexer));
	}

	private void Register(IConfasterMCPTool tool)
	{
		_tools[tool.Name] = tool;
	}

	internal IConfasterMCPTool? GetTool(string name)
	{
		_tools.TryGetValue(name, out IConfasterMCPTool value);
		return value;
	}

	internal List<object> GetAllDefinitions()
	{
		List<object> list = new List<object>();
		foreach (IConfasterMCPTool value in _tools.Values)
		{
			if (!DisabledTools.Contains(value.Name))
			{
				list.Add(new
				{
					name = value.Name,
					description = value.Description,
					inputSchema = value.InputSchema
				});
			}
		}
		return list;
	}
}
