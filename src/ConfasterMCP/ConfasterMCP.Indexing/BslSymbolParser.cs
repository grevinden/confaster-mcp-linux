using System.Collections.Generic;
using System.Text.RegularExpressions;
using ConfasterMCP.TreeSitter;

namespace ConfasterMCP.Indexing;

internal static class BslSymbolParser
{
	private static readonly Regex _regionStart = new Regex("^[ \\t]*#(?:Область|Region)\\s+(.+?)\\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

	private static readonly Regex _regionEnd = new Regex("^[ \\t]*#(?:КонецОбласти|EndRegion)\\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

	internal static List<SymbolInfo> Parse(byte[] utf8Bytes, string[] lines)
	{
		return BslAstParser.Parse(utf8Bytes, lines);
	}

	internal static List<RegionInfo> ParseRegions(string[] lines)
	{
		List<RegionInfo> list = new List<RegionInfo>();
		Stack<(string, int)> stack = new Stack<(string, int)>();
		for (int i = 0; i < lines.Length; i++)
		{
			Match match = _regionStart.Match(lines[i]);
			if (match.Success)
			{
				stack.Push((match.Groups[1].Value.Trim(), i + 1));
			}
			else if (_regionEnd.IsMatch(lines[i]) && stack.Count > 0)
			{
				var (name, startLine) = stack.Pop();
				list.Add(new RegionInfo
				{
					Name = name,
					StartLine = startLine,
					EndLine = i + 1
				});
			}
		}
		while (stack.Count > 0)
		{
			var (name2, startLine2) = stack.Pop();
			list.Add(new RegionInfo
			{
				Name = name2,
				StartLine = startLine2,
				EndLine = lines.Length
			});
		}
		return list;
	}
}
