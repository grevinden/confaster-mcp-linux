using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Xml;

namespace ConfasterMCP.Util;

internal static class ConfigurationXml
{
	internal readonly record struct Info(bool Exists, bool IsExtension, string? Name, string? NamePrefix, bool ReadFailed = false, IReadOnlyList<string>? Synonyms = null);

	internal const int DefaultReadAttempts = 5;

	private static readonly HashSet<string> TypeFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"Documents", "Catalogs", "CommonModules", "CommonForms", "CommonCommands", "InformationRegisters", "AccumulationRegisters", "AccountingRegisters", "CalculationRegisters", "DataProcessors",
		"Reports", "Enums", "ChartsOfCharacteristicTypes", "ChartsOfAccounts", "ChartsOfCalculationTypes", "BusinessProcesses", "Tasks", "ExchangePlans", "Constants", "Sequences",
		"DocumentJournals", "FilterCriteria", "SettingsStorages", "FunctionalOptions", "FunctionalOptionsParameters", "DefinedTypes", "CommonAttributes", "SessionParameters", "Roles", "CommonPictures",
		"CommonTemplates", "XDTOPackages", "WebServices", "HTTPServices", "WSReferences", "Styles", "StyleItems", "Languages", "Subsystems", "EventSubscriptions",
		"ScheduledJobs", "CommandGroups", "ExternalDataSources", "Interfaces", "IntegrationServices", "Bots", "WebSocketClients", "PaletteColors"
	};

	internal static Info Read(string root, int attempts = 5)
	{
		string text;
		try
		{
			text = Path.Combine(root, "Configuration.xml");
		}
		catch
		{
			return default(Info);
		}
		if (!File.Exists(text))
		{
			return default(Info);
		}
		int num = 1;
		while (true)
		{
			try
			{
				return ReadCore(text);
			}
			catch (Exception ex) when (((ex is IOException || ex is XmlException) ? true : false) && num < attempts)
			{
				Thread.Sleep(300);
			}
			catch
			{
				return new Info(Exists: true, IsExtension: false, null, null, ReadFailed: true);
			}
			num++;
		}
	}

	internal static List<string> FindNestedExtensionExports(string mainRoot, int attempts = 5)
	{
		List<string> found = new List<string>();
		HashSet<string> knownExtensions = null;
		Probe(mainRoot, 1);
		return found;
		void Probe(string dir, int depth)
		{
			List<string> list;
			try
			{
				list = Directory.EnumerateDirectories(dir).ToList();
			}
			catch
			{
				return;
			}
			foreach (string item2 in list)
			{
				string fileName = Path.GetFileName(item2);
				if (!fileName.StartsWith('.') && !TypeFolders.Contains(fileName) && !fileName.Equals("Ext", StringComparison.OrdinalIgnoreCase))
				{
					Info info = Read(item2, attempts);
					string item = Path.GetFullPath(item2).TrimEnd('\\', '/');
					if (info.IsExtension)
					{
						found.Add(item);
					}
					else if (info.ReadFailed)
					{
						if (knownExtensions == null)
						{
							knownExtensions = LoadKnownExtensionDirs(mainRoot);
						}
						if (knownExtensions.Contains(item))
						{
							found.Add(item);
						}
					}
					else if (depth < 2 && !info.Exists)
					{
						Probe(item2, depth + 1);
					}
				}
			}
		}
	}

	private static HashSet<string> LoadKnownExtensionDirs(string mainRoot)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			string path = Path.Combine(mainRoot, ".index", "extension_names.json");
			if (!File.Exists(path))
			{
				return hashSet;
			}
			Dictionary<string, string> dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
			if (dictionary == null)
			{
				return hashSet;
			}
			foreach (string key in dictionary.Keys)
			{
				hashSet.Add(key.StartsWith("#id|", StringComparison.Ordinal) ? key.Substring("#id|".Length) : key);
			}
		}
		catch
		{
		}
		return hashSet;
	}

	internal static string? PickExtensionIndexRoot(IEnumerable<string> extensionDirs)
	{
		string text = null;
		foreach (string extensionDir in extensionDirs)
		{
			try
			{
				if (!string.IsNullOrEmpty(extensionDir) && Directory.Exists(extensionDir))
				{
					if (File.Exists(Path.Combine(extensionDir, ".index", "onec_search.db")))
					{
						return extensionDir;
					}
					if (text == null)
					{
						text = extensionDir;
					}
				}
			}
			catch
			{
			}
		}
		return text;
	}

	internal static string? NormalizeDir(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		try
		{
			string fullPath = Path.GetFullPath(path.Trim().Trim('"'));
			return string.Equals(fullPath, Path.GetPathRoot(fullPath), StringComparison.OrdinalIgnoreCase) ? fullPath : fullPath.TrimEnd('\\', '/');
		}
		catch
		{
			return null;
		}
	}

	internal static string? SanitizeExtensionName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		string text = new string((from c in name.Trim()
			where c != '/' && c != '\\' && c != '@' && c != '|' && c != '\'' && !char.IsControl(c)
			select c).ToArray()).Trim();
		if (text.Length != 0)
		{
			return text;
		}
		return null;
	}

	private static Info ReadCore(string file)
	{
		bool isExtension = false;
		string text = null;
		string text2 = null;
		List<string> list = new List<string>();
		using XmlReader xmlReader = XmlReader.Create(file, new XmlReaderSettings
		{
			IgnoreComments = true,
			IgnoreWhitespace = true
		});
		int num = -1;
		xmlReader.MoveToContent();
		while (!xmlReader.EOF && (num < 0 || xmlReader.NodeType != XmlNodeType.EndElement || xmlReader.Depth != num || !(xmlReader.LocalName == "Properties")))
		{
			if (xmlReader.NodeType != XmlNodeType.Element)
			{
				xmlReader.Read();
				continue;
			}
			string localName = xmlReader.LocalName;
			if (localName == "ChildObjects")
			{
				break;
			}
			if (localName == "Properties" && num < 0)
			{
				num = xmlReader.Depth;
				xmlReader.Read();
				continue;
			}
			if (num < 0 || xmlReader.Depth != num + 1)
			{
				xmlReader.Read();
				continue;
			}
			switch (localName)
			{
			case "ConfigurationExtensionPurpose":
				isExtension = true;
				xmlReader.Skip();
				break;
			case "Name":
			{
				string text4 = xmlReader.ReadElementContentAsString().Trim();
				if (text == null)
				{
					text = text4;
				}
				break;
			}
			case "NamePrefix":
				text2 = xmlReader.ReadElementContentAsString().Trim();
				break;
			case "Synonym":
			{
				using (XmlReader xmlReader2 = xmlReader.ReadSubtree())
				{
					while (xmlReader2.Read())
					{
						if (xmlReader2.NodeType == XmlNodeType.Element && !(xmlReader2.LocalName != "content"))
						{
							string text3 = xmlReader2.ReadElementContentAsString().Trim();
							if (text3.Length > 0)
							{
								list.Add(text3);
							}
						}
					}
				}
				xmlReader.Skip();
				break;
			}
			default:
				xmlReader.Skip();
				break;
			}
		}
		return new Info(Exists: true, isExtension, string.IsNullOrEmpty(text) ? null : text, string.IsNullOrEmpty(text2) ? null : text2, ReadFailed: false, list);
	}
}
