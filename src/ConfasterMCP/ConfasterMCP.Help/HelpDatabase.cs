using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace ConfasterMCP.Help;

internal sealed class HelpDatabase : IDisposable
{
	private readonly string _dbPath;

	private SqliteConnection _conn;

	internal HelpDatabase()
	{
		string text = Path.Combine(GetConfigDirectory(), "Confaster", "HelpIndex");
		Directory.CreateDirectory(text);
		_dbPath = Path.Combine(text, "help.db");
		_conn = OpenConnection(_dbPath);
	}

	// .NET на Linux возвращает ApplicationData только при заданном XDG_CONFIG_HOME,
	// иначе пустую строку — индекс справки уходил бы в текущий каталог процесса.
	private static string GetConfigDirectory()
	{
		string text = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		if (string.IsNullOrWhiteSpace(text))
		{
			string text2 = Environment.GetEnvironmentVariable("HOME");
			if (!string.IsNullOrWhiteSpace(text2))
			{
				text = Path.Combine(text2, ".config");
			}
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			text = Path.GetTempPath();
		}
		return text;
	}

	internal void Recreate()
	{
		try
		{
			_conn.Close();
		}
		catch
		{
		}
		try
		{
			_conn.Dispose();
		}
		catch
		{
		}
		string[] array = new string[3] { "", "-wal", "-shm" };
		foreach (string text in array)
		{
			try
			{
				File.Delete(_dbPath + text);
			}
			catch
			{
			}
		}
		_conn = OpenConnection(_dbPath);
	}

	private static SqliteConnection OpenConnection(string dbPath)
	{
		SqliteConnection sqliteConnection = new SqliteConnection("Data Source=" + dbPath);
		sqliteConnection.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "PRAGMA journal_mode=WAL";
		sqliteCommand.ExecuteNonQuery();
		using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
		sqliteCommand2.CommandText = "PRAGMA synchronous=NORMAL";
		sqliteCommand2.ExecuteNonQuery();
		using SqliteCommand sqliteCommand3 = sqliteConnection.CreateCommand();
		sqliteCommand3.CommandText = "CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT)";
		sqliteCommand3.ExecuteNonQuery();
		using SqliteCommand sqliteCommand4 = sqliteConnection.CreateCommand();
		sqliteCommand4.CommandText = "CREATE VIRTUAL TABLE IF NOT EXISTS topics\n                USING fts5(topic_id, title, content, category, version, tokenize='unicode61')";
		sqliteCommand4.ExecuteNonQuery();
		return sqliteConnection;
	}

	internal string? GetMeta(string key)
	{
		using SqliteCommand sqliteCommand = CreateCommand("SELECT value FROM meta WHERE key = @k");
		sqliteCommand.Parameters.AddWithValue("@k", key);
		return sqliteCommand.ExecuteScalar() as string;
	}

	internal void SetMeta(string key, string value)
	{
		using SqliteCommand sqliteCommand = CreateCommand("INSERT OR REPLACE INTO meta (key, value) VALUES (@k, @v)");
		sqliteCommand.Parameters.AddWithValue("@k", key);
		sqliteCommand.Parameters.AddWithValue("@v", value);
		sqliteCommand.ExecuteNonQuery();
	}

	internal void DeleteTopicsByVersion(string version)
	{
		using SqliteCommand sqliteCommand = CreateCommand("DELETE FROM topics WHERE version = @v");
		sqliteCommand.Parameters.AddWithValue("@v", version);
		sqliteCommand.ExecuteNonQuery();
	}

	internal void InsertTopic(string topicId, string title, string content, string category, string version)
	{
		using SqliteCommand sqliteCommand = CreateCommand("INSERT INTO topics (topic_id, title, content, category, version) VALUES (@id, @t, @c, @cat, @v)");
		sqliteCommand.Parameters.AddWithValue("@id", topicId);
		sqliteCommand.Parameters.AddWithValue("@t", title);
		sqliteCommand.Parameters.AddWithValue("@c", content);
		sqliteCommand.Parameters.AddWithValue("@cat", category);
		sqliteCommand.Parameters.AddWithValue("@v", version);
		sqliteCommand.ExecuteNonQuery();
	}

	internal long GetTopicCount()
	{
		using SqliteCommand sqliteCommand = CreateCommand("SELECT COUNT(*) FROM topics");
		return (long)(sqliteCommand.ExecuteScalar() ?? ((object)0L));
	}

