using System;
using System.Collections.Generic;

namespace ConfasterMCP.TreeSitter;

internal static class BslKeywords
{
	private static readonly HashSet<string> _set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"если", "иначеесли", "иначе", "конецесли", "пока", "конецпока", "для", "каждого", "из", "по",
		"конеццикла", "конецдля", "попытка", "исключение", "вызватьисключение", "конецпопытки", "возврат", "прервать", "продолжить", "перейти",
		"новый", "истина", "ложь", "неопределено", "null", "и", "или", "не", "экспорт", "перем",
		"if", "elseif", "else", "endif", "while", "endwhile", "for", "each", "in", "to",
		"endfor", "try", "except", "raise", "endtry", "return", "break", "continue", "goto", "new",
		"true", "false", "undefined", "and", "or", "not", "export", "var"
	};

	internal static bool IsKeyword(string name)
	{
		return _set.Contains(name);
	}
}
