using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ConfasterMCP.Help;

internal static class HbkParser
{
	private static readonly Regex TagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

	private static readonly Regex ScriptStyleRegex = new Regex("<(script|style|nav)[^>]*>[\\s\\S]*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex WhitespaceRegex = new Regex("\\s{2,}", RegexOptions.Compiled);

	private static readonly Regex TitleRegex = new Regex("<(?:h1|h2|title)[^>]*>(.*?)</(?:h1|h2|title)>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

	internal static IEnumerable<HelpTopic> Parse(string hbkPath)
	{
		byte[] array = File.ReadAllBytes(hbkPath);
		if (array.Length < 100)
		{
			yield break;
		}
		List<(int, int)> list = new List<(int, int)>();
		int num = 47;
		for (int i = 0; i < 7; i++)
		{
			if (num + 12 > array.Length)
			{
				break;
			}
			int item = BitConverter.ToInt32(array, num);
			int item2 = BitConverter.ToInt32(array, num + 4);
			list.Add((item, item2));
			num += 12;
		}
		byte[] array2 = null;
		foreach (var (num2, num3) in list)
		{
			if (num2 > 0 && num2 < array.Length && num3 > 0 && num3 < array.Length)
			{
				string text = ReadFileName(array, num2);
				if (text != null && text.Equals("FileStorage", StringComparison.OrdinalIgnoreCase))
				{
					array2 = ReadBlockChain(array, num3);
					break;
				}
			}
		}
		if (array2 == null || array2.Length < 10)
		{
			yield break;
		}
		using MemoryStream zipStream = new MemoryStream(array2);
		using ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			if ((!entry.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) && !entry.FullName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)) || entry.Length == 0L)
			{
				continue;
			}
			string text2;
			try
			{
				using Stream stream = entry.Open();
				using StreamReader streamReader = new StreamReader(stream, Encoding.UTF8);
				text2 = streamReader.ReadToEnd();
			}
			catch
			{
				continue;
			}
			if (string.IsNullOrWhiteSpace(text2))
			{
				continue;
			}
			var (text3, text4) = ExtractTextFromHtml(text2);
			if (!string.IsNullOrWhiteSpace(text4))
			{
				if (text4.Length > 10000)
				{
					text4 = text4.Substring(0, 10000);
				}
				yield return new HelpTopic
				{
					TopicName = Path.GetFileNameWithoutExtension(entry.FullName),
					Title = (text3 ?? Path.GetFileNameWithoutExtension(entry.FullName)),
					Content = text4
				};
			}
		}
	}

	private static string? ReadFileName(byte[] data, int headerAddr)
	{
		try
		{
			int num = headerAddr + 31;
			num += 20;
			if (num >= data.Length)
			{
				return null;
			}
			List<byte> list = new List<byte>();
			for (int i = num; i + 1 < data.Length; i += 2)
			{
				if (list.Count >= 200)
				{
					break;
				}
				if (BitConverter.ToUInt16(data, i) == 0)
				{
					break;
				}
				list.Add(data[i]);
				list.Add(data[i + 1]);
			}
			if (list.Count == 0)
			{
				return null;
			}
			return Encoding.Unicode.GetString(list.ToArray());
		}
		catch
		{
			return null;
		}
	}

	private static byte[] ReadBlockChain(byte[] data, int startAddr)
	{
		using MemoryStream memoryStream = new MemoryStream();
		int num = startAddr;
		int num2 = 100000;
		int payloadSize;
		int blockSize;
		int nextBlock;
		while (num > 0 && num < data.Length && num2-- > 0 && num + 31 <= data.Length && TryParseBlockHeader(Encoding.ASCII.GetString(data, num, 31), out payloadSize, out blockSize, out nextBlock))
		{
			int num3 = num + 31;
			if (num3 + payloadSize > data.Length)
			{
				payloadSize = data.Length - num3;
			}
			if (payloadSize > 0)
			{
				memoryStream.Write(data, num3, payloadSize);
			}
			if (nextBlock == int.MaxValue || nextBlock <= 0)
			{
				break;
			}
			num = nextBlock;
		}
		return memoryStream.ToArray();
	}

	private static bool TryParseBlockHeader(string header, out int payloadSize, out int blockSize, out int nextBlock)
	{
		payloadSize = 0;
		blockSize = 0;
		nextBlock = 0;
		try
		{
			string value = header.Substring(2, 8).Trim();
			string value2 = header.Substring(11, 8).Trim();
			string value3 = header.Substring(20, 8).Trim();
			payloadSize = Convert.ToInt32(value, 16);
			blockSize = Convert.ToInt32(value2, 16);
			nextBlock = Convert.ToInt32(value3, 16);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static (string? title, string text) ExtractTextFromHtml(string html)
	{
		string item = null;
		Match match = TitleRegex.Match(html);
		if (match.Success)
		{
			item = TagRegex.Replace(match.Groups[1].Value, "").Trim();
		}
		string input = ScriptStyleRegex.Replace(html, " ");
		input = TagRegex.Replace(input, " ");
		input = WebUtility.HtmlDecode(input);
		input = WhitespaceRegex.Replace(input, " ").Trim();
		return (title: item, text: input);
	}
}
