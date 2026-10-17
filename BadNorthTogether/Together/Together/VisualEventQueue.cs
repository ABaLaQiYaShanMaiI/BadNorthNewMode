using System;
using System.Collections.Generic;

namespace Together;

internal sealed class VisualEventQueue<T>
{
	private sealed class Entry
	{
		public T Value;

		public string Key;

		public float Time;

		public float ReceivedAt;
	}

	private readonly List<Entry> entries = new List<Entry>();

	public int Count => entries.Count;

	public bool Add(T value, string key, float time, float receivedAt)
	{
		if (entries.Count >= 2048)
		{
			return false;
		}
		entries.Add(new Entry
		{
			Value = value,
			Key = key,
			Time = time,
			ReceivedAt = receivedAt
		});
		return true;
	}

	public void Clear()
	{
		entries.Clear();
	}

	public void Visit(Action<T> visit)
	{
		foreach (Entry entry in entries)
		{
			visit(entry.Value);
		}
	}

	public void Process(float time, float realTime, Func<T, bool> apply, Action<T> expired)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		int num = 0;
		int num2 = 0;
		while (num2 < entries.Count && num < 128)
		{
			Entry entry = entries[num2];
			if (hashSet.Contains(entry.Key))
			{
				num2++;
				continue;
			}
			if (entry.Time > time)
			{
				hashSet.Add(entry.Key);
				num2++;
				continue;
			}
			num++;
			if (!apply(entry.Value))
			{
				if (realTime - entry.ReceivedAt < 10f)
				{
					hashSet.Add(entry.Key);
					num2++;
					continue;
				}
				expired(entry.Value);
			}
			entries.RemoveAt(num2);
		}
	}
}
