using System;
using System.Collections.Generic;
using System.IO;

namespace ConfasterMCP.Util;

internal static class PathHelper
{
	internal static readonly Dictionary<string, string> TypeFolderToXmlTag;

	internal static readonly Dictionary<string, string> XmlTagToTypeFolder;

	internal static string ToDisplayPath(string relativePath)
	{
		return relativePath.Replace("/Ext/Form/", "/", StringComparison.OrdinalIgnoreCase).Replace("\\Ext\\Form\\", "\\", StringComparison.OrdinalIgnoreCase).Replace("/Ext/", "/", StringComparison.OrdinalIgnoreCase)
			.Replace("\\Ext\\", "\\", StringComparison.OrdinalIgnoreCase);
	}

	internal static string GetRelativePath(string fullPath, string exportRoot)
	{
		return Path.GetRelativePath(exportRoot, fullPath).Replace('\\', '/');
	}

	static PathHelper()
	{
		TypeFolderToXmlTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["Documents"] = "Document",
			["Catalogs"] = "Catalog",
			["CommonModules"] = "CommonModule",
			["CommonForms"] = "CommonForm",
			["CommonCommands"] = "CommonCommand",
			["InformationRegisters"] = "InformationRegister",
			["AccumulationRegisters"] = "AccumulationRegister",
			["AccountingRegisters"] = "AccountingRegister",
			["CalculationRegisters"] = "CalculationRegister",
			["DataProcessors"] = "DataProcessor",
			["Reports"] = "Report",
			["Enums"] = "Enum",
			["ChartsOfCharacteristicTypes"] = "ChartOfCharacteristicTypes",
			["ChartsOfAccounts"] = "ChartOfAccounts",
			["ChartsOfCalculationTypes"] = "ChartOfCalculationTypes",
			["BusinessProcesses"] = "BusinessProcess",
			["Tasks"] = "Task",
			["ExchangePlans"] = "ExchangePlan",
			["Constants"] = "Constant",
			["Sequences"] = "Sequence",
			["DocumentJournals"] = "DocumentJournal",
			["FilterCriteria"] = "FilterCriterion",
			["SettingsStorages"] = "SettingsStorage",
			["FunctionalOptions"] = "FunctionalOption",
			["FunctionalOptionsParameters"] = "FunctionalOptionsParameter",
			["DefinedTypes"] = "DefinedType",
			["CommonAttributes"] = "CommonAttribute",
			["SessionParameters"] = "SessionParameter",
			["Roles"] = "Role",
			["CommonPictures"] = "CommonPicture",
			["CommonTemplates"] = "CommonTemplate",
			["XDTOPackages"] = "XDTOPackage",
			["WebServices"] = "WebService",
			["HTTPServices"] = "HTTPService",
			["WSReferences"] = "WSReference",
			["Styles"] = "Style",
			["Languages"] = "Language",
			["Subsystems"] = "Subsystem",
			["EventSubscriptions"] = "EventSubscription",
			["ScheduledJobs"] = "ScheduledJob",
			["CommandGroups"] = "CommandGroup",
			["ExternalDataSources"] = "ExternalDataSource",
			["Interfaces"] = "Interface"
		};
		XmlTagToTypeFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> item in TypeFolderToXmlTag)
		{
			XmlTagToTypeFolder[item.Value] = item.Key;
		}
	}

	internal static (string? typeFolder, string? objectName) ExtractObjectInfo(string relativePath)
	{
		string[] array = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (array.Length < 2)
		{
			return (typeFolder: null, objectName: null);
		}
		string text = array[0];
		if (!TypeFolderToXmlTag.ContainsKey(text))
		{
			return (typeFolder: null, objectName: null);
		}
		return (typeFolder: text, objectName: array[1]);
	}
}
