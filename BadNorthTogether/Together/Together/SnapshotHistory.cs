using System;
using System.Collections.Generic;

namespace Together;

internal sealed class SnapshotHistory<T> where T : class
{
	private struct Entry
	{
		public int Sequence;

		public float Time;

		public T Value;
	}

	private readonly List<Entry> entries = new List<Entry>();

	private readonly int capacity;

	private int lastSequence = -1;

	public int Count => entries.Count;

	public float LatestTime
	{
		get
		{
			if (entries.Count != 0)
			{
				return entries[entries.Count - 1].Time;
			}
			return 0f;
		}
	}

	public SnapshotHistory(int capacity = 48)
	{
		if (capacity < 2 || capacity > 512)
		{
			throw new ArgumentOutOfRangeException("capacity");
		}
		this.capacity = capacity;
	}

	public bool Add(int sequence, float time, T value)
	{
		if (value == null || sequence < 0 || sequence <= lastSequence || float.IsNaN(time) || float.IsInfinity(time) || time < 0f || (entries.Count > 0 && time < LatestTime))
		{
			return false;
		}
		lastSequence = sequence;
		if (entries.Count > 0 && time == LatestTime)
		{
			entries.RemoveAt(entries.Count - 1);
		}
		entries.Add(new Entry
		{
			Sequence = sequence,
			Time = time,
			Value = value
		});
		if (entries.Count > capacity)
		{
			entries.RemoveAt(0);
		}
		return true;
	}

	public bool TrySample(float time, out T previous, out T next, out float blend)
	{
		previous = (next = null);
		blend = 0f;
		if (entries.Count == 0 || float.IsNaN(time) || float.IsInfinity(time))
		{
			return false;
		}
		if (time <= entries[0].Time)
		{
			previous = (next = entries[0].Value);
			return true;
		}
		for (int i = 1; i < entries.Count; i++)
		{
			if (!(entries[i].Time < time))
			{
				previous = entries[i - 1].Value;
				next = entries[i].Value;
				blend = Math.Max(0f, Math.Min(1f, (time - entries[i - 1].Time) / (entries[i].Time - entries[i - 1].Time)));
				return true;
			}
		}
		previous = (next = entries[entries.Count - 1].Value);
		return true;
	}

	public void Clear()
	{
		entries.Clear();
		lastSequence = -1;
	}
}
