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

	internal static IEnumerable<HelpTopic> Parse(string hbkPath, ISet<string>? failures = null)
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
			int num2 = FindLocalHeader(array2, num, failures);
			if (num2 < 0)
			{
				yield break;
			}
			int num3 = BitConverter.ToUInt16(array2, num2 + 6);
			int num4 = BitConverter.ToUInt16(array2, num2 + 8);
			uint crc = BitConverter.ToUInt32(array2, num2 + 14);
			int num5 = (int)BitConverter.ToUInt32(array2, num2 + 18);
			int num9 = (int)BitConverter.ToUInt32(array2, num2 + 22);
			int num6 = BitConverter.ToUInt16(array2, num2 + 26);
			int num7 = BitConverter.ToUInt16(array2, num2 + 28);
			int num8 = num2 + LocalHeaderSize + num6 + num7;
			string text = DecodeEntryName(array2, num2 + LocalHeaderSize, num6, (num3 & 0x800) != 0);
			num = num8 + num5;
			if (text.IndexOf('\0') < 0)
			{
				byte[] array3 = ReadEntryData(array2, num8, num5, num4, num9, crc);
				if (array3 == null || array3.Length == 0)
				{
					failures?.Add(Path.GetFileNameWithoutExtension(text));
					continue;
				}
				// Расширение не показатель: в shquery_ru.hbk страницы называются
				// KeyWordsSELECT, WhereStatement, JOIN и т. п., без .html. Тип
				// определяется по содержимому, шаблоны .st и рисунки отсеиваются.
				if (!LooksLikeHtml(array3))
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
	private static int FindLocalHeader(byte[] zip, int start, ISet<string>? failures = null)
	{
		int num = start;
		while (num + LocalHeaderSize <= zip.Length)
		{
			if (zip[num] == 80 && zip[num + 1] == 75 && zip[num + 2] == 3 && zip[num + 3] == 4)
			{
				if (IsPlausibleLocalHeader(zip, num))
				{
					return num;
				}
				// Последняя запись книги может быть обрезана: заголовок и имя
				// читаются, а объявленный размер данных выходит за границы
				// потока. Имя нужно сохранить, чтобы подставить английский дубль.
				CollectTruncatedEntryName(zip, num, failures);
			}
			num++;
		}
		return -1;
	}

	private static void CollectTruncatedEntryName(byte[] zip, int header, ISet<string>? failures)
	{
		if (failures == null || header < 0 || header + LocalHeaderSize > zip.Length)
		{
			return;
		}
		int num = BitConverter.ToUInt16(zip, header + 6);
		int num2 = BitConverter.ToUInt16(zip, header + 8);
		if (num2 != 0 && num2 != 8)
		{
			return;
		}
		if ((num & 8) != 0)
		{
			return;
		}
		int num3 = BitConverter.ToUInt16(zip, header + 26);
		int num4 = BitConverter.ToUInt16(zip, header + 28);
		if (num3 <= 0 || num3 > MaxEntryNameBytes || num4 > MaxExtraBytes)
		{
			return;
		}
		long num5 = header + LocalHeaderSize + num3 + num4;
		long num6 = (uint)BitConverter.ToUInt32(zip, header + 18);
		if (num5 + num6 <= zip.Length)
		{
			return;
		}
		int num7 = header + LocalHeaderSize;
		for (int i = 0; i < num3; i++)
		{
			byte b = zip[num7 + i];
			if (b < 32 || b == 127)
			{
				return;
			}
		}
		string text = DecodeEntryName(zip, num7, num3, (num & 0x800) != 0);
		if (text.Length > 0)
		{
			failures.Add(Path.GetFileNameWithoutExtension(text));
		}
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

	private static bool LooksLikeHtml(byte[] data)
	{
		int num = 0;
		if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
		{
			num = 3;
		}
		while (num < data.Length && (data[num] == 32 || data[num] == 9 || data[num] == 13 || data[num] == 10))
		{
			num++;
		}
		return num < data.Length && data[num] == 60;
	}

	// Тринадцать страниц в русских книгах shlang_ru.hbk и shquery_ru.hbk
	// повреждены: deflate-поток не даёт заявленных размеров, часть кодов битая.
	// Поэтому запись принимается только при совпадении и размера, и CRC-32;
	// иначе она попадает в failures и заменяется английским дублем.
	private static byte[]? ReadEntryData(byte[] zip, int offset, int compressedSize, int method, int uncompressedSize, uint crc)
	{
		if (compressedSize <= 0 || offset < 0 || offset + compressedSize > zip.Length)
		{
			return null;
		}
		try
		{
			byte[] array;
			if (method == 0)
			{
				if (uncompressedSize > 0 && compressedSize != uncompressedSize)
				{
					return null;
				}
				int num = Math.Min(compressedSize, MaxEntryBytes);
				array = new byte[num];
				Buffer.BlockCopy(zip, offset, array, 0, num);
			}
			else
			{
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
				if (uncompressedSize > 0 && memoryStream2.Length != uncompressedSize)
				{
					return null;
				}
				array = memoryStream2.ToArray();
			}
			if (crc != 0 && ComputeCrc32(array) != crc)
			{
				return null;
			}
			return array;
		}
		catch
		{
			return null;
		}
	}

	private static readonly uint[] Crc32Table = BuildCrc32Table();

	private static uint[] BuildCrc32Table()
	{
		uint[] array = new uint[256];
		for (uint num = 0u; num < 256; num++)
		{
			uint num2 = num;
			for (int i = 0; i < 8; i++)
			{
				num2 = (((num2 & 1) == 0) ? (num2 >> 1) : ((num2 >> 1) ^ 0xEDB88320u));
			}
			array[num] = num2;
		}
		return array;
	}

	private static uint ComputeCrc32(byte[] data)
	{
		uint num = uint.MaxValue;
		foreach (byte b in data)
		{
			num = Crc32Table[(num ^ b) & 0xFF] ^ (num >> 8);
		}
		return num ^ uint.MaxValue;
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
