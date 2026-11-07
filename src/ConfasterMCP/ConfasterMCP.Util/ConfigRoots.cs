using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ConfasterMCP.Util;

internal sealed class ConfigRoots
{
	private sealed class ExtensionNameCache
	{
		private readonly string? _file;

		private readonly Dictionary<string, string> _names;

		private const string IdKeyPrefix = "#id|";

		private readonly Dictionary<string, string> _dirty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		private ExtensionNameCache(string? file, Dictionary<string, string> names)
		{
			_file = file;
			_names = names;
		}

		internal static ExtensionNameCache Load(string? indexRoot)
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			string text = null;
			try
			{
				if (!string.IsNullOrEmpty(indexRoot))
				{
					text = Path.Combine(indexRoot, ".index", "extension_names.json");
					if (File.Exists(text))
					{
						Dictionary<string, string> dictionary2 = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(text));
						if (dictionary2 != null)
						{
							foreach (var (key, value) in dictionary2)
							{
								if (!string.IsNullOrWhiteSpace(value))
								{
									dictionary[key] = value;
								}
							}
						}
					}
				}
			}
			catch
			{
			}
			return new ExtensionNameCache(text, dictionary);
		}

		internal bool TryGetName(string path, [NotNullWhen(true)] out string? name)
		{
			return _names.TryGetValue(path, out name);
		}

		internal bool TryGetId(string path, [NotNullWhen(true)] out string? id)
		{
			return _names.TryGetValue("#id|" + path, out id);
		}

		internal void RememberId(string path, string id)
		{
			Remember("#id|" + path, id);
		}

		internal void Remember(string path, string name)
		{
			if (!_names.TryGetValue(path, out string value) || !(value == name))
			{
				_names[path] = name;
				_dirty[path] = name;
			}
		}

		internal void Save()
		{
			if (_dirty.Count == 0 || _file == null)
			{
				return;
			}
			Mutex mutex = null;
			bool flag = false;
			try
			{
				byte[] inArray = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(_file).ToUpperInvariant()));
				mutex = new Mutex(initiallyOwned: false, "Local\\ConfasterMCP-extnames-" + Convert.ToHexString(inArray, 0, 12));
				try
				{
					flag = mutex.WaitOne(TimeSpan.FromSeconds(2.0));
				}
				catch (AbandonedMutexException)
				{
					flag = true;
				}
				if (!flag)
				{
					return;
				}
				Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				string key;
				string value;
				try
				{
					if (File.Exists(_file))
					{
						Dictionary<string, string> dictionary2 = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_file));
						if (dictionary2 != null)
						{
							foreach (KeyValuePair<string, string> item in dictionary2)
							{
								item.Deconstruct(out key, out value);
								string key2 = key;
								string value2 = value;
								if (!string.IsNullOrWhiteSpace(value2))
								{
									dictionary[key2] = value2;
								}
							}
						}
					}
				}
				catch
				{
				}
				foreach (KeyValuePair<string, string> item2 in _dirty)
				{
					item2.Deconstruct(out value, out key);
					string key3 = value;
					string value3 = key;
					dictionary[key3] = value3;
				}
				Directory.CreateDirectory(Path.GetDirectoryName(_file));
				string text = _file + "." + Environment.ProcessId + ".tmp";
				File.WriteAllText(text, JsonSerializer.Serialize(dictionary));
				File.Move(text, _file, overwrite: true);
				_dirty.Clear();
			}
			catch
			{
			}
			finally
			{
				if (flag)
				{
					mutex.ReleaseMutex();
				}
				mutex?.Dispose();
			}
		}
	}

	private const char KeyPrefix = '@';

	private readonly List<ConfigRoot> _roots;

	private readonly List<string> _warnings = new List<string>();

	private readonly List<string> _scanWarnings = new List<string>();

	private string? _mainArg;

	private List<(string? name, string path)> _extensionArgs = new List<(string, string)>();

	private static readonly HashSet<string> MainAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "main", "основная", "основной", "основная конфигурация", "основной конфигурации", "конфигурация" };

	internal ConfigRoot? Main { get; }

	internal IReadOnlyList<ConfigRoot> All => _roots;

	internal IEnumerable<ConfigRoot> Extensions => _roots.Where((ConfigRoot r) => !r.IsMain);

	internal bool HasExtensions => _roots.Any((ConfigRoot r) => !r.IsMain);

	internal string IndexRoot { get; }

	internal IReadOnlyList<string> Warnings
	{
		get
		{
			lock (_warnings)
			{
				return _warnings.Concat(_scanWarnings).Distinct().ToList();
			}
		}
	}

	internal string Signature => string.Join("|", _roots.Select(SignatureEntry).OrderBy<string, string>((string s) => s, StringComparer.Ordinal));

	private string PathSignature => string.Join("|", (from r in _roots
		where !r.AutoDetected
		select (r.IsMain ? "M:" : "E:") + r.Path.ToUpperInvariant()).OrderBy<string, string>((string s) => s, StringComparer.Ordinal));

	private string UncertainSignature => string.Join("|", (from r in _roots
		where r.IdUncertain
		select r.Path.ToUpperInvariant()).OrderBy<string, string>((string s) => s, StringComparer.Ordinal));

	internal void AddWarning(string warning)
	{
		lock (_warnings)
		{
			if (!_warnings.Contains(warning))
			{
				_warnings.Add(warning);
			}
		}
	}

	internal void AddScanWarning(string warning)
	{
		lock (_warnings)
		{
			if (!_scanWarnings.Contains(warning))
			{
				_scanWarnings.Add(warning);
			}
		}
	}

	internal void ClearScanWarnings()
	{
		lock (_warnings)
		{
			_scanWarnings.Clear();
		}
	}

	internal static string SignatureEntry(ConfigRoot r)
	{
		return r.Id + "=" + r.Path.ToUpperInvariant();
	}

	private ConfigRoots(ConfigRoot? main, List<ConfigRoot> roots, string indexRoot)
	{
		Main = main;
		_roots = roots;
		IndexRoot = indexRoot;
	}

	internal ConfigRoots? Rebuild()
	{
		return Build(_mainArg, _extensionArgs, 1, from r in _roots
			where r.AutoDetected
			select r.Path);
	}

	internal bool SameSetAs(ConfigRoots other)
	{
		if (Signature == other.Signature && UncertainSignature == other.UncertainSignature)
		{
			return string.Equals(IndexRoot, other.IndexRoot, StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	internal bool SamePathsAs(ConfigRoots other)
	{
		if (PathSignature == other.PathSignature)
		{
			return string.Equals(IndexRoot, other.IndexRoot, StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	internal bool LosesCertaintyTo(ConfigRoots other)
	{
		HashSet<string> uncertainHere = new HashSet<string>(from r in _roots
			where r.IdUncertain
			select r.Path, StringComparer.OrdinalIgnoreCase);
		return other._roots.Any((ConfigRoot r) => r.IdUncertain && !uncertainHere.Contains(r.Path));
	}

	internal void AdoptBuildWarnings(ConfigRoots fresh)
	{
		List<string> collection;
		lock (fresh._warnings)
		{
			collection = fresh._warnings.ToList();
		}
		lock (_warnings)
		{
			_warnings.Clear();
			_warnings.AddRange(collection);
		}
	}

	internal static ConfigRoots? Build(string? mainPath, IEnumerable<(string? name, string path)> extensions, int readAttempts = 5, IEnumerable<string>? stickyNested = null)
	{
		List<string> warnings = new List<string>();
		List<ConfigRoot> list = new List<ConfigRoot>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<(string baseId, string path, string? namePrefix, bool autoDetected, bool idUncertain)> pending = new List<(string, string, string, bool, bool)>();
		List<(string, string)> list2 = extensions.ToList();
		ConfigRoot main = null;
		string mainFull = NormalizeDir(mainPath);
		string mainAsExtension = null;
		bool flag = mainFull != null && Directory.Exists(mainFull);
		string indexRoot = (flag ? mainFull : ConfigurationXml.PickExtensionIndexRoot(list2.Select<(string, string), string>(((string name, string path) e) => NormalizeDir(e.path)).OfType<string>()));
		ExtensionNameCache nameCache = ExtensionNameCache.Load(indexRoot);
		if (mainFull != null && Directory.Exists(mainFull))
		{
			ConfigurationXml.Info info = ConfigurationXml.Read(mainFull, readAttempts);
			if (info.ReadFailed && nameCache.TryGetName(mainFull, out string name))
			{
				info = info with
				{
					IsExtension = true,
					Name = name
				};
			}
			if (info.ReadFailed && !info.IsExtension)
			{
				warnings.Add("Не удалось прочитать Configuration.xml основной выгрузки «" + mainFull + "» — папка индексируется как основная конфигурация.");
			}
			if (info.IsExtension)
			{
				mainAsExtension = mainFull;
				if (!list2.Any<(string, string)>(((string name, string path) e) => string.Equals(NormalizeDir(e.path), mainFull, StringComparison.OrdinalIgnoreCase)))
				{
					warnings.Add($"Папка основной выгрузки «{mainFull}» содержит выгрузку РАСШИРЕНИЯ «{info.Name}», " + "а не основной конфигурации. Укажите в настройках базы путь выгрузки основной конфигурации, а это расширение подключите в списке расширений.");
				}
			}
			else
			{
				main = new ConfigRoot
				{
					Id = "",
					Path = mainFull
				};
				list.Add(main);
				usedPaths.Add(mainFull);
			}
		}
		else if (!string.IsNullOrWhiteSpace(mainPath))
		{
			warnings.Add("Папка основной выгрузки не найдена: " + mainPath);
		}
		if (mainAsExtension != null)
		{
			string item = list2.FirstOrDefault<(string, string)>(((string name, string path) e) => string.Equals(NormalizeDir(e.path), mainAsExtension, StringComparison.OrdinalIgnoreCase)).Item1;
			AddExtension(item, mainAsExtension, autoDetected: false);
		}
		foreach (var item6 in list2)
		{
			string item2 = item6.Item1;
			string item3 = item6.Item2;
			string text = NormalizeDir(item3);
			if (text == null)
			{
				warnings.Add("Папка выгрузки расширения «" + item2 + "» не найдена: " + item3);
			}
			else
			{
				AddExtension(item2, text, autoDetected: false, !Directory.Exists(text));
			}
		}
		if (main != null)
		{
			foreach (string item7 in ConfigurationXml.FindNestedExtensionExports(main.Path, readAttempts))
			{
				AddExtension(null, item7, autoDetected: true);
			}
			string value = main.Path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
			foreach (string item8 in stickyNested ?? Enumerable.Empty<string>())
			{
				if (!usedPaths.Contains(item8) && item8.StartsWith(value, StringComparison.OrdinalIgnoreCase) && Directory.Exists(item8))
				{
					ConfigurationXml.Info info2 = ConfigurationXml.Read(item8, readAttempts);
					if (!info2.Exists || info2.ReadFailed || info2.IsExtension)
					{
						AddExtension(null, item8, autoDetected: true);
					}
				}
			}
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		List<(string, string, string, bool, bool)> list3 = pending.OrderBy<(string, string, string, bool, bool), string>(((string baseId, string path, string namePrefix, bool autoDetected, bool idUncertain) p) => p.baseId, StringComparer.OrdinalIgnoreCase).ThenBy<(string, string, string, bool, bool), string>(((string baseId, string path, string namePrefix, bool autoDetected, bool idUncertain) p) => p.path, StringComparer.OrdinalIgnoreCase).ToList();
		foreach (var item9 in list3)
		{
			if (nameCache.TryGetId(item9.Item2, out string id) && IsIdOfBase(id, item9.Item1) && hashSet.Add(id))
			{
				dictionary[item9.Item2] = id;
			}
		}
		foreach (var item10 in list3)
		{
			if (!dictionary.ContainsKey(item10.Item2))
			{
				string text2 = item10.Item1;
				int num = 2;
				while (!hashSet.Add(text2))
				{
					text2 = $"{item10.Item1}_{num}";
					num++;
				}
				dictionary[item10.Item2] = text2;
			}
		}
		foreach (var (path, id2) in dictionary)
		{
			nameCache.RememberId(path, id2);
		}
		foreach (var item11 in pending)
		{
			string text5 = dictionary[item11.path];
			list.Add(new ConfigRoot
			{
				Id = text5,
				Path = item11.path,
				NamePrefix = item11.namePrefix,
				IdUncertain = item11.idUncertain,
				AutoDetected = item11.autoDetected
			});
			if (item11.autoDetected)
			{
				Console.Error.WriteLine("MCP_LOG: nested extension export detected: " + text5 + " → " + item11.path);
			}
		}
		nameCache.Save();
		string text6 = (flag ? mainFull : ConfigurationXml.PickExtensionIndexRoot(list.Select((ConfigRoot r) => r.Path)));
		if (text6 == null)
		{
			return null;
		}
		ConfigRoots configRoots = new ConfigRoots(main, list, text6)
		{
			_mainArg = mainPath,
			_extensionArgs = list2
		};
		foreach (string item12 in warnings)
		{
			configRoots.AddWarning(item12);
		}
		return configRoots;
		void AddExtension(string? configuredName, string fullPath, bool autoDetected, bool unavailable = false)
		{
			if (!usedPaths.Add(fullPath))
			{
				if (main != null && string.Equals(fullPath, main.Path, StringComparison.OrdinalIgnoreCase))
				{
					warnings.Add("Путь расширения «" + configuredName + "» совпадает с папкой основной выгрузки — расширение пропущено. Выгрузите расширение в отдельную папку.");
				}
			}
			else
			{
				ConfigurationXml.Info info3 = (unavailable ? default(ConfigurationXml.Info) : ConfigurationXml.Read(fullPath, readAttempts));
				string name2 = null;
				if (unavailable)
				{
					nameCache.TryGetName(fullPath, out name2);
				}
				else if (info3.ReadFailed)
				{
					nameCache.TryGetName(fullPath, out name2);
					warnings.Add($"Не удалось прочитать Configuration.xml расширения «{name2 ?? configuredName ?? fullPath}» ({fullPath}) — " + ((name2 != null) ? "использовано имя из прошлой индексации." : "имя взято из настроек."));
				}
				else if (!info3.Exists)
				{
					nameCache.TryGetName(fullPath, out name2);
					warnings.Add("В папке расширения «" + (name2 ?? configuredName ?? fullPath) + "» нет файла Configuration.xml — похоже, это не корень выгрузки расширения (или выгрузка ещё идёт). Файлы всё равно индексируются.");
				}
				else if (!info3.IsExtension)
				{
					warnings.Add($"Папка «{fullPath}», указанная как расширение «{configuredName}», содержит выгрузку " + "основной конфигурации, а не расширения. Файлы индексируются, но проверьте путь.");
				}
				else if (info3.Name != null)
				{
					nameCache.Remember(fullPath, info3.Name);
				}
				string item4 = SanitizeId(name2) ?? SanitizeId(info3.Name) ?? SanitizeId(configuredName) ?? SanitizeId(Path.GetFileName(fullPath.TrimEnd('\\', '/'))) ?? "Расширение";
				bool item5 = name2 == null && (info3.ReadFailed || (unavailable && !IsDefinitelyMissing(fullPath)));
				pending.Add((item4, fullPath, info3.NamePrefix, autoDetected, item5));
			}
		}
	}

	internal static (string? name, string path) ParseExtensionArg(string value)
	{
		int num = value.LastIndexOf('|');
		return (name: (num > 0) ? value.Substring(0, num) : null, path: (num >= 0) ? value.Substring(num + 1) : value);
	}

	internal string ToKey(ConfigRoot root, string relativePath)
	{
		string text = relativePath.Replace('\\', '/');
		if (!root.IsMain)
		{
			return "@" + root.Id + "/" + text;
		}
		return text;
	}

	internal (ConfigRoot? root, string relative) SplitKey(string key)
	{
		if (key.Length > 1 && key[0] == '@')
		{
			int num = key.IndexOf('/');
			if (num > 1)
			{
				string id = key.Substring(1, num - 1);
				return (root: _roots.FirstOrDefault((ConfigRoot r) => !r.IsMain && string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)), relative: key.Substring(num + 1));
			}
		}
		return (root: Main, relative: key);
	}

	internal static bool IsExtensionKey(string key)
	{
		if (key.Length > 1)
		{
			return key[0] == '@';
		}
		return false;
	}

	internal static string RelativeOf(string key)
	{
		if (!IsExtensionKey(key))
		{
			return key;
		}
		int num = key.IndexOf('/');
		if (num <= 0)
		{
			return key;
		}
		return key.Substring(num + 1);
	}

	internal static string DisplayPath(string key)
	{
		return PathHelper.ToDisplayPath(RelativeOf(key));
	}

	internal string? LabelOf(string key)
	{
		if (!HasExtensions)
		{
			return null;
		}
		return SplitKey(key).root?.Label;
	}

	internal string? LabelOf(ConfigRoot root)
	{
		if (!HasExtensions)
		{
			return null;
		}
		return root.Label;
	}

	internal string? ResolveKey(string key)
	{
		var (configRoot, relative) = SplitKey(key);
		if (configRoot == null)
		{
			return null;
		}
		string text = Combine(configRoot, relative);
		if (!File.Exists(text))
		{
			return null;
		}
		return text;
	}

	internal string? ResolveUserPath(string path, ConfigRoot? preferred = null)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		if (Path.IsPathRooted(path))
		{
			if (!File.Exists(path))
			{
				return null;
			}
			string fullPath = Path.GetFullPath(path);
			ConfigRoot configRoot = ((preferred != null) ? RootOfFullPath(fullPath) : null);
			if (configRoot == null || configRoot == preferred)
			{
				return fullPath;
			}
			string text = Combine(preferred, PathHelper.GetRelativePath(fullPath, configRoot.Path));
			if (!File.Exists(text))
			{
				return null;
			}
			return text;
		}
		if (IsExtensionKey(path))
		{
			return ResolveKey(path);
		}
		string relative = path.Replace('\\', '/').TrimStart('/');
		IEnumerable<ConfigRoot> enumerable;
		if (preferred == null)
		{
			IEnumerable<ConfigRoot> roots = _roots;
			enumerable = roots;
		}
		else
		{
			IEnumerable<ConfigRoot> roots = new ConfigRoot[1] { preferred };
			enumerable = roots;
		}
		foreach (ConfigRoot item in enumerable)
		{
			string text2 = Combine(item, relative);
			if (File.Exists(text2))
			{
				return text2;
			}
		}
		return null;
	}

	internal string? KeyOfFullPath(string fullPath)
	{
		ConfigRoot configRoot = RootOfFullPath(fullPath);
		if (configRoot == null)
		{
			return null;
		}
		return ToKey(configRoot, PathHelper.GetRelativePath(fullPath, configRoot.Path));
	}

	internal ConfigRoot? RootOfFullPath(string fullPath)
	{
		string fullPath2;
		try
		{
			fullPath2 = Path.GetFullPath(fullPath);
		}
		catch
		{
			return null;
		}
		ConfigRoot configRoot = null;
		foreach (ConfigRoot root in _roots)
		{
			string value = root.Path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
			if (fullPath2.StartsWith(value, StringComparison.OrdinalIgnoreCase) && (configRoot == null || root.Path.Length > configRoot.Path.Length))
			{
				configRoot = root;
			}
		}
		return configRoot;
	}

	internal ConfigRoot? FindByName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		string n = name.Trim();
		if (n.StartsWith("расширение ", StringComparison.OrdinalIgnoreCase))
		{
			n = n.Substring("расширение ".Length).Trim();
		}
		ConfigRoot configRoot = _roots.FirstOrDefault((ConfigRoot r) => !r.IsMain && string.Equals(r.Id, n, StringComparison.OrdinalIgnoreCase));
		if (configRoot != null)
		{
			return configRoot;
		}
		if (!MainAliases.Contains(n))
		{
			return null;
		}
		return Main;
	}

	internal bool IsUnknownName(string? name, out ConfigRoot? root)
	{
		root = FindByName(name);
		if (!string.IsNullOrWhiteSpace(name))
		{
			return root == null;
		}
		return false;
	}

	internal bool IsKnownConfig(string id)
	{
		if (id.Length != 0)
		{
			return _roots.Any((ConfigRoot r) => !r.IsMain && string.Equals(r.Id, id, StringComparison.Ordinal));
		}
		return Main != null;
	}

	internal (string inList, List<(string, object)> parameters) KnownConfigsSql()
	{
		List<string> list = new List<string>();
		if (Main != null)
		{
			list.Add("");
		}
		list.AddRange(Extensions.Select((ConfigRoot r) => r.Id));
		if (list.Count == 0)
		{
			list.Add("\0");
		}
		List<(string, object)> list2 = list.Select((string id, int i) => ((string, object))("@k" + i, id)).ToList();
		return (inList: "(" + string.Join(", ", list2.Select(((string, object) p) => p.Item1)) + ")", parameters: list2);
	}

	internal static string AnyRootLike(string column, string param)
	{
		return $"({column} LIKE {param} OR {column} LIKE '@%/' || {param})";
	}

	internal string RootLike(string column, string param, ConfigRoot root)
	{
		if (!root.IsMain)
		{
			string text = "@" + root.Id + "/";
			return $"(substr({column}, 1, {text.Length}) = '{text.Replace("'", "''")}' AND substr({column}, {text.Length + 1}) LIKE {param})";
		}
		return $"({column} LIKE {param} AND {column} NOT LIKE '@%')";
	}

	internal string KnownKeysSql(string column)
	{
		if (_roots.Count != 0)
		{
			return "(" + string.Join(" OR ", _roots.Select((ConfigRoot r) => InRootSql(column, r))) + ")";
		}
		return "0";
	}

	internal bool IsKnownKey(string key)
	{
		if (!IsExtensionKey(key))
		{
			return Main != null;
		}
		return _roots.Any((ConfigRoot r) => !r.IsMain && key.StartsWith("@" + r.Id + "/", StringComparison.Ordinal));
	}

	internal static string InRootSql(string column, ConfigRoot root)
	{
		if (root.IsMain)
		{
			return "(" + column + " NOT LIKE '@%')";
		}
		string text = "@" + root.Id + "/";
		return $"(substr({column}, 1, {text.Length}) = '{text.Replace("'", "''")}')";
	}

	internal HashSet<string> NestedRootsOf(ConfigRoot root)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string value = root.Path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
		foreach (ConfigRoot root2 in _roots)
		{
			if (root2 != root && root2.Path.StartsWith(value, StringComparison.OrdinalIgnoreCase))
			{
				hashSet.Add(root2.Path.TrimEnd('\\', '/'));
			}
		}
		return hashSet;
	}

	private static bool IsDefinitelyMissing(string fullPath)
	{
		try
		{
			string directoryName = Path.GetDirectoryName(fullPath);
			string fileName = Path.GetFileName(fullPath);
			if (string.IsNullOrEmpty(directoryName) || string.IsNullOrEmpty(fileName) || !Directory.Exists(directoryName))
			{
				return false;
			}
			return !Directory.EnumerateFileSystemEntries(directoryName, fileName).Any();
		}
		catch
		{
			return false;
		}
	}

	private static string Combine(ConfigRoot root, string relative)
	{
		return Path.Combine(root.Path, relative.Replace('/', Path.DirectorySeparatorChar));
	}

	private static string? NormalizeDir(string? path)
	{
		return ConfigurationXml.NormalizeDir(path);
	}

	private static bool IsIdOfBase(string id, string baseId)
	{
		if (string.Equals(id, baseId, StringComparison.Ordinal))
		{
			return true;
		}
		if (id.Length > baseId.Length + 1 && id.StartsWith(baseId + "_", StringComparison.Ordinal))
		{
			return id.Substring(baseId.Length + 1).All(char.IsAsciiDigit);
		}
		return false;
	}

	private static string? SanitizeId(string? name)
	{
		return ConfigurationXml.SanitizeExtensionName(name);
	}
}
