using System;
using System.IO;
using System.Text;

namespace ConfasterMCP.Util;

internal static class EncodingHelper
{
	static EncodingHelper()
	{
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
	}

	internal static string ReadFileText(string path)
	{
		byte[] array = File.ReadAllBytes(path);
		if (array.Length == 0)
		{
			return "";
		}
		int num = 0;
		if (array.Length >= 3 && array[0] == 239 && array[1] == 187 && array[2] == 191)
		{
			num = 3;
		}
		try
		{
			return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(array, num, array.Length - num);
		}
		catch (DecoderFallbackException)
		{
		}
		return Encoding.GetEncoding(1251).GetString(array);
	}

	internal static string[] SplitLines(string text)
	{
		return text.Split(new string[3] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
	}

	internal static (byte[] utf8Bytes, string[] lines) ReadFileForParsing(string path)
	{
		byte[] array = File.ReadAllBytes(path);
		if (array.Length == 0)
		{
			return (utf8Bytes: Array.Empty<byte>(), lines: new string[1] { "" });
		}
		int num = 0;
		if (array.Length >= 3 && array[0] == 239 && array[1] == 187 && array[2] == 191)
		{
			num = 3;
		}
		try
		{
			string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(array, num, array.Length - num);
			byte[] array2;
			if (num == 0)
			{
				array2 = array;
			}
			else
			{
				array2 = new byte[array.Length - num];
				Buffer.BlockCopy(array, num, array2, 0, array.Length - num);
			}
			return (utf8Bytes: array2, lines: text.Split('\n'));
		}
		catch (DecoderFallbackException)
		{
		}
		string text2 = Encoding.GetEncoding(1251).GetString(array);
		return (utf8Bytes: Encoding.UTF8.GetBytes(text2), lines: text2.Split('\n'));
	}
}
