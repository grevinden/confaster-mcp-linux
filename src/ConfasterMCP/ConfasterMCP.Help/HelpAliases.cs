using System;
using System.Collections.Generic;
using System.IO;

namespace ConfasterMCP.Help;

// Русские поисковые синонимы для страниц, восстановленных из английского дубля.
// Файл search-aliases.tsv лежит рядом с книгами справки; строки вида
// <category>/<имя записи>\t<синоним>;<синоним>.
internal interface IHelpAliasResolver
{
	IEnumerable<string> Resolve(string query, string? category);
}

internal sealed class HelpAliases : IHelpAliasResolver
{
	private readonly Dictionary<string, List<string>> _topics = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

	internal static HelpAliases Empty { get; } = new HelpAliases();

	private HelpAliases()
	{
	}

	internal static HelpAliases Load(string helpDirectory, string version)
	{
		HelpAliases helpAliases = new HelpAliases();
		string path = Path.Combine(helpDirectory, "search-aliases.tsv");
		if (!File.Exists(path))
		{
			return helpAliases;
		}
		string[] lines;
		try
		{
			lines = File.ReadAllLines(path);
		}
		catch (IOException)
		{
			return helpAliases;
		}
		catch (UnauthorizedAccessException)
		{
			return helpAliases;
		}
		foreach (string line in lines)
		{
			string text = line.Trim();
			if (text.Length == 0 || text.StartsWith("#", StringComparison.Ordinal))
			{
				continue;
			}
			string[] parts = text.Split('\t');
			if (parts.Length < 2)
			{
				continue;
			}
			string name = parts[0].Trim();
			if (name.Length == 0 || name.IndexOf('/') <= 0 || name.IndexOf('/', name.IndexOf('/') + 1) >= 0)
			{
				continue;
			}
			string topicId = version + "/" + name;
			foreach (string item in parts[1].Split(';'))
			{
				string key = Normalize(item);
				if (key.Length == 0)
				{
					continue;
				}
				if (!helpAliases._topics.TryGetValue(key, out List<string> value))
				{
					value = new List<string>();
					helpAliases._topics[key] = value;
				}
				if (!value.Contains(topicId))
				{
					value.Add(topicId);
				}
			}
		}
		return helpAliases;
	}

	public IEnumerable<string> Resolve(string query, string? category)
	{
		if (!_topics.TryGetValue(Normalize(query), out List<string> value))
		{
			yield break;
		}
		foreach (string topicId in value)
		{
			if (string.IsNullOrEmpty(category) || category == "all" || IsCategory(topicId, category))
			{
				yield return topicId;
			}
		}
	}

	private static bool IsCategory(string topicId, string category)
	{
		string[] parts = topicId.Split('/');
		return parts.Length >= 3 && parts[1] == category;
	}

	private static string Normalize(string value)
	{
		string text = value.Trim().ToLowerInvariant().Replace('ё', 'е');
		return string.Join(" ", text.Split(new char[4] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));
	}
}
