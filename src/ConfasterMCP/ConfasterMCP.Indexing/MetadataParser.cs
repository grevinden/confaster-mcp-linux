using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using ConfasterMCP.Util;

namespace ConfasterMCP.Indexing;

internal static class MetadataParser
{
	internal static List<ObjectMetadata> ScanExportRoot(ConfigRoots roots, ConfigRoot root)
	{
		List<ObjectMetadata> list = new List<ObjectMetadata>();
		string exportRoot = root.Path;
		foreach (KeyValuePair<string, string> item in PathHelper.TypeFolderToXmlTag)
		{
			item.Deconstruct(out var key, out var value);
			string text = key;
			string text2 = value;
			string typePath = Path.Combine(exportRoot, text);
			if (!Directory.Exists(typePath))
			{
				continue;
			}
			foreach (string item2 in ListOrSkip(() => Directory.EnumerateFiles(typePath, "*.xml", SearchOption.TopDirectoryOnly), typePath))
			{
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(item2);
				string fqn = text2 + "." + fileNameWithoutExtension;
				string text3 = Path.Combine(typePath, fileNameWithoutExtension);
				ObjectMetadata objectMetadata = new ObjectMetadata
				{
					Fqn = fqn,
					TypeFolder = text,
					Name = fileNameWithoutExtension,
					ExportPath = ToKey(text3)
				};
				ParseObjectXml(item2, objectMetadata);
				if (Directory.Exists(text3))
				{
					ScanObjectDirectory(text3, ToKey, objectMetadata);
				}
				list.Add(objectMetadata);
			}
		}
		return list;
		string ToKey(string fullPath)
		{
			return roots.ToKey(root, PathHelper.GetRelativePath(fullPath, exportRoot));
		}
	}

	private static List<string> ListOrSkip(Func<IEnumerable<string>> enumerate, string dir)
	{
		try
		{
			return enumerate().ToList();
		}
		catch (Exception ex) when (((ex is UnauthorizedAccessException || ex is IOException) ? 1 : 0) != 0)
		{
			Console.Error.WriteLine("MCP_LOG: metadata: skipped unreadable folder " + dir + ": " + ex.Message);
			Console.Error.Flush();
			return new List<string>();
		}
	}

	private static void ParseObjectXml(string xmlFile, ObjectMetadata metadata)
	{
		try
		{
			XmlDocument xmlDocument = new XmlDocument();
			xmlDocument.Load(xmlFile);
			XmlNamespaceManager xmlNamespaceManager = new XmlNamespaceManager(xmlDocument.NameTable);
			xmlNamespaceManager.AddNamespace("v8", "http://v8.1c.ru/8.3/MDClasses");
			xmlNamespaceManager.AddNamespace("core", "http://v8.1c.ru/8.1/data/core");
			xmlNamespaceManager.AddNamespace("xs", "http://www.w3.org/2001/XMLSchema");
			XmlElement xmlElement = xmlDocument.DocumentElement?.ChildNodes.OfType<XmlElement>().FirstOrDefault();
			if (xmlElement != null && IsAdopted(xmlElement))
			{
				metadata.Adopted = true;
			}
			if (xmlDocument.SelectNodes("//core:item/core:content", xmlNamespaceManager) != null)
			{
				XmlElement documentElement = xmlDocument.DocumentElement;
				if (documentElement != null)
				{
					{
						IEnumerator enumerator = documentElement.GetElementsByTagName("Synonym").GetEnumerator();
						try
						{
							if (enumerator.MoveNext())
							{
								foreach (XmlNode childNode in ((XmlNode)enumerator.Current).ChildNodes)
								{
									if (!(childNode.LocalName == "item"))
									{
										continue;
									}
									foreach (XmlNode childNode2 in childNode.ChildNodes)
									{
										if (childNode2.LocalName == "content")
										{
											string text = childNode2.InnerText?.Trim();
											if (!string.IsNullOrEmpty(text))
											{
												metadata.Synonyms.Add(text);
											}
										}
									}
								}
							}
						}
						finally
						{
							IDisposable disposable = enumerator as IDisposable;
							if (disposable != null)
							{
								disposable.Dispose();
							}
						}
					}
				}
			}
			XmlNodeList elementsByTagName = xmlDocument.GetElementsByTagName("ChildObjects");
			if (elementsByTagName.Count <= 0)
			{
				return;
			}
			foreach (XmlNode childNode3 in elementsByTagName[0].ChildNodes)
			{
				if (childNode3.NodeType != XmlNodeType.Element)
				{
					continue;
				}
				string localName = childNode3.LocalName;
				string text2 = ExtractChildName(childNode3);
				if (!string.IsNullOrEmpty(text2))
				{
					string text3 = localName switch
					{
						"Attribute" => "attribute", 
						"TabularSection" => "tabular_section", 
						"Form" => "form", 
						"Command" => "command", 
						"Template" => "template", 
						"Dimension" => "dimension", 
						"Resource" => "resource", 
						"Column" => "column", 
						"AccountingFlag" => "accounting_flag", 
						"ExtDimensionAccountingFlag" => "ext_dimension_flag", 
						"AddressingAttribute" => "addressing_attribute", 
						_ => null, 
					};
					if (text3 != null)
					{
						metadata.Items.Add(new ObjectItemInfo(text3, text2, null, IsAdopted(childNode3)));
					}
				}
			}
		}
		catch
		{
		}
	}

