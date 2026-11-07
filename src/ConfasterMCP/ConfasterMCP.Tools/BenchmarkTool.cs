using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace ConfasterMCP.Tools;

internal sealed class BenchmarkTool : IConfasterMCPTool
{
	private readonly ToolRegistry _registry;

	public string Name => "benchmark";

	public string Description => "Замер производительности инструментов поиска: min/avg/p95/max latency в мс.";

	public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
	{
		type = "object",
		properties = new
		{
			iterations = new
			{
				type = "integer",
				description = "Количество итераций (по умолчанию 20, max 100)",
				@default = 20
			}
		}
	});

	internal BenchmarkTool(ToolRegistry registry)
	{
		_registry = registry;
	}

	public string Execute(JsonElement args)
	{
		int value = (args.TryGetProperty("iterations", out var value2) ? value2.GetInt32() : 20);
		value = Math.Clamp(value, 1, 100);
		List<object> list = new List<object>();
		BenchTool("find_symbol", JsonSerializer.SerializeToElement(new
		{
			query = "Печать",
			limit = 5
		}), value, list);
		BenchTool("search_code", JsonSerializer.SerializeToElement(new
		{
			query = "Процедура",
			limit = 5
		}), value, list);
		BenchTool("list_objects", JsonSerializer.SerializeToElement(new
		{
			limit = 10
		}), value, list);
		BenchTool("stats", default(JsonElement), value, list);
		return JsonSerializer.Serialize(new
		{
			benchmarks = list,
			iterations = value
		});
	}

	private void BenchTool(string toolName, JsonElement args, int iterations, List<object> results)
	{
		IConfasterMCPTool tool = _registry.GetTool(toolName);
		if (tool == null)
		{
			return;
		}
		List<double> list = new List<double>(iterations);
		for (int i = 0; i < iterations; i++)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				tool.Execute(args);
			}
			catch
			{
			}
			stopwatch.Stop();
			list.Add(stopwatch.Elapsed.TotalMilliseconds);
		}
		list.Sort();
		int val = (int)((double)list.Count * 0.95);
		results.Add(new
		{
			tool = toolName,
			min_ms = Math.Round(list[0], 2),
			avg_ms = Math.Round(list.Average(), 2),
			p95_ms = Math.Round(list[Math.Min(val, list.Count - 1)], 2),
			max_ms = Math.Round(list[list.Count - 1], 2)
		});
	}
}
