namespace Together;

internal sealed class ActorTimeline<T> where T : class
{
	private readonly SnapshotHistory<T> states = new SnapshotHistory<T>();

	private readonly SnapshotHistory<T> visuals = new SnapshotHistory<T>();

	public uint LayoutHash { get; private set; }

	public float LatestStateTime => states.LatestTime;

	public float LatestVisualTime => visuals.LatestTime;

	public void Bind(uint layoutHash, int sequence, float time, T sample)
	{
		if (LayoutHash != layoutHash)
		{
			visuals.Clear();
			LayoutHash = layoutHash;
		}
		Add(layoutHash, sequence, time, sample);
	}

	public void Add(uint layoutHash, int sequence, float time, T sample)
	{
		states.Add(sequence, time, sample);
		if (LayoutHash == layoutHash)
		{
			visuals.Add(sequence, time, sample);
		}
	}

	public void AddState(int sequence, float time, T sample)
	{
		states.Add(sequence, time, sample);
	}

	public void Reset()
	{
		states.Clear();
		visuals.Clear();
		LayoutHash = 0u;
	}

	public bool TryState(float time, out T previous, out T next, out float blend)
	{
		return states.TrySample(time, out previous, out next, out blend);
	}

	public bool TryVisual(float time, out T previous, out T next, out float blend)
	{
		return visuals.TrySample(time, out previous, out next, out blend);
	}
}
