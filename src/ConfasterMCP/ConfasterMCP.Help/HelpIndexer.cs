using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ConfasterMCP.Help;

internal sealed class HelpIndexer
{
	private readonly HelpDatabase _db;

	private int _indexingFlag;

	internal bool IsIndexing => Volatile.Read(in _indexingFlag) != 0;

	internal string? IndexedVersion => _db.GetMeta("indexed_version");

	internal HelpIndexer(HelpDatabase db)
	{
		_db = db;
	}

	internal bool EnsureIndexed()
	{
		string text = FindPlatformPath();
		if (text == null)
		{
			Console.Error.WriteLine("HELP_STATUS:unavailable");
			return false;
		}
		string version = Path.GetFileName(Path.GetDirectoryName(Path.Combine(text, "bin"))) ?? ExtractVersionFromPath(text);
		string meta;
		long topicCount;
		try
		{
			meta = _db.GetMeta("indexed_version");
			topicCount = _db.GetTopicCount();
		}
		catch
		{
			Console.Error.WriteLine("HELP_STATUS:corrupted:Индекс справки повреждён, пересоздание...");
			RepairAndReindex();
			return true;
		}
		if (meta == version && topicCount > 0)
		{
			Console.Error.WriteLine($"HELP_STATUS:ready:{version}:{topicCount}");
			return true;
		}
		string binPath = GetHelpDirectory(text);
		Task.Run(delegate
		{
			RunIndexing(binPath, version);
		});
		return true;
	}

	internal void ForceReindex()
	{
		if (IsIndexing)
		{
			return;
		}
		string text = FindPlatformPath();
		if (text != null)
		{
			string version = ExtractVersionFromPath(text);
			string binPath = GetHelpDirectory(text);
			try
			{
				_db.ClearAll();
			}
			catch
			{
				_db.Recreate();
			}
			Task.Run(delegate
			{
				RunIndexing(binPath, version);
			});
		}
	}

	internal void RepairAndReindex()
	{
		if (IsIndexing)
		{
			return;
		}
		string text = FindPlatformPath();
		if (text != null)
		{
			string version = ExtractVersionFromPath(text);
			string binPath = GetHelpDirectory(text);
			_db.Recreate();
			Console.Error.WriteLine("HELP_STATUS:repair:Индекс пересоздан, запуск индексации " + version);
			Task.Run(delegate
			{
				RunIndexing(binPath, version);
			});
		}
	}

	private void RunIndexing(string binPath, string version)
	{
		if (Interlocked.CompareExchange(ref _indexingFlag, 1, 0) != 0)
		{
			return;
		}
		try
		{
			Console.Error.WriteLine("HELP_STATUS:indexing:0:0:Начало индексации справки " + version);
			_db.DeleteTopicsByVersion(version);
			(string, string, string?)[] obj = new(string, string, string?)[3]
			{
				("shcntx_ru.hbk", "syntax", null),
				("shlang_ru.hbk", "language", "shlang_root.hbk"),
				("shquery_ru.hbk", "query", "shquery_root.hbk")
			};
			int num = 0;
			(string, string, string?)[] array = obj;
			for (int i = 0; i < array.Length; i++)
			{
				(string, string, string?) tuple = array[i];
				string item = tuple.Item1;
				string item2 = tuple.Item2;
				string? item4 = tuple.Item3;
				string text = Path.Combine(binPath, item);
				if (!File.Exists(text))
				{
					continue;
				}
				Console.Error.WriteLine("HELP_STATUS:indexing:0:0:Парсинг " + item + "...");
				int num2 = 0;
				int num4 = 0;
				HashSet<string> failures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				_db.BeginTransaction();
				try
				{
					foreach (HelpTopic item3 in HbkParser.Parse(text, failures))
					{
						string topicId = $"{version}/{item2}/{item3.TopicName}";
						_db.InsertTopic(topicId, item3.Title, item3.Content, item2, version);
						num++;
						num2++;
						num4++;
						if (num2 >= 100)
						{
							_db.Commit();
							Console.Error.WriteLine($"HELP_STATUS:indexing:{num}:0:Индексация {item2}...");
							_db.BeginTransaction();
							num2 = 0;
						}
					}
					_db.Commit();
				}
				catch
				{
					try
					{
						_db.Rollback();
					}
					catch
					{
					}
				}
				Console.Error.WriteLine($"HELP_STATUS:parsed:{item2}:{num4}");
				int num5 = IndexFallback(binPath, item4, item2, version, failures);
				if (num5 > 0)
				{
					num += num5;
					Console.Error.WriteLine($"HELP_STATUS:fallback:{item2}:{num5}");
				}
			}
			_db.SetMeta("indexed_version", version);
			_db.SetMeta("topic_count", num.ToString());
			_db.SetMeta("indexed_at", DateTime.UtcNow.ToString("O"));
			Console.Error.WriteLine($"HELP_STATUS:ready:{version}:{num}");
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("HELP_STATUS:error:" + ex.Message);
		}
		finally
		{
			Interlocked.Exchange(ref _indexingFlag, 0);
		}
	}

