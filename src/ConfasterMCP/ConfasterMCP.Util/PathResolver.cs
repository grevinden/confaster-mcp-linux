using System.Collections.Generic;
using System.IO;

namespace ConfasterMCP.Util;

internal sealed class PathResolver
{
	private readonly List<string> _roots;

	internal string PrimaryRoot => _roots[0];

	internal IReadOnlyList<string> AllRoots => _roots;

	internal PathResolver(string mainRoot, List<string>? extensionRoots = null)
	{
		_roots = new List<string> { mainRoot };
		if (extensionRoots != null)
		{
			_roots.AddRange(extensionRoots);
		}
	}

	internal string? Resolve(string relativePath)
	{
		string path = relativePath.Replace('/', Path.DirectorySeparatorChar);
		foreach (string root in _roots)
		{
			string text = Path.Combine(root, path);
			if (File.Exists(text))
			{
				return text;
			}
		}
		return null;
	}
}
