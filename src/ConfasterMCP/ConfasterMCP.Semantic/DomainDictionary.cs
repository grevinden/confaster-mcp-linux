using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConfasterMCP.Semantic;

internal static class DomainDictionary
{
	internal sealed class DomainAlias
	{
		[JsonPropertyName("term")]
		public string term { get; set; } = "";

		[JsonPropertyName("alias")]
		public string alias { get; set; } = "";

		[JsonPropertyName("weight")]
		public double weight { get; set; } = 1.0;
	}

	private static List<DomainAlias>? _entries;

	private static readonly object _lock = new object();

	internal static IReadOnlyList<DomainAlias> Entries
	{
		get
		{
			if (_entries != null)
			{
				return _entries;
			}
			lock (_lock)
			{
				if (_entries == null)
				{
					_entries = LoadFromResource();
				}
			}
			return _entries;
		}
	}

	private static List<DomainAlias> LoadFromResource()
	{
		Assembly assembly = typeof(DomainDictionary).Assembly;
		string name = "ConfasterMCP.Semantic.domain_dictionary.json";
		using Stream stream = assembly.GetManifestResourceStream(name);
		if (stream == null)
		{
			return new List<DomainAlias>();
		}
		try
		{
			return JsonSerializer.Deserialize<List<DomainAlias>>(stream, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			}) ?? new List<DomainAlias>();
		}
		catch
		{
			return new List<DomainAlias>();
		}
	}
}
