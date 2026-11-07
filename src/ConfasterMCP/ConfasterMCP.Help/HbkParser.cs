using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ConfasterMCP.Help;

// Разбор .hbk: контейнер 1С (таблица из 7 записей + поток FileStorage), внутри — zip.
// Zip ищется по локальным заголовкам, а не через ZipArchive: у shlang_ru.hbk и
// shquery_ru.hbk нет корректного central directory (два EOCD или ни одного),
// поэтому ZipArchive видит только shcntx_ru.hbk.
internal static class HbkParser
{
	private const int LocalHeaderSize = 30;

	private const int MaxEntryNameBytes = 512;

	private const int MaxExtraBytes = 4096;

	// Больше не нужно: в индекс всё равно попадает не более 10000 символов.
	private const int MaxEntryBytes = 1 << 20;

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
		byte[] array2 = ReadFileStorage(array);
		if (array2 == null || array2.Length < LocalHeaderSize)
		{
			yield break;
		}
		int num = 0;
		while (num >= 0 && num + LocalHeaderSize <= array2.Length)
		{
			int num2 = FindLocalHeader(array2, num);
			if (num2 < 0)
			{
				yield break;
			}
			int num3 = BitConverter.ToUInt16(array2, num2 + 6);
			int num4 = BitConverter.ToUInt16(array2, num2 + 8);
			int num5 = (int)BitConverter.ToUInt32(array2, num2 + 18);
			int num6 = BitConverter.ToUInt16(array2, num2 + 26);
			int num7 = BitConverter.ToUInt16(array2, num2 + 28);
			int num8 = num2 + LocalHeaderSize + num6 + num7;
			string text = DecodeEntryName(array2, num2 + LocalHeaderSize, num6, (num3 & 0x800) != 0);
			num = num8 + num5;
			if ((text.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || text.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)) && text.IndexOf('\0') < 0)
			{
				byte[] array3 = ReadEntryData(array2, num8, num5, num4);
				if (array3 == null || array3.Length == 0)
				{
					continue;
				}
				string html = Encoding.UTF8.GetString(array3).TrimStart('\uFEFF');
				var (text2, text3) = ExtractTextFromHtml(html);
				if (!string.IsNullOrWhiteSpace(text3))
				{
					if (text3.Length > 10000)
					{
						text3 = text3.Substring(0, 10000);
					}
					string text4 = Path.GetFileNameWithoutExtension(text);
					yield return new HelpTopic
					{
						TopicName = text4,
						Title = (text2 ?? text4),
						Content = text3
					};
				}
			}
		}
	}

	private static byte[]? ReadFileStorage(byte[] data)
	{
		List<(int, int)> list = new List<(int, int)>();
		int num = 47;
		for (int i = 0; i < 7; i++)
		{
			if (num + 12 > data.Length)
			{
				break;
			}
			int item = BitConverter.ToInt32(data, num);
			int item2 = BitConverter.ToInt32(data, num + 4);
			list.Add((item, item2));
			num += 12;
		}
		foreach (var (num2, num3) in list)
		{
			if (num2 > 0 && num2 < data.Length && num3 > 0 && num3 < data.Length)
			{
				string text = ReadFileName(data, num2);
				if (text != null && text.Equals("FileStorage", StringComparison.OrdinalIgnoreCase))
				{
					return ReadBlockChain(data, num3);
				}
			}
		}
		return null;
	}

	// Локальные заголовки идут подряд; после данных записи снова ищем сигнатуру,
	// потому что между записями может встретиться выравнивание.
	private static int FindLocalHeader(byte[] zip, int start)
	{
		int num = start;
		while (num + LocalHeaderSize <= zip.Length)
		{
			if (zip[num] == 80 && zip[num + 1] == 75 && zip[num + 2] == 3 && zip[num + 3] == 4 && IsPlausibleLocalHeader(zip, num))
			{
				return num;
			}
			num++;
		}
		return -1;
	}

	private static bool IsPlausibleLocalHeader(byte[] zip, int header)
	{
		if (header < 0 || header + LocalHeaderSize > zip.Length)
		{
			return false;
		}
		int num = BitConverter.ToUInt16(zip, header + 6);
		int num2 = BitConverter.ToUInt16(zip, header + 8);
		if (num2 != 0 && num2 != 8)
		{
			return false;
		}
		if ((num & 8) != 0)
		{
			return false;
		}
		int num3 = BitConverter.ToUInt16(zip, header + 26);
		int num4 = BitConverter.ToUInt16(zip, header + 28);
		if (num3 <= 0 || num3 > MaxEntryNameBytes || num4 > MaxExtraBytes)
		{
			return false;
		}
		long num5 = (long)(uint)BitConverter.ToUInt32(zip, header + 18);
		long num6 = (long)header + LocalHeaderSize + num3 + num4;
		if (num6 + num5 > zip.Length)
		{
			return false;
		}
		int num7 = header + LocalHeaderSize;
		for (int i = 0; i < num3; i++)
		{
			byte b = zip[num7 + i];
			if (b < 32 || b == 127)
			{
				return false;
			}
		}
		return true;
	}

	private static string DecodeEntryName(byte[] zip, int offset, int length, bool utf8)
	{
		try
		{
			Encoding encoding = (utf8 ? Encoding.UTF8 : Encoding.Latin1);
			return encoding.GetString(zip, offset, length);
		}
		catch
		{
			return string.Empty;
		}
	}

	// Часть страниц в shlang/shquery сжата deflate с предустановленным словарём
	// (его задаёт упаковщик 1С). DeflateStream словарь задать не может, такие
	// записи пропускаются: их 3 из 25551.
	private static byte[]? ReadEntryData(byte[] zip, int offset, int compressedSize, int method)
	{
		if (compressedSize <= 0 || offset < 0 || offset + compressedSize > zip.Length)
		{
			return null;
		}
		try
		{
			if (method == 0)
			{
				int num = Math.Min(compressedSize, MaxEntryBytes);
				byte[] array = new byte[num];
				Buffer.BlockCopy(zip, offset, array, 0, num);
				return array;
			}
			using MemoryStream memoryStream = new MemoryStream(zip, offset, compressedSize, writable: false);
			using DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionMode.Decompress);
			using MemoryStream memoryStream2 = new MemoryStream();
			byte[] array2 = new byte[16384];
			int num2 = 0;
			while (num2 < MaxEntryBytes)
			{
				int num3 = deflateStream.Read(array2, 0, Math.Min(array2.Length, MaxEntryBytes - num2));
				if (num3 <= 0)
				{
					break;
				}
				memoryStream2.Write(array2, 0, num3);
				num2 += num3;
			}
			return memoryStream2.ToArray();
		}
		catch
		{
			return null;
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
			item = WebUtility.HtmlDecode(TagRegex.Replace(match.Groups[1].Value, "")).Trim();
			item = WhitespaceRegex.Replace(item, " ");
		}
		string input = ScriptStyleRegex.Replace(html, " ");
		input = TagRegex.Replace(input, " ");
		input = WebUtility.HtmlDecode(input);
		input = WhitespaceRegex.Replace(input, " ").Trim();
		return (title: item, text: input);
	}
}