	private int IndexFallback(string binPath, string? rootBook, string category, string version, HashSet<string> failures)
	{
		if (rootBook == null || failures.Count == 0)
		{
			return 0;
		}
		string text = Path.Combine(binPath, rootBook);
		if (!File.Exists(text))
		{
			return 0;
		}
		int num = 0;
		_db.BeginTransaction();
		try
		{
			foreach (HelpTopic item in HbkParser.Parse(text))
			{
				if (!failures.Contains(item.TopicName))
				{
					continue;
				}
				string topicId = $"{version}/{category}/{item.TopicName}";
				string content = "Английская версия: русская страница повреждена в файле справки 1С.\n\n" + item.Content;
				_db.InsertTopic(topicId, item.Title + " (en)", content, category, version);
				num++;
				if (num % 100 == 0)
				{
					_db.Commit();
					_db.BeginTransaction();
				}
			}
			_db.Commit();
		}
		catch
		{
			try
			{
				_db.Rollback();
			}
			catch
			{
			}
		}
		return num;
	}

	internal static string? FindPlatformPath()
	{
		// Порядок поиска: явный путь, бандл рядом с бинарником (tarball),
		// системный бандл (.deb), затем установленная платформа.
		string text = FindHelpBundle(Environment.GetEnvironmentVariable("CONFASTER_1C_HELP_PATH"));
		if (text != null)
		{
			return text;
		}
		text = FindHelpBundle(Path.Combine(AppContext.BaseDirectory, "1c-help"));
		if (text != null)
		{
			return text;
		}
		text = FindHelpBundle("/usr/share/confaster-mcp/1c-help");
		if (text != null)
		{
			return text;
		}
		string[] array = new string[6]
		{
			"C:\\Program Files\\1cv8",
			"C:\\Program Files (x86)\\1cv8",
			"/opt/1cv8/x86_64",
			"/opt/1C/1cv8/x86_64",
			"/opt/1cv8",
			"/opt/1C/1cv8"
		};
		foreach (string path in array)
		{
			text = FindHelpBundle(path);
			if (text != null)
			{
				return text;
			}
		}
		return null;
	}

	// Каталог с .hbk (сам по себе или с подкаталогами версий); берётся старшая версия.
	private static string? FindHelpBundle(string? root)
	{
		if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
		{
			return null;
		}
		if (HasHelpFiles(root))
		{
			return root;
		}
		List<string> list = (from d in Directory.EnumerateDirectories(root)
			where Regex.IsMatch(Path.GetFileName(d), "^\\d+\\.\\d+\\.\\d+\\.\\d+$")
			where HasHelpFiles(d)
			orderby ParseVersion(Path.GetFileName(d)) descending
			select d).ToList();
		return (list.Count > 0) ? list[0] : null;
	}

	private static bool HasHelpFiles(string dir)
	{
		return File.Exists(Path.Combine(dir, "bin", "shcntx_ru.hbk")) || File.Exists(Path.Combine(dir, "shcntx_ru.hbk"));
	}

	// Windows: справка в <версия>\bin\; Linux: файлы справки лежат в корне каталога версии.
	internal static string GetHelpDirectory(string platformPath)
	{
		string text = Path.Combine(platformPath, "bin");
		return File.Exists(Path.Combine(text, "shcntx_ru.hbk")) ? text : platformPath;
	}

	private static Version ParseVersion(string versionStr)
	{
		if (!Version.TryParse(versionStr, out Version result))
		{
			return new Version(0, 0);
		}
		return result;
	}

	internal static string ExtractVersionFromPath(string platformPath)
	{
		string text = Path.GetFileName(platformPath) ?? "unknown";
		if (!Regex.IsMatch(text, "^\\d+\\.\\d+\\.\\d+\\.\\d+$"))
		{
			return "unknown";
		}
		return text;
	}
}
