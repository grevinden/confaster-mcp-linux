using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace ConfasterMCP.Indexing;

internal sealed class Database : IDisposable
{
	internal sealed class OwnedReader : IDisposable
	{
		private readonly SqliteCommand _cmd;

		private readonly SqliteDataReader _reader;

		internal OwnedReader(SqliteCommand cmd, SqliteDataReader reader)
		{
			_cmd = cmd;
			_reader = reader;
		}

		internal bool Read()
		{
			return _reader.Read();
		}

		internal string GetString(int ordinal)
		{
			return _reader.GetString(ordinal);
		}

		internal int GetInt32(int ordinal)
		{
			return _reader.GetInt32(ordinal);
		}

		internal long GetInt64(int ordinal)
		{
			return _reader.GetInt64(ordinal);
		}

		internal double GetDouble(int ordinal)
		{
			return _reader.GetDouble(ordinal);
		}

		internal bool IsDBNull(int ordinal)
		{
			return _reader.IsDBNull(ordinal);
		}

		public void Dispose()
		{
			_reader.Dispose();
			_cmd.Dispose();
		}
	}

	private readonly SqliteConnection _conn;

	private readonly object _writeLock = new object();

	private SqliteCommand? _insertSymbolCmd;

	private SqliteCommand? _lastRowIdCmd;

	private SqliteCommand? _insertSymbolFtsCmd;

	private SqliteCommand? _insertSymbolTermsCmd;

	private SqliteCommand? _insertCallCmd;

	private SqliteCommand? _insertRegionCmd;

	private SqliteCommand? _upsertIndexedFileCmd;

	private SqliteCommand? _delSymbolsFtsByFileCmd;

	private SqliteCommand? _delSymbolTermsByFileCmd;

	private SqliteCommand? _delCallsByFileCmd;

	private SqliteCommand? _delSymbolWeightsByFileCmd;

	private SqliteCommand? _delSymbolsByFileCmd;

	private SqliteCommand? _delRegionsByFileCmd;

	private const string ObjectsSchemaVersion = "2";

	internal object WriteLock => _writeLock;

