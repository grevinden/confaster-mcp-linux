using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using ConfasterMCP.Util;

namespace ConfasterMCP.Indexing;

internal static class MetadataParser
{
	internal static List<ObjectMetadata> ScanExportRoot(string exportRoot)
	{
		List<ObjectMetadata> list = new List<ObjectMetadata>();
		foreach (KeyValuePair<string, string> item in PathHelper.TypeFolderToXmlTag)
		{
			item.Deconstruct(out var key, out var value);
			string text = key;
			string text2 = value;
			string text3 = Path.Combine(exportRoot, text);
			if (!Directory.Exists(text3))
			{
				continue;
			}
			foreach (string item2 in Directory.EnumerateFiles(text3, "*.xml", SearchOption.TopDirectoryOnly))
			{
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(item2);
				string fqn = text2 + "." + fileNameWithoutExtension;
				string text4 = Path.Combine(text3, fileNameWithoutExtension);
				ObjectMetadata objectMetadata = new ObjectMetadata
				{
					Fqn = fqn,
					TypeFolder = text,
					Name = fileNameWithoutExtension,
					ExportPath = PathHelper.GetRelativePath(text4, exportRoot)
				};
				ParseObjectXml(item2, objectMetadata);
				if (Directory.Exists(text4))
				{
					ScanObjectDirectory(text4, exportRoot, objectMetadata);
				}
				list.Add(objectMetadata);
			}
		}
		return list;
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
					switch (localName)
					{
					case "Attribute":
						metadata.Items.Add(new ObjectItemInfo("attribute", text2, null));
						break;
					case "TabularSection":
						metadata.Items.Add(new ObjectItemInfo("tabular_section", text2, null));
						break;
					case "Form":
						metadata.Items.Add(new ObjectItemInfo("form", text2, null));
						break;
					case "Command":
						metadata.Items.Add(new ObjectItemInfo("command", text2, null));
						break;
					case "Template":
						metadata.Items.Add(new ObjectItemInfo("template", text2, null));
						break;
					case "Dimension":
						metadata.Items.Add(new ObjectItemInfo("dimension", text2, null));
						break;
					case "Resource":
						metadata.Items.Add(new ObjectItemInfo("resource", text2, null));
						break;
					case "Column":
						metadata.Items.Add(new ObjectItemInfo("column", text2, null));
						break;
					case "AccountingFlag":
						metadata.Items.Add(new ObjectItemInfo("accounting_flag", text2, null));
						break;
					case "ExtDimensionAccountingFlag":
						metadata.Items.Add(new ObjectItemInfo("ext_dimension_flag", text2, null));
						break;
					case "AddressingAttribute":
						metadata.Items.Add(new ObjectItemInfo("addressing_attribute", text2, null));
						break;
					}
				}
			}
		}
		catch
		{
		}
	}

	private static void ScanObjectDirectory(string objectDir, string exportRoot, ObjectMetadata metadata)
	{
		string path = Path.Combine(objectDir, "Ext");
		if (Directory.Exists(path))
		{
			foreach (string item in Directory.EnumerateFiles(path, "*.bsl"))
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
				metadata.Items.Add(new ObjectItemInfo("module", name, PathHelper.GetRelativePath(item, exportRoot)));
			}
		}
		string path2 = Path.Combine(objectDir, "Forms");
		if (Directory.Exists(path2))
		{
			foreach (string item2 in Directory.EnumerateDirectories(path2))
			{
				string fileName = Path.GetFileName(item2);
				string text = Path.Combine(item2, "Ext", "Form", "Module.bsl");
				if (File.Exists(text))
				{
					metadata.Items.Add(new ObjectItemInfo("form_module", fileName, PathHelper.GetRelativePath(text, exportRoot)));
				}
			}
		}
		string path3 = Path.Combine(objectDir, "Commands");
		if (Directory.Exists(path3))
		{
			foreach (string item3 in Directory.EnumerateDirectories(path3))
			{
				string text2 = Path.Combine(item3, "Ext", "CommandModule.bsl");
				if (File.Exists(text2))
				{
					string fileName2 = Path.GetFileName(item3);
					metadata.Items.Add(new ObjectItemInfo("command_module", fileName2, PathHelper.GetRelativePath(text2, exportRoot)));
				}
			}
		}
		foreach (string item4 in Directory.EnumerateFiles(objectDir, "*.xml", SearchOption.TopDirectoryOnly))
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
						metadata.Items.Add(new ObjectItemInfo("ts_attribute", text, fileNameWithoutExtension));
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
}
