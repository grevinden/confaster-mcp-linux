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
		string binPath = Path.Combine(text, "bin");
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
			string binPath = Path.Combine(text, "bin");
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
			string binPath = Path.Combine(text, "bin");
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
			(string, string)[] obj = new(string, string)[3]
			{
				("shcntx_ru.hbk", "syntax"),
				("shlang_ru.hbk", "language"),
				("shquery_ru.hbk", "query")
			};
			int num = 0;
			(string, string)[] array = obj;
			for (int i = 0; i < array.Length; i++)
			{
				(string, string) tuple = array[i];
				string item = tuple.Item1;
				string item2 = tuple.Item2;
				string text = Path.Combine(binPath, item);
				if (!File.Exists(text))
				{
					continue;
				}
				Console.Error.WriteLine("HELP_STATUS:indexing:0:0:Парсинг " + item + "...");
				int num2 = 0;
				_db.BeginTransaction();
				try
				{
					foreach (HelpTopic item3 in HbkParser.Parse(text))
					{
						string topicId = $"{version}/{item2}/{item3.TopicName}";
						_db.InsertTopic(topicId, item3.Title, item3.Content, item2, version);
						num++;
						num2++;
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

	internal static string? FindPlatformPath()
	{
		string[] array = new string[2] { "C:\\Program Files\\1cv8", "C:\\Program Files (x86)\\1cv8" };
		foreach (string path in array)
		{
			if (Directory.Exists(path))
			{
				List<string> list = (from d in Directory.EnumerateDirectories(path)
					where Regex.IsMatch(Path.GetFileName(d), "^\\d+\\.\\d+\\.\\d+\\.\\d+$")
					where File.Exists(Path.Combine(d, "bin", "shcntx_ru.hbk"))
					orderby ParseVersion(Path.GetFileName(d)) descending
					select d).ToList();
				if (list.Count > 0)
				{
					return list[0];
				}
			}
		}
		return null;
	}

	private static Version ParseVersion(string versionStr)
	{
		if (!Version.TryParse(versionStr, out Version result))
		{
			return new Version(0, 0);
		}
		return result;
	}

	private static string ExtractVersionFromPath(string platformPath)
	{
		string text = Path.GetFileName(platformPath) ?? "unknown";
		if (!Regex.IsMatch(text, "^\\d+\\.\\d+\\.\\d+\\.\\d+$"))
		{
			return "unknown";
		}
		return text;
	}
}
