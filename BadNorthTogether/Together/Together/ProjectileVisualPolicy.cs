using System;
using System.Collections.Generic;

namespace Together;

internal static class ProjectileVisualPolicy
{
	public static T[] Select<T>(IEnumerable<T> nodes, Func<T, bool> supported, Func<T, bool> generated)
	{
		List<T> list = new List<T>();
		foreach (T node in nodes)
		{
			if (supported(node) && !generated(node))
			{
				list.Add(node);
			}
		}
		return list.ToArray();
	}
}
