using System;

namespace Together;

internal sealed class NetworkClock
{
	private readonly float delay;

	private float latest;

	private float rate = 1f;

	private bool initialized;

	private int sequence = -1;

	public float Time { get; private set; }

	public float Latest => latest;

	public NetworkClock(float delay = 0.12f)
	{
		if (!Finite(delay))
		{
			throw new ArgumentOutOfRangeException("delay");
		}
		this.delay = Math.Max(0.03f, Math.Min(0.5f, delay));
	}

	public bool Observe(int sequence, float hostTime, float hostRate)
	{
		if (sequence <= this.sequence || hostTime < latest || !Finite(hostTime) || !Finite(hostRate) || hostTime < 0f || hostRate < 0f || hostRate > 10f)
		{
			return false;
		}
		this.sequence = sequence;
		latest = hostTime;
		rate = hostRate;
		if (!initialized)
		{
			Time = Math.Max(0f, latest - delay * Math.Max(0.1f, rate));
			initialized = true;
		}
		return true;
	}

	public void Advance(float realDelta)
	{
		if (initialized && Finite(realDelta) && !(realDelta < 0f))
		{
			float num = Math.Max(0f, latest - delay * Math.Max(0.1f, rate));
			float num2 = rate;
			if (num - Time > 0.25f)
			{
				num2 = Math.Max(rate, 1f) * 1.25f;
			}
			if (rate == 0f)
			{
				num2 = Math.Max(0.1f, Math.Min(1f, latest - Time));
			}
			Time = Math.Min(latest, Time + Math.Min(realDelta, 0.2f) * num2);
			if (num > Time + 1f)
			{
				Time = num;
			}
		}
	}

	public void ConfirmTime(float hostTime)
	{
		if (Finite(hostTime) && !(hostTime < latest))
		{
			latest = hostTime;
			initialized = true;
		}
	}

	public void FreezeAt(float hostTime)
	{
		if (Finite(hostTime) && !(hostTime < latest))
		{
			float num = (Time = hostTime);
			latest = num;
			rate = 0f;
			initialized = true;
		}
	}

	public void Reset()
	{
		float num = (Time = 0f);
		latest = num;
		rate = 1f;
		sequence = -1;
		initialized = false;
	}

	private static bool Finite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}
}
