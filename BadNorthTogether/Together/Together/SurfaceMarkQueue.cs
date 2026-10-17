using System;

namespace Together;

internal sealed class SurfaceMarkQueue
{
	private readonly VisualEventQueue<SurfaceMarkData> pending = new VisualEventQueue<SurfaceMarkData>();

	public int Count => pending.Count;

	public bool Add(SurfaceMarkData data, float receivedAt)
	{
		return pending.Add(data, data.Key, data.Time, receivedAt);
	}

	public void Clear()
	{
		pending.Clear();
	}

	public void Process(float playbackTime, float realTime, Func<SurfaceMarkData, bool> apply, Action<SurfaceMarkData> expired)
	{
		pending.Process(playbackTime, realTime, apply, expired);
	}
}
