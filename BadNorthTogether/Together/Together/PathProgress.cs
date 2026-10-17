using System;
using System.IO;

namespace Together;

internal struct PathProgress
{
	public bool Known;

	public float Distance;

	internal const float MaxDistance = 100000f;

	internal static PathProgress FromDistance(float value)
	{
		if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 100000f)
		{
			return default(PathProgress);
		}
		return new PathProgress
		{
			Known = true,
			Distance = Math.Max(0f, value)
		};
	}

	internal PathProgress Canonical()
	{
		if (!Known)
		{
			return default(PathProgress);
		}
		return FromDistance(Distance);
	}

	internal void Write(BinaryWriter writer)
	{
		PathProgress pathProgress = Canonical();
		writer.Write(pathProgress.Known);
		writer.Write(pathProgress.Distance);
	}

	internal static PathProgress Read(BinaryReader reader)
	{
		PathProgress pathProgress = new PathProgress
		{
			Known = reader.ReadBoolean(),
			Distance = reader.ReadSingle()
		};
		return pathProgress.Canonical();
	}

	internal static PathProgress Blend(PathProgress previous, PathProgress next, float blend, bool sameTarget)
	{
		previous = previous.Canonical();
		next = next.Canonical();
		if (blend >= 0.9999f)
		{
			return next;
		}
		if (!sameTarget || !previous.Known || !next.Known)
		{
			return previous;
		}
		return FromDistance(previous.Distance + (next.Distance - previous.Distance) * Math.Max(0f, blend));
	}
}
