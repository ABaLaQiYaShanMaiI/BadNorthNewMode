using System;
using System.Collections.Generic;
using System.IO;

namespace Together;

internal sealed class RetiredIds
{
	private struct Range
	{
		public int First;

		public int Last;
	}

	private readonly List<Range> ranges = new List<Range>();

	private readonly int maxRanges;

	internal int RangeCount => ranges.Count;

	public RetiredIds(int limit = 8193)
	{
		maxRanges = limit;
	}

	public bool Contains(int id)
	{
		int num = 0;
		int num2 = ranges.Count - 1;
		while (num <= num2)
		{
			int num3 = num + (num2 - num) / 2;
			Range range = ranges[num3];
			if (id < range.First)
			{
				num2 = num3 - 1;
				continue;
			}
			if (id > range.Last)
			{
				num = num3 + 1;
				continue;
			}
			return true;
		}
		return false;
	}

	public void Add(int id)
	{
		AddRange(id, id);
	}

	public void AddRange(int first, int last)
	{
		if (first < 0 || last < first)
		{
			throw new IOException("退场墓碑编号非法");
		}
		int i;
		for (i = 0; i < ranges.Count && (long)ranges[i].Last + 1L < first; i++)
		{
		}
		int num = i;
		for (; i < ranges.Count && ranges[i].First <= (long)last + 1L; i++)
		{
			first = Math.Min(first, ranges[i].First);
			last = Math.Max(last, ranges[i].Last);
		}
		if (i == num && ranges.Count >= maxRanges)
		{
			throw new IOException("退场墓碑空洞超过当前实体预算");
		}
		if (i > num)
		{
			ranges.RemoveRange(num, i - num);
		}
		ranges.Insert(num, new Range
		{
			First = first,
			Last = last
		});
	}

	public void Clear()
	{
		ranges.Clear();
	}
}
