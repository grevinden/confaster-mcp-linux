using System;
using System.Collections.Generic;
using System.Linq;
using ConfasterMCP.Indexing;
using ConfasterMCP.Util;

namespace ConfasterMCP.Tools;

internal static class ExtensionLinks
{
	internal sealed class Interceptor
	{
		public string configuration { get; init; } = "";

		public string annotation { get; init; } = "";

		public string method { get; init; } = "";

		public string file { get; init; } = "";

		public string? absolute_path { get; init; }

		public int start_line { get; init; }

		public int end_line { get; init; }
	}

	internal static List<Interceptor>? FindInterceptors(Database db, ConfigRoots roots, string key, string name)
	{
		if (!roots.HasExtensions || ConfigRoots.IsExtensionKey(key))
		{
			return null;
		}
		List<Interceptor> list = FindByTarget(db, roots, name, (string rel) => string.Equals(rel, key, StringComparison.OrdinalIgnoreCase), 20);
		if (list.Count <= 0)
		{
			return null;
		}
		return list;
	}

	internal static List<Interceptor> FindByTarget(Database db, ConfigRoots roots, string name, Func<string, bool> acceptModule, int max)
	{
		List<Interceptor> list = new List<Interceptor>();
		if (!roots.HasExtensions)
		{
			return list;
		}
		using Database.OwnedReader ownedReader = db.Query("\n                SELECT file_path, name, annotation, start_line, end_line\n                FROM symbols\n                WHERE annotation_target_lower = @n AND file_path LIKE '@%'", ("@n", name.ToLowerInvariant()));
		while (ownedReader.Read() && list.Count < max)
		{
			string text = ownedReader.GetString(0);
			if (acceptModule(ConfigRoots.RelativeOf(text)) && IsKnownExtension(roots, text))
			{
				list.Add(ReadInterceptor(roots, ownedReader, text));
			}
		}
		return list;
	}

	private static Interceptor ReadInterceptor(ConfigRoots roots, Database.OwnedReader reader, string extKey)
	{
		return new Interceptor
		{
			configuration = (roots.LabelOf(extKey) ?? ""),
			annotation = (reader.IsDBNull(2) ? "" : reader.GetString(2)),
			method = reader.GetString(1),
			file = ConfigRoots.DisplayPath(extKey),
			absolute_path = roots.ResolveKey(extKey),
			start_line = reader.GetInt32(3),
			end_line = reader.GetInt32(4)
		};
	}

	internal static void FillInterceptors(Database db, ConfigRoots roots, List<SymbolResult> symbols)
	{
		if (!roots.HasExtensions)
		{
			return;
		}
		List<SymbolResult> list = symbols.Where((SymbolResult s) => !ConfigRoots.IsExtensionKey(s.Key)).ToList();
		if (list.Count == 0)
		{
			return;
		}
		List<string> source = list.Select((SymbolResult s) => s.name.ToLowerInvariant()).Distinct().ToList();
		string text = string.Join(", ", source.Select((string _, int i) => "@n" + i));
		(string, object)[] parameters = source.Select((string n, int i) => ((string, object))("@n" + i, n)).ToArray();
		Dictionary<(string, string), List<Interceptor>> dictionary = new Dictionary<(string, string), List<Interceptor>>();
		using (Database.OwnedReader ownedReader = db.Query("\n                SELECT file_path, name, annotation, start_line, end_line, annotation_target_lower\n                FROM symbols\n                WHERE annotation_target_lower IN (" + text + ") AND file_path LIKE '@%'", parameters))
		{
			while (ownedReader.Read())
			{
				string text2 = ownedReader.GetString(0);
				if (IsKnownExtension(roots, text2))
				{
					(string, string) key = (ConfigRoots.RelativeOf(text2).ToUpperInvariant(), ownedReader.GetString(5));
					if (!dictionary.TryGetValue(key, out var value))
					{
						value = (dictionary[key] = new List<Interceptor>());
					}
					if (value.Count < 20)
					{
						value.Add(ReadInterceptor(roots, ownedReader, text2));
					}
				}
			}
		}
		foreach (SymbolResult item in list)
		{
			if (dictionary.TryGetValue((item.Key.ToUpperInvariant(), item.name.ToLowerInvariant()), out var value2))
			{
				item.intercepted_by = value2;
			}
		}
	}

	private static bool IsKnownExtension(ConfigRoots roots, string extKey)
	{
		return roots.IsKnownKey(extKey);
	}

	internal static object? DescribeTarget(string key, string? annotation, string? target)
	{
		if (string.IsNullOrEmpty(annotation) || !ConfigRoots.IsExtensionKey(key))
		{
			return null;
		}
		return new
		{
			annotation = annotation,
			target_method = target,
			target_module = ConfigRoots.DisplayPath(key),
			note = "Метод расширения: перехватывает метод основной конфигурации с указанным именем в одноимённом модуле."
		};
	}
}