	private static void ScanObjectDirectory(string objectDir, Func<string, string> toKey, ObjectMetadata metadata)
	{
		string extDir = Path.Combine(objectDir, "Ext");
		if (Directory.Exists(extDir))
		{
			foreach (string item in ListOrSkip(() => Directory.EnumerateFiles(extDir, "*.bsl"), extDir))
			{
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(item);
				string name = fileNameWithoutExtension switch
				{
					"ObjectModule" => "object", 
					"ManagerModule" => "manager", 
					"Module" => "module", 
					"RecordSetModule" => "recordset", 
					"ValueManagerModule" => "value_manager", 
					"CommandModule" => "command", 
					"ManagedApplicationModule" => "application", 
					"SessionModule" => "session", 
					"ExternalConnectionModule" => "external_connection", 
					_ => fileNameWithoutExtension.ToLowerInvariant(), 
				};
				metadata.Items.Add(new ObjectItemInfo("module", name, toKey(item)));
			}
		}
		string formsDir = Path.Combine(objectDir, "Forms");
		if (Directory.Exists(formsDir))
		{
			foreach (string item2 in ListOrSkip(() => Directory.EnumerateDirectories(formsDir), formsDir))
			{
				string fileName = Path.GetFileName(item2);
				string text = Path.Combine(item2, "Ext", "Form", "Module.bsl");
				if (File.Exists(text))
				{
					metadata.Items.Add(new ObjectItemInfo("form_module", fileName, toKey(text)));
				}
			}
		}
		string commandsDir = Path.Combine(objectDir, "Commands");
		if (Directory.Exists(commandsDir))
		{
			foreach (string item3 in ListOrSkip(() => Directory.EnumerateDirectories(commandsDir), commandsDir))
			{
				string text2 = Path.Combine(item3, "Ext", "CommandModule.bsl");
				if (File.Exists(text2))
				{
					string fileName2 = Path.GetFileName(item3);
					metadata.Items.Add(new ObjectItemInfo("command_module", fileName2, toKey(text2)));
				}
			}
		}
		foreach (string item4 in ListOrSkip(() => Directory.EnumerateFiles(objectDir, "*.xml", SearchOption.TopDirectoryOnly), objectDir))
		{
			if (!(Path.GetFileName(item4) == Path.GetFileName(objectDir) + ".xml"))
			{
				ParseTabularSectionXml(item4, metadata);
			}
		}
	}

	private static void ParseTabularSectionXml(string xmlFile, ObjectMetadata metadata)
	{
		try
		{
			XmlDocument xmlDocument = new XmlDocument();
			xmlDocument.Load(xmlFile);
			XmlNodeList elementsByTagName = xmlDocument.GetElementsByTagName("ChildObjects");
			if (elementsByTagName.Count == 0)
			{
				return;
			}
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(xmlFile);
			foreach (XmlNode childNode in elementsByTagName[0].ChildNodes)
			{
				if (childNode.NodeType == XmlNodeType.Element && childNode.LocalName == "Attribute")
				{
					string text = ExtractChildName(childNode);
					if (!string.IsNullOrEmpty(text))
					{
						metadata.Items.Add(new ObjectItemInfo("ts_attribute", text, fileNameWithoutExtension, IsAdopted(childNode)));
					}
				}
			}
		}
		catch
		{
		}
	}

	private static string ExtractChildName(XmlNode element)
	{
		foreach (XmlNode childNode in element.ChildNodes)
		{
			if (childNode.NodeType != XmlNodeType.Element || !(childNode.LocalName == "Properties"))
			{
				continue;
			}
			foreach (XmlNode childNode2 in childNode.ChildNodes)
			{
				if (childNode2.NodeType == XmlNodeType.Element && childNode2.LocalName == "Name")
				{
					return childNode2.InnerText?.Trim() ?? "";
				}
			}
			return "";
		}
		foreach (XmlNode childNode3 in element.ChildNodes)
		{
			if (childNode3.NodeType == XmlNodeType.Element)
			{
				return "";
			}
		}
		return element.InnerText?.Trim() ?? "";
	}

	private static bool IsAdopted(XmlNode element)
	{
		foreach (XmlNode childNode in element.ChildNodes)
		{
			if (childNode.NodeType != XmlNodeType.Element || childNode.LocalName != "Properties")
			{
				continue;
			}
			foreach (XmlNode childNode2 in childNode.ChildNodes)
			{
				if (childNode2.NodeType == XmlNodeType.Element && childNode2.LocalName == "ObjectBelonging")
				{
					return string.Equals(childNode2.InnerText?.Trim(), "Adopted", StringComparison.OrdinalIgnoreCase);
				}
			}
			return false;
		}
		return false;
	}
}
