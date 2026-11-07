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

	private ConfigRoots? _roots;

	internal static bool IndexAdminDisabled { get; set; }

	internal ConfigRoots? Roots => _roots;

	internal ToolRegistry(ConfigRoots roots, FileCache fileCache)
	{
		_roots = roots;
		_fileCache = fileCache;
		_db = OpenDatabase(roots);
		RegisterTools(roots);
		InitHelpTools();
	}

	internal ToolRegistry()
	{
		_db = null;
		_fileCache = null;
		InitHelpTools();
	}

	private static Database OpenDatabase(ConfigRoots roots)
	{
		return new Database(Path.Combine(roots.IndexRoot, ".index", "onec_search.db"));
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

	internal (int added, int updated, int deleted) Reinitialize(ConfigRoots roots, bool sync = true)
	{
		lock (_db.WriteLock)
		{
			_db.Dispose();
		}
		_fileCache.Clear();
		_roots = roots;
		_db = OpenDatabase(roots);
		_tools.Clear();
		IndexManager indexManager = RegisterTools(roots);
		if (_helpDb != null && _helpIndexer != null)
		{
			RegisterHelpTools(_helpDb, _helpIndexer);
		}
		if (!sync)
		{
			return (added: 0, updated: 0, deleted: 0);
		}
		return indexManager.IncrementalSync();
	}

	internal ConfigRoots? RefreshRoots()
	{
		ConfigRoots roots = _roots;
		if (roots == null)
		{
			return null;
		}
		ConfigRoots configRoots;
		try
		{
			configRoots = roots.Rebuild();
		}
		catch
		{
			return null;
		}
		if (configRoots == null || roots.LosesCertaintyTo(configRoots))
		{
			return null;
		}
		roots.AdoptBuildWarnings(configRoots);
		if (roots.SameSetAs(configRoots) || !roots.SamePathsAs(configRoots))
		{
			return null;
		}
		return configRoots;
	}

	private IndexManager RegisterTools(ConfigRoots roots)
	{
		Database db = _db;
		FileCache fileCache = _fileCache;
		FileScanner fileScanner = new FileScanner(db, roots, fileCache);
		IndexManager indexManager = new IndexManager(db, roots, fileCache, !IndexAdminDisabled);
		Register(new SemanticFindTool(db, fileScanner, fileCache));
		Register(new SearchCodeTool(fileScanner));
		Register(new FindSymbolTool(db, fileScanner));
		Register(new SmartFindTool(db, fileCache, fileScanner));
		Register(new GetSymbolContextTool(db, fileCache, fileScanner));
		Register(new GetFileContextTool(fileCache, fileScanner));
		Register(new ListObjectsTool(db, roots));
		Register(new GetObjectStructureTool(db, fileScanner));
		Register(new FindReferencesTool(fileScanner));
		Register(new ImpactAnalysisTool(fileScanner));
		Register(new GetFunctionContextTool(db, fileScanner));
		Register(new GetModuleFunctionsTool(db, fileScanner));
		Register(new FindFunctionInObjectTool(db, fileCache, fileScanner));
		Register(new StatsTool(db, roots));
		Register(new SyncIndexTool(this, indexManager));
		if (!IndexAdminDisabled)
		{
			Register(new BenchmarkTool(this));
			Register(new SwitchExportRootTool(this));
		}
		return indexManager;
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
