using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ConfasterMCP.Semantic;

internal static class Tokenizer
{
	private static readonly HashSet<string> _stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"и", "в", "на", "с", "по", "из", "для", "от", "до", "за",
		"к", "о", "что", "как", "это", "не", "а", "но", "или", "то",
		"же", "при", "который", "которая", "которые", "которого", "которую", "найди", "найти", "покажи",
		"покажите", "какая", "какой", "какие", "нужно", "надо", "хочу", "хочется", "поищи", "глянь",
		"посмотри", "смотри", "открой", "выведи", "пожалуйста", "плиз", "плз", "плс", "подскажи", "подскажите",
		"помоги", "помогите", "скажи", "скажите", "знаешь", "функция", "функцию", "функции", "процедура", "процедуру",
		"метод", "делает", "делать", "выполняет", "выполнять", "возвращает", "именно", "конкретно", "примерно", "точно",
		"вроде", "наверное", "видимо", "кажется", "вообще", "кстати", "тогда", "уже", "очень", "сильно",
		"тоже", "также", "тут", "там", "здесь", "везде", "куда", "откуда", "конфигурации", "конфигурация",
		"элемента", "объекта", "документа", "справочника", "регистра", "перечислений", "регистров", "констант", "обработок", "отчётов",
		"отчетов", "планов", "ролей", "подписок", "расширений", "расширения", "строки", "реквизита", "реквизитов", "значения"
	};

	internal static List<string> TokenizeIdentifier(string name)
	{
		List<string> list = new List<string>();
		if (string.IsNullOrEmpty(name))
		{
			return list;
		}
		int length = name.Length;
		int num = 0;
		int num2 = 0;
		while (num2 < length)
		{
			char c = name[num2];
			if (!char.IsLetter(c))
			{
				if (num2 > num)
				{
					string text = name.Substring(num, num2 - num);
					if (text.Length >= 2)
					{
						list.Add(text);
					}
				}
				num = num2 + 1;
				num2++;
				continue;
			}
			if (num2 > num && char.IsUpper(c))
			{
				if (char.IsLower(name[num2 - 1]))
				{
					string text2 = name.Substring(num, num2 - num);
					if (text2.Length >= 2)
					{
						list.Add(text2);
					}
					num = num2;
				}
				else if (char.IsUpper(name[num2 - 1]) && num2 + 1 < length && char.IsLower(name[num2 + 1]))
				{
					string text3 = name.Substring(num, num2 - num);
					if (text3.Length >= 2)
					{
						list.Add(text3);
					}
					num = num2;
				}
			}
			num2++;
		}
		if (num < length)
		{
			string text4 = name.Substring(num);
			if (text4.Length >= 2)
			{
				list.Add(text4);
			}
		}
		return list;
	}

	internal static List<string> TokenizeLower(string name)
	{
		List<string> list = TokenizeIdentifier(name);
		for (int i = 0; i < list.Count; i++)
		{
			list[i] = list[i].ToLower(CultureInfo.InvariantCulture);
		}
		return list;
	}

	internal static List<string> TokenizeQuery(string query)
	{
		List<string> list = new List<string>();
		if (string.IsNullOrEmpty(query))
		{
			return list;
		}
		List<string> list2 = new List<string>();
		int num = -1;
		for (int i = 0; i < query.Length; i++)
		{
			if (char.IsLetter(query[i]))
			{
				if (num < 0)
				{
					num = i;
				}
			}
			else if (num >= 0)
			{
				list2.Add(query.Substring(num, i - num));
				num = -1;
			}
		}
		if (num >= 0)
		{
			list2.Add(query.Substring(num));
		}
		foreach (string item in list2)
		{
			List<string> list3 = TokenizeIdentifier(item);
			if (list3.Count <= 1)
			{
				list3 = new List<string> { item };
			}
			foreach (string item2 in list3)
			{
				string text = item2.ToLower(CultureInfo.InvariantCulture);
				if (text.Length >= 2 && !_stopWords.Contains(text))
				{
					list.Add(text);
				}
			}
		}
		return list;
	}

	internal static string SanitizeForFts(string token)
	{
		StringBuilder stringBuilder = new StringBuilder(token.Length);
		foreach (char c in token)
		{
			if (char.IsLetterOrDigit(c))
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}
}