	internal List<HelpSearchResult> Search(string query, int limit = 5, string? category = null)
	{
		// Сначала фразовый поиск (как в оригинале). Если фраза не нашлась,
		// повторяем как AND по словам: запросы вида «ВЫБРАТЬ ИЗ ГДЕ» иначе
		// дают ноль, хотя все слова в теме есть.
		foreach (string item in BuildMatchQueries(query))
		{
			try
			{
				List<HelpSearchResult> list = SearchFts(item, limit, category);
				if (list.Count > 0)
				{
					return list;
				}
			}
			catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
			{
				break;
			}
		}
		return SearchLike(query, limit);
	}

	private static IEnumerable<string> BuildMatchQueries(string query)
	{
		string text = query.Trim();
		if (text.Length == 0)
		{
			yield break;
		}
		yield return QuoteFts(text);
		string[] array = text.Split(new char[3] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length > 1)
		{
			yield return string.Join(" ", array.Select(QuoteFts));
		}
	}

	private static string QuoteFts(string term)
	{
		return "\"" + term.Replace("\"", "\"\"") + "\"";
	}

	private List<HelpSearchResult> SearchFts(string matchQuery, int limit, string? category)
	{
		List<HelpSearchResult> list = new List<HelpSearchResult>();
		string sql = ((!string.IsNullOrEmpty(category) && !(category == "all")) ? "SELECT topic_id, title, snippet(topics, 2, '>>', '<<', '...', 30) as excerpt\n                        FROM topics WHERE topics MATCH @q AND category = @cat ORDER BY rank LIMIT @l" : "SELECT topic_id, title, snippet(topics, 2, '>>', '<<', '...', 30) as excerpt\n                        FROM topics WHERE topics MATCH @q ORDER BY rank LIMIT @l");
		using SqliteCommand sqliteCommand = CreateCommand(sql);
		sqliteCommand.Parameters.AddWithValue("@q", matchQuery);
		sqliteCommand.Parameters.AddWithValue("@l", limit);
		if (!string.IsNullOrEmpty(category) && category != "all")
		{
			sqliteCommand.Parameters.AddWithValue("@cat", category);
		}
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			list.Add(new HelpSearchResult
			{
				TopicId = sqliteDataReader.GetString(0),
				Title = sqliteDataReader.GetString(1),
				Excerpt = sqliteDataReader.GetString(2)
			});
		}
		return list;
	}

	private List<HelpSearchResult> SearchLike(string query, int limit)
	{
		List<HelpSearchResult> list = new List<HelpSearchResult>();
		string sql2 = "SELECT topic_id, title, substr(content, 1, 200) as excerpt\n            FROM topics WHERE title LIKE @q2 OR content LIKE @q2 LIMIT @l";
		using SqliteCommand sqliteCommand2 = CreateCommand(sql2);
		sqliteCommand2.Parameters.AddWithValue("@q2", "%" + query + "%");
		sqliteCommand2.Parameters.AddWithValue("@l", limit);
		using SqliteDataReader sqliteDataReader2 = sqliteCommand2.ExecuteReader();
		while (sqliteDataReader2.Read())
		{
			list.Add(new HelpSearchResult
			{
				TopicId = sqliteDataReader2.GetString(0),
				Title = sqliteDataReader2.GetString(1),
				Excerpt = sqliteDataReader2.GetString(2)
			});
		}
		return list;
	}

	internal (string? title, string? content) GetTopic(string topicId)
	{
		using SqliteCommand sqliteCommand = CreateCommand("SELECT title, content FROM topics WHERE topic_id = @id");
		sqliteCommand.Parameters.AddWithValue("@id", topicId);
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		if (sqliteDataReader.Read())
		{
			return (title: sqliteDataReader.GetString(0), content: sqliteDataReader.GetString(1));
		}
		return (title: null, content: null);
	}

	internal void ClearAll()
	{
		Execute("DELETE FROM topics");
		Execute("DELETE FROM meta");
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

	private void Execute(string sql)
	{
		using SqliteCommand sqliteCommand = _conn.CreateCommand();
		sqliteCommand.CommandText = sql;
		sqliteCommand.ExecuteNonQuery();
	}

	private SqliteCommand CreateCommand(string sql)
	{
		SqliteCommand sqliteCommand = _conn.CreateCommand();
		sqliteCommand.CommandText = sql;
		return sqliteCommand;
	}

	public void Dispose()
	{
		_conn.Dispose();
	}
}
