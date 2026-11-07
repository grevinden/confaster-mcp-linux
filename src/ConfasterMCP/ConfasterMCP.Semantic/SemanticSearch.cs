using System;
using System.Collections.Generic;
using System.Globalization;
using ConfasterMCP.Indexing;

namespace ConfasterMCP.Semantic;

internal sealed class SemanticSearch
{
	internal sealed class Result
	{
		public long SymbolId { get; init; }

		public string Name { get; init; } = "";

		public string Kind { get; init; } = "";

		public string FilePath { get; init; } = "";

		public int StartLine { get; init; }

		public int EndLine { get; init; }

		public bool IsExport { get; init; }

		public double Bm25Score { get; init; }

		public long CallCount { get; init; }

		public long CallerDiversity { get; init; }

		public double FinalScore { get; set; }
	}

	private readonly Database _db;

	internal SemanticSearch(Database db)
	{
		_db = db;
	}

	internal List<Result> Search(string query, IReadOnlyList<string> contextObjects, int limit)
	{
		List<string> list = Tokenizer.TokenizeQuery(query);
		if (list.Count == 0)
		{
			return new List<Result>();
		}
		List<(string, double)> list2 = ExpandWithAliases(list);
		if (list2.Count == 0)
		{
			return new List<Result>();
		}
		List<string> list3 = new List<string>(list2.Count);
		foreach (var item2 in list2)
		{
			string text = Tokenizer.SanitizeForFts(item2.Item1);
			if (text.Length >= 2)
			{
				list3.Add(text + "*");
			}
		}
		if (list3.Count == 0)
		{
			return new List<Result>();
		}
		string item = string.Join(" OR ", list3);
		List<string> list4 = new List<string>();
		foreach (string contextObject in contextObjects)
		{
			if (string.IsNullOrWhiteSpace(contextObject))
			{
				continue;
			}
			int num = contextObject.IndexOf('.');
			string text2 = ((num >= 0) ? contextObject.Substring(num + 1) : contextObject);
			foreach (string item3 in Tokenizer.TokenizeLower(text2))
			{
				list4.Add(item3);
			}
			list4.Add(text2.ToLower(CultureInfo.InvariantCulture));
		}
		string sql = $"\n                SELECT\n                    CAST(st.symbol_id AS INTEGER) AS sid,\n                    s.name, s.kind, s.file_path, s.start_line, s.end_line, s.is_export,\n                    bm25(symbol_terms) AS bm25,\n                    COALESCE(sw.call_count, 0) AS call_count,\n                    COALESCE(sw.caller_diversity, 0) AS caller_diversity\n                FROM symbol_terms st\n                JOIN symbols s ON s.id = CAST(st.symbol_id AS INTEGER)\n                LEFT JOIN symbol_weights sw ON sw.symbol_id = s.id\n                WHERE symbol_terms MATCH @q\n                ORDER BY bm25(symbol_terms)\n                LIMIT {Math.Max(limit * 5, limit)}";
		List<Result> list5 = new List<Result>();
		try
		{
			using Database.OwnedReader ownedReader = _db.Query(sql, ("@q", item));
			while (ownedReader.Read())
			{
				list5.Add(new Result
				{
					SymbolId = ownedReader.GetInt64(0),
					Name = ownedReader.GetString(1),
					Kind = ownedReader.GetString(2),
					FilePath = ownedReader.GetString(3),
					StartLine = ownedReader.GetInt32(4),
					EndLine = ownedReader.GetInt32(5),
					IsExport = (ownedReader.GetInt32(6) != 0),
					Bm25Score = SafeGetDouble(ownedReader, 7),
					CallCount = ownedReader.GetInt64(8),
					CallerDiversity = ownedReader.GetInt64(9)
				});
			}
		}
		catch
		{
			return new List<Result>();
		}
		foreach (Result item4 in list5)
		{
			double num2 = 1.0 / (1.0 + Math.Max(0.0 - item4.Bm25Score, 0.0));
			double num3 = 1.0 + Math.Log(1.0 + (double)item4.CallCount) * 0.15;
			double num4 = 1.0 + Math.Log(1.0 + (double)item4.CallerDiversity) * 0.15;
			double num5;
			if (list4.Count == 0)
			{
				num5 = 1.0;
			}
			else
			{
				string text3 = item4.FilePath.ToLower(CultureInfo.InvariantCulture);
				string text4 = item4.Name.ToLower(CultureInfo.InvariantCulture);
				int num6 = 0;
				foreach (string item5 in list4)
				{
					if (item5.Length >= 2 && (text3.Contains(item5) || text4.Contains(item5)))
					{
						num6++;
					}
				}
				num5 = 1.0 + (double)num6 * 0.3;
			}
			item4.FinalScore = num2 * num3 * num4 * num5;
		}
		list5.Sort((Result a, Result b) => b.FinalScore.CompareTo(a.FinalScore));
		if (list5.Count > limit)
		{
			list5.RemoveRange(limit, list5.Count - limit);
		}
		return list5;
	}

	private static double SafeGetDouble(Database.OwnedReader reader, int ordinal)
	{
		if (reader.IsDBNull(ordinal))
		{
			return 0.0;
		}
		return reader.GetDouble(ordinal);
	}

	private List<(string term, double weight)> ExpandWithAliases(List<string> tokens)
	{
		Dictionary<string, double> dictionary = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
		foreach (string token in tokens)
		{
			dictionary[token.ToLower(CultureInfo.InvariantCulture)] = 1.0;
		}
		foreach (string token2 in tokens)
		{
			string item = token2.ToLower(CultureInfo.InvariantCulture);
			using Database.OwnedReader ownedReader = _db.Query("SELECT alias, weight FROM domain_aliases WHERE term = @t", ("@t", item));
			while (ownedReader.Read())
			{
				string key = ownedReader.GetString(0).ToLower(CultureInfo.InvariantCulture);
				double num = SafeGetDouble(ownedReader, 1);
				if (!dictionary.TryGetValue(key, out var value) || num > value)
				{
					dictionary[key] = num;
				}
			}
		}
		List<(string, double)> list = new List<(string, double)>(dictionary.Count);
		foreach (KeyValuePair<string, double> item2 in dictionary)
		{
			list.Add((item2.Key, item2.Value));
		}
		return list;
	}
}