	internal Database(string dbPath)
	{
		string directoryName = Path.GetDirectoryName(dbPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		_conn = new SqliteConnection("Data Source=" + dbPath);
		_conn.Open();
		Execute("PRAGMA journal_mode=WAL");
		Execute("PRAGMA synchronous=NORMAL");
		Execute("PRAGMA cache_size=-65536");
		Execute("PRAGMA foreign_keys=ON");
		Execute("PRAGMA busy_timeout=60000");
		if (IsSchemaCurrent())
		{
			InitSchema();
			return;
		}
		Execute("BEGIN IMMEDIATE");
		try
		{
			InitSchema();
			Execute("COMMIT");
		}
		catch
		{
			try
			{
				Execute("ROLLBACK");
			}
			catch
			{
			}
			throw;
		}
	}

	private bool IsSchemaCurrent()
	{
		try
		{
			if (QueryScalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='meta'") == 0L)
			{
				return false;
			}
			return GetMeta("objects_schema_version") == "2" && HasColumn("symbols", "annotation_target_lower");
		}
		catch
		{
			return false;
		}
	}

	private void MigrateSchema()
	{
		bool num = QueryScalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='meta'") > 0;
		bool flag = QueryScalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='objects'") > 0;
		string text = (num ? GetMeta("objects_schema_version") : null);
		if (flag && text != "2")
		{
			Execute("DROP TABLE IF EXISTS object_synonyms");
			Execute("DROP TABLE IF EXISTS object_items");
			Execute("DROP TABLE IF EXISTS objects");
			Execute("DROP TABLE IF EXISTS synonyms_fts");
		}
		if (QueryScalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='symbols'") > 0 && !HasColumn("symbols", "annotation"))
		{
			Execute("ALTER TABLE symbols ADD COLUMN annotation TEXT");
			Execute("ALTER TABLE symbols ADD COLUMN annotation_target TEXT");
			Execute("ALTER TABLE symbols ADD COLUMN annotation_target_lower TEXT");
		}
	}

	private bool HasColumn(string table, string column)
	{
		using SqliteCommand sqliteCommand = CreateCommand("PRAGMA table_info(" + table + ")");
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			if (string.Equals(sqliteDataReader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private void InitSchema()
	{
		MigrateSchema();
		Execute("\n                CREATE TABLE IF NOT EXISTS indexed_files (\n                    path TEXT PRIMARY KEY,\n                    mtime INTEGER NOT NULL,\n                    size INTEGER NOT NULL\n                )");
		Execute("\n                CREATE TABLE IF NOT EXISTS symbols (\n                    id INTEGER PRIMARY KEY AUTOINCREMENT,\n                    file_path TEXT NOT NULL,\n                    name TEXT NOT NULL,\n                    name_lower TEXT NOT NULL,\n                    kind TEXT NOT NULL,\n                    start_line INTEGER NOT NULL,\n                    end_line INTEGER NOT NULL,\n                    is_export INTEGER NOT NULL DEFAULT 0,\n                    is_async INTEGER NOT NULL DEFAULT 0,\n                    directive TEXT,\n                    annotation TEXT,\n                    annotation_target TEXT,\n                    annotation_target_lower TEXT\n                )");
		Execute("CREATE INDEX IF NOT EXISTS idx_symbols_file ON symbols(file_path)");
		Execute("CREATE INDEX IF NOT EXISTS idx_symbols_name ON symbols(name_lower)");
		Execute("CREATE INDEX IF NOT EXISTS idx_symbols_annotation_target ON symbols(annotation_target_lower)");
		Execute("\n                CREATE TABLE IF NOT EXISTS calls (\n                    id INTEGER PRIMARY KEY AUTOINCREMENT,\n                    caller_id INTEGER NOT NULL,\n                    callee_name TEXT NOT NULL,\n                    callee_name_lower TEXT NOT NULL,\n                    line_number INTEGER NOT NULL,\n                    FOREIGN KEY (caller_id) REFERENCES symbols(id) ON DELETE CASCADE\n                )");
		Execute("CREATE INDEX IF NOT EXISTS idx_calls_caller ON calls(caller_id)");
		Execute("CREATE INDEX IF NOT EXISTS idx_calls_callee ON calls(callee_name_lower)");
		Execute("\n                CREATE TABLE IF NOT EXISTS objects (\n                    id INTEGER PRIMARY KEY AUTOINCREMENT,\n                    config TEXT NOT NULL DEFAULT '',\n                    fqn TEXT NOT NULL,\n                    type_folder TEXT NOT NULL,\n                    name TEXT NOT NULL,\n                    name_lower TEXT NOT NULL,\n                    export_path TEXT,\n                    adopted INTEGER NOT NULL DEFAULT 0,\n                    UNIQUE (config, fqn)\n                )");
		Execute("CREATE INDEX IF NOT EXISTS idx_objects_fqn ON objects(fqn)");
		Execute("CREATE INDEX IF NOT EXISTS idx_objects_type ON objects(type_folder)");
		Execute("CREATE INDEX IF NOT EXISTS idx_objects_name ON objects(name_lower)");
		Execute("\n                CREATE TABLE IF NOT EXISTS object_items (\n                    id INTEGER PRIMARY KEY AUTOINCREMENT,\n                    object_id INTEGER NOT NULL,\n                    kind TEXT NOT NULL,\n                    name TEXT NOT NULL,\n                    parent_name TEXT,\n                    type_info TEXT,\n                    adopted INTEGER NOT NULL DEFAULT 0,\n                    FOREIGN KEY (object_id) REFERENCES objects(id) ON DELETE CASCADE\n                )");
		Execute("CREATE INDEX IF NOT EXISTS idx_items_object ON object_items(object_id)");
		Execute("\n                CREATE TABLE IF NOT EXISTS object_synonyms (\n                    id INTEGER PRIMARY KEY AUTOINCREMENT,\n                    object_id INTEGER NOT NULL,\n                    synonym TEXT NOT NULL,\n                    FOREIGN KEY (object_id) REFERENCES objects(id) ON DELETE CASCADE\n                )");
		Execute("\n                CREATE TABLE IF NOT EXISTS regions (\n                    id INTEGER PRIMARY KEY AUTOINCREMENT,\n                    file_path TEXT NOT NULL,\n                    name TEXT NOT NULL,\n                    start_line INTEGER NOT NULL,\n                    end_line INTEGER NOT NULL\n                )");
		Execute("CREATE INDEX IF NOT EXISTS idx_regions_file ON regions(file_path)");
		Execute("\n                CREATE TABLE IF NOT EXISTS meta (\n                    key TEXT PRIMARY KEY,\n                    value TEXT\n                )");
		Execute("\n                CREATE VIRTUAL TABLE IF NOT EXISTS symbols_fts\n                USING fts5(name, tokenize='trigram')");
		Execute("\n                CREATE VIRTUAL TABLE IF NOT EXISTS synonyms_fts\n                USING fts5(synonym, tokenize='trigram')");
		Execute("\n                CREATE VIRTUAL TABLE IF NOT EXISTS symbol_terms\n                USING fts5(\n                    symbol_id UNINDEXED,\n                    name_tokens,\n                    comment_head,\n                    param_tokens,\n                    tokenize='unicode61 remove_diacritics 1'\n                )");
		Execute("\n                CREATE TABLE IF NOT EXISTS symbol_weights (\n                    symbol_id INTEGER PRIMARY KEY,\n                    call_count INTEGER NOT NULL DEFAULT 0,\n                    caller_diversity INTEGER NOT NULL DEFAULT 0\n                )");
		Execute("\n                CREATE TABLE IF NOT EXISTS domain_aliases (\n                    id INTEGER PRIMARY KEY AUTOINCREMENT,\n                    term TEXT NOT NULL,\n                    alias TEXT NOT NULL,\n                    weight REAL NOT NULL DEFAULT 1.0\n                )");
		Execute("CREATE INDEX IF NOT EXISTS idx_alias_term ON domain_aliases(term)");
		if (GetMeta("objects_schema_version") != "2")
		{
			SetMeta("objects_schema_version", "2");
		}
	}

	internal void Execute(string sql)
	{
		using SqliteCommand sqliteCommand = _conn.CreateCommand();
		sqliteCommand.CommandText = sql;
		sqliteCommand.ExecuteNonQuery();
	}

	internal SqliteCommand CreateCommand(string sql)
	{
		SqliteCommand sqliteCommand = _conn.CreateCommand();
		sqliteCommand.CommandText = sql;
		return sqliteCommand;
	}

	internal void BeginTransaction()
	{
		Execute("BEGIN TRANSACTION");
	}

	internal void Commit()
	{
		Execute("COMMIT");
	}

	internal void Rollback()
	{
		Execute("ROLLBACK");
	}

	internal Dictionary<string, (long mtime, long size)> GetIndexedFiles()
	{
		Dictionary<string, (long, long)> dictionary = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
		using SqliteCommand sqliteCommand = CreateCommand("SELECT path, mtime, size FROM indexed_files");
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			dictionary[sqliteDataReader.GetString(0)] = (sqliteDataReader.GetInt64(1), sqliteDataReader.GetInt64(2));
		}
		return dictionary;
	}

	internal void UpsertIndexedFile(string path, long mtime, long size)
	{
		if (_upsertIndexedFileCmd == null)
		{
			SqliteCommand sqliteCommand = _conn.CreateCommand();
			sqliteCommand.CommandText = "INSERT OR REPLACE INTO indexed_files (path, mtime, size) VALUES (@p, @m, @s)";
			sqliteCommand.Parameters.Add("@p", SqliteType.Text);
			sqliteCommand.Parameters.Add("@m", SqliteType.Integer);
			sqliteCommand.Parameters.Add("@s", SqliteType.Integer);
			sqliteCommand.Prepare();
			_upsertIndexedFileCmd = sqliteCommand;
		}
		_upsertIndexedFileCmd.Parameters["@p"].Value = path;
		_upsertIndexedFileCmd.Parameters["@m"].Value = mtime;
		_upsertIndexedFileCmd.Parameters["@s"].Value = size;
		_upsertIndexedFileCmd.ExecuteNonQuery();
	}

	internal void DeleteIndexedFile(string path)
	{
		using SqliteCommand sqliteCommand = CreateCommand("DELETE FROM indexed_files WHERE path = @p");
		sqliteCommand.Parameters.AddWithValue("@p", path);
		sqliteCommand.ExecuteNonQuery();
	}

	internal long InsertSymbol(string filePath, string name, string kind, int startLine, int endLine, bool isExport, bool isAsync, string? directive, string? annotation = null, string? annotationTarget = null)
	{
		if (_insertSymbolCmd == null)
		{
			SqliteCommand sqliteCommand = _conn.CreateCommand();
			sqliteCommand.CommandText = "\n                    INSERT INTO symbols (file_path, name, name_lower, kind, start_line, end_line, is_export, is_async, directive,\n                                         annotation, annotation_target, annotation_target_lower)\n                    VALUES (@fp, @n, @nl, @k, @sl, @el, @exp, @asy, @dir, @ann, @at, @atl)";
			sqliteCommand.Parameters.Add("@fp", SqliteType.Text);
			sqliteCommand.Parameters.Add("@n", SqliteType.Text);
			sqliteCommand.Parameters.Add("@nl", SqliteType.Text);
			sqliteCommand.Parameters.Add("@k", SqliteType.Text);
			sqliteCommand.Parameters.Add("@sl", SqliteType.Integer);
			sqliteCommand.Parameters.Add("@el", SqliteType.Integer);
			sqliteCommand.Parameters.Add("@exp", SqliteType.Integer);
			sqliteCommand.Parameters.Add("@asy", SqliteType.Integer);
			sqliteCommand.Parameters.Add("@dir", SqliteType.Text);
			sqliteCommand.Parameters.Add("@ann", SqliteType.Text);
			sqliteCommand.Parameters.Add("@at", SqliteType.Text);
			sqliteCommand.Parameters.Add("@atl", SqliteType.Text);
			sqliteCommand.Prepare();
			_insertSymbolCmd = sqliteCommand;
		}
		_insertSymbolCmd.Parameters["@fp"].Value = filePath;
		_insertSymbolCmd.Parameters["@n"].Value = name;
		_insertSymbolCmd.Parameters["@nl"].Value = name.ToLowerInvariant();
		_insertSymbolCmd.Parameters["@k"].Value = kind;
		_insertSymbolCmd.Parameters["@sl"].Value = startLine;
		_insertSymbolCmd.Parameters["@el"].Value = endLine;
		_insertSymbolCmd.Parameters["@exp"].Value = (isExport ? 1 : 0);
		_insertSymbolCmd.Parameters["@asy"].Value = (isAsync ? 1 : 0);
		_insertSymbolCmd.Parameters["@dir"].Value = ((object)directive) ?? ((object)DBNull.Value);
		_insertSymbolCmd.Parameters["@ann"].Value = ((object)annotation) ?? ((object)DBNull.Value);
		_insertSymbolCmd.Parameters["@at"].Value = ((object)annotationTarget) ?? ((object)DBNull.Value);
		_insertSymbolCmd.Parameters["@atl"].Value = ((object)annotationTarget?.ToLowerInvariant()) ?? ((object)DBNull.Value);
		_insertSymbolCmd.ExecuteNonQuery();
		if (_lastRowIdCmd == null)
		{
			SqliteCommand sqliteCommand2 = _conn.CreateCommand();
			sqliteCommand2.CommandText = "SELECT last_insert_rowid()";
			sqliteCommand2.Prepare();
			_lastRowIdCmd = sqliteCommand2;
		}
		long num = (long)_lastRowIdCmd.ExecuteScalar();
		if (_insertSymbolFtsCmd == null)
		{
			SqliteCommand sqliteCommand3 = _conn.CreateCommand();
			sqliteCommand3.CommandText = "INSERT INTO symbols_fts (rowid, name) VALUES (@id, @n)";
			sqliteCommand3.Parameters.Add("@id", SqliteType.Integer);
			sqliteCommand3.Parameters.Add("@n", SqliteType.Text);
			sqliteCommand3.Prepare();
			_insertSymbolFtsCmd = sqliteCommand3;
		}
		_insertSymbolFtsCmd.Parameters["@id"].Value = num;
		_insertSymbolFtsCmd.Parameters["@n"].Value = name;
		_insertSymbolFtsCmd.ExecuteNonQuery();
		return num;
	}

	internal void DeleteSymbolsByFile(string filePath)
	{
		if (_delSymbolsFtsByFileCmd == null)
		{
			SqliteCommand sqliteCommand = _conn.CreateCommand();
			sqliteCommand.CommandText = "DELETE FROM symbols_fts WHERE rowid IN (SELECT id FROM symbols WHERE file_path = @fp)";
			sqliteCommand.Parameters.Add("@fp", SqliteType.Text);
			sqliteCommand.Prepare();
			_delSymbolsFtsByFileCmd = sqliteCommand;
		}
		_delSymbolsFtsByFileCmd.Parameters["@fp"].Value = filePath;
		_delSymbolsFtsByFileCmd.ExecuteNonQuery();
		if (_delSymbolTermsByFileCmd == null)
		{
			SqliteCommand sqliteCommand2 = _conn.CreateCommand();
			sqliteCommand2.CommandText = "DELETE FROM symbol_terms WHERE symbol_id IN (SELECT CAST(id AS TEXT) FROM symbols WHERE file_path = @fp)";
			sqliteCommand2.Parameters.Add("@fp", SqliteType.Text);
			sqliteCommand2.Prepare();
			_delSymbolTermsByFileCmd = sqliteCommand2;
		}
		_delSymbolTermsByFileCmd.Parameters["@fp"].Value = filePath;
		_delSymbolTermsByFileCmd.ExecuteNonQuery();
		if (_delCallsByFileCmd == null)
		{
			SqliteCommand sqliteCommand3 = _conn.CreateCommand();
			sqliteCommand3.CommandText = "DELETE FROM calls WHERE caller_id IN (SELECT id FROM symbols WHERE file_path = @fp)";
			sqliteCommand3.Parameters.Add("@fp", SqliteType.Text);
			sqliteCommand3.Prepare();
			_delCallsByFileCmd = sqliteCommand3;
		}
		_delCallsByFileCmd.Parameters["@fp"].Value = filePath;
		_delCallsByFileCmd.ExecuteNonQuery();
		if (_delSymbolWeightsByFileCmd == null)
		{
			SqliteCommand sqliteCommand4 = _conn.CreateCommand();
			sqliteCommand4.CommandText = "DELETE FROM symbol_weights WHERE symbol_id IN (SELECT id FROM symbols WHERE file_path = @fp)";
			sqliteCommand4.Parameters.Add("@fp", SqliteType.Text);
			sqliteCommand4.Prepare();
			_delSymbolWeightsByFileCmd = sqliteCommand4;
		}
		_delSymbolWeightsByFileCmd.Parameters["@fp"].Value = filePath;
		_delSymbolWeightsByFileCmd.ExecuteNonQuery();
		if (_delSymbolsByFileCmd == null)
		{
			SqliteCommand sqliteCommand5 = _conn.CreateCommand();
			sqliteCommand5.CommandText = "DELETE FROM symbols WHERE file_path = @fp";
			sqliteCommand5.Parameters.Add("@fp", SqliteType.Text);
			sqliteCommand5.Prepare();
			_delSymbolsByFileCmd = sqliteCommand5;
		}
		_delSymbolsByFileCmd.Parameters["@fp"].Value = filePath;
		_delSymbolsByFileCmd.ExecuteNonQuery();
	}

	internal void InsertSymbolTerms(long symbolId, string nameTokens, string commentHead, string paramTokens)
	{
		if (_insertSymbolTermsCmd == null)
		{
			SqliteCommand sqliteCommand = _conn.CreateCommand();
			sqliteCommand.CommandText = "\n                    INSERT INTO symbol_terms (symbol_id, name_tokens, comment_head, param_tokens)\n                    VALUES (@sid, @nt, @ch, @pt)";
			sqliteCommand.Parameters.Add("@sid", SqliteType.Text);
			sqliteCommand.Parameters.Add("@nt", SqliteType.Text);
			sqliteCommand.Parameters.Add("@ch", SqliteType.Text);
			sqliteCommand.Parameters.Add("@pt", SqliteType.Text);
			sqliteCommand.Prepare();
			_insertSymbolTermsCmd = sqliteCommand;
		}
		_insertSymbolTermsCmd.Parameters["@sid"].Value = symbolId.ToString();
		_insertSymbolTermsCmd.Parameters["@nt"].Value = nameTokens ?? "";
		_insertSymbolTermsCmd.Parameters["@ch"].Value = commentHead ?? "";
		_insertSymbolTermsCmd.Parameters["@pt"].Value = paramTokens ?? "";
		_insertSymbolTermsCmd.ExecuteNonQuery();
	}

	internal void RebuildSymbolWeights()
	{
		Execute("DELETE FROM symbol_weights");
		Execute("\n                INSERT INTO symbol_weights (symbol_id, call_count, caller_diversity)\n                SELECT s.id,\n                       COUNT(c.id),\n                       COUNT(DISTINCT cs.file_path)\n                FROM symbols s\n                LEFT JOIN calls c ON c.callee_name_lower = s.name_lower\n                LEFT JOIN symbols cs ON cs.id = c.caller_id\n                GROUP BY s.id");
	}

	internal void InsertDomainAlias(string term, string alias, double weight)
	{
		using SqliteCommand sqliteCommand = CreateCommand("INSERT INTO domain_aliases (term, alias, weight) VALUES (@t, @a, @w)");
		sqliteCommand.Parameters.AddWithValue("@t", term);
		sqliteCommand.Parameters.AddWithValue("@a", alias);
		sqliteCommand.Parameters.AddWithValue("@w", weight);
		sqliteCommand.ExecuteNonQuery();
	}

	internal void InsertCall(long callerId, string calleeName, int lineNumber)
	{
		if (_insertCallCmd == null)
		{
			SqliteCommand sqliteCommand = _conn.CreateCommand();
			sqliteCommand.CommandText = "INSERT INTO calls (caller_id, callee_name, callee_name_lower, line_number) VALUES (@cid, @cn, @cnl, @ln)";
			sqliteCommand.Parameters.Add("@cid", SqliteType.Integer);
			sqliteCommand.Parameters.Add("@cn", SqliteType.Text);
			sqliteCommand.Parameters.Add("@cnl", SqliteType.Text);
			sqliteCommand.Parameters.Add("@ln", SqliteType.Integer);
			sqliteCommand.Prepare();
			_insertCallCmd = sqliteCommand;
		}
		_insertCallCmd.Parameters["@cid"].Value = callerId;
		_insertCallCmd.Parameters["@cn"].Value = calleeName;
		_insertCallCmd.Parameters["@cnl"].Value = calleeName.ToLowerInvariant();
		_insertCallCmd.Parameters["@ln"].Value = lineNumber;
		_insertCallCmd.ExecuteNonQuery();
	}

	internal void ClearObjects(IEnumerable<string> configs)
	{
		List<string> list = configs.ToList();
		if (list.Count == 0)
		{
			return;
		}
		string text = string.Join(", ", list.Select((string _, int i) => "@c" + i));
		string text2 = "SELECT id FROM objects WHERE config IN (" + text + ")";
		string[] array = new string[4]
		{
			"DELETE FROM synonyms_fts WHERE rowid IN (SELECT id FROM object_synonyms WHERE object_id IN (" + text2 + "))",
			"DELETE FROM object_synonyms WHERE object_id IN (" + text2 + ")",
			"DELETE FROM object_items WHERE object_id IN (" + text2 + ")",
			"DELETE FROM objects WHERE config IN (" + text + ")"
		};
		foreach (string sql in array)
		{
			using SqliteCommand sqliteCommand = CreateCommand(sql);
			for (int num2 = 0; num2 < list.Count; num2++)
			{
				sqliteCommand.Parameters.AddWithValue("@c" + num2, list[num2]);
			}
			sqliteCommand.ExecuteNonQuery();
		}
	}

	internal long InsertObject(string config, string fqn, string typeFolder, string name, string? exportPath, bool adopted)
	{
		using SqliteCommand sqliteCommand = CreateCommand("\n                INSERT OR IGNORE INTO objects (config, fqn, type_folder, name, name_lower, export_path, adopted)\n                VALUES (@c, @f, @t, @n, @nl, @e, @a)");
		sqliteCommand.Parameters.AddWithValue("@c", config);
		sqliteCommand.Parameters.AddWithValue("@f", fqn);
		sqliteCommand.Parameters.AddWithValue("@t", typeFolder);
		sqliteCommand.Parameters.AddWithValue("@n", name);
		sqliteCommand.Parameters.AddWithValue("@nl", name.ToLowerInvariant());
		sqliteCommand.Parameters.AddWithValue("@e", ((object)exportPath) ?? ((object)DBNull.Value));
		sqliteCommand.Parameters.AddWithValue("@a", adopted ? 1 : 0);
		if (sqliteCommand.ExecuteNonQuery() == 0)
		{
			return 0L;
		}
		using SqliteCommand sqliteCommand2 = CreateCommand("SELECT last_insert_rowid()");
		return (long)sqliteCommand2.ExecuteScalar();
	}

	internal void InsertObjectItem(long objectId, string kind, string name, string? parentName, string? typeInfo, bool adopted)
	{
		using SqliteCommand sqliteCommand = CreateCommand("INSERT INTO object_items (object_id, kind, name, parent_name, type_info, adopted) VALUES (@o, @k, @n, @p, @t, @a)");
		sqliteCommand.Parameters.AddWithValue("@o", objectId);
		sqliteCommand.Parameters.AddWithValue("@k", kind);
		sqliteCommand.Parameters.AddWithValue("@n", name);
		sqliteCommand.Parameters.AddWithValue("@p", ((object)parentName) ?? ((object)DBNull.Value));
		sqliteCommand.Parameters.AddWithValue("@t", ((object)typeInfo) ?? ((object)DBNull.Value));
		sqliteCommand.Parameters.AddWithValue("@a", adopted ? 1 : 0);
		sqliteCommand.ExecuteNonQuery();
	}

	internal void InsertSynonym(long objectId, string synonym)
	{
		using SqliteCommand sqliteCommand = CreateCommand("INSERT INTO object_synonyms (object_id, synonym) VALUES (@o, @s)");
		sqliteCommand.Parameters.AddWithValue("@o", objectId);
		sqliteCommand.Parameters.AddWithValue("@s", synonym);
		sqliteCommand.ExecuteNonQuery();
		using SqliteCommand sqliteCommand2 = CreateCommand("SELECT last_insert_rowid()");
		long num = (long)sqliteCommand2.ExecuteScalar();
		using SqliteCommand sqliteCommand3 = CreateCommand("INSERT INTO synonyms_fts (rowid, synonym) VALUES (@id, @s)");
		sqliteCommand3.Parameters.AddWithValue("@id", num);
		sqliteCommand3.Parameters.AddWithValue("@s", synonym);
		sqliteCommand3.ExecuteNonQuery();
	}

	internal void DeleteRegionsByFile(string filePath)
	{
		if (_delRegionsByFileCmd == null)
		{
			SqliteCommand sqliteCommand = _conn.CreateCommand();
			sqliteCommand.CommandText = "DELETE FROM regions WHERE file_path = @fp";
			sqliteCommand.Parameters.Add("@fp", SqliteType.Text);
			sqliteCommand.Prepare();
			_delRegionsByFileCmd = sqliteCommand;
		}
		_delRegionsByFileCmd.Parameters["@fp"].Value = filePath;
		_delRegionsByFileCmd.ExecuteNonQuery();
	}

	internal void InsertRegion(string filePath, string name, int startLine, int endLine)
	{
		if (_insertRegionCmd == null)
		{
			SqliteCommand sqliteCommand = _conn.CreateCommand();
			sqliteCommand.CommandText = "INSERT INTO regions (file_path, name, start_line, end_line) VALUES (@fp, @n, @sl, @el)";
			sqliteCommand.Parameters.Add("@fp", SqliteType.Text);
			sqliteCommand.Parameters.Add("@n", SqliteType.Text);
			sqliteCommand.Parameters.Add("@sl", SqliteType.Integer);
			sqliteCommand.Parameters.Add("@el", SqliteType.Integer);
			sqliteCommand.Prepare();
			_insertRegionCmd = sqliteCommand;
		}
		_insertRegionCmd.Parameters["@fp"].Value = filePath;
		_insertRegionCmd.Parameters["@n"].Value = name;
		_insertRegionCmd.Parameters["@sl"].Value = startLine;
		_insertRegionCmd.Parameters["@el"].Value = endLine;
		_insertRegionCmd.ExecuteNonQuery();
	}

	internal void SetMeta(string key, string value)
	{
		using SqliteCommand sqliteCommand = CreateCommand("INSERT OR REPLACE INTO meta (key, value) VALUES (@k, @v)");
		sqliteCommand.Parameters.AddWithValue("@k", key);
		sqliteCommand.Parameters.AddWithValue("@v", value);
		sqliteCommand.ExecuteNonQuery();
	}

	internal string? GetMeta(string key)
	{
		using SqliteCommand sqliteCommand = CreateCommand("SELECT value FROM meta WHERE key = @k");
		sqliteCommand.Parameters.AddWithValue("@k", key);
		return sqliteCommand.ExecuteScalar() as string;
	}

	internal OwnedReader Query(string sql, params (string name, object value)[] parameters)
	{
		SqliteCommand sqliteCommand = CreateCommand(sql);
		for (int i = 0; i < parameters.Length; i++)
		{
			var (parameterName, value) = parameters[i];
			sqliteCommand.Parameters.AddWithValue(parameterName, value);
		}
		SqliteDataReader reader = sqliteCommand.ExecuteReader();
		return new OwnedReader(sqliteCommand, reader);
	}

	internal long QueryScalar(string sql)
	{
		using SqliteCommand sqliteCommand = CreateCommand(sql);
		return (long)(sqliteCommand.ExecuteScalar() ?? ((object)0L));
	}

	public void Dispose()
	{
		_insertSymbolCmd?.Dispose();
		_lastRowIdCmd?.Dispose();
		_insertSymbolFtsCmd?.Dispose();
		_insertSymbolTermsCmd?.Dispose();
		_insertCallCmd?.Dispose();
		_insertRegionCmd?.Dispose();
		_upsertIndexedFileCmd?.Dispose();
		_delSymbolsFtsByFileCmd?.Dispose();
		_delSymbolTermsByFileCmd?.Dispose();
		_delCallsByFileCmd?.Dispose();
		_delSymbolWeightsByFileCmd?.Dispose();
		_delSymbolsByFileCmd?.Dispose();
		_delRegionsByFileCmd?.Dispose();
		_conn.Dispose();
	}
}
