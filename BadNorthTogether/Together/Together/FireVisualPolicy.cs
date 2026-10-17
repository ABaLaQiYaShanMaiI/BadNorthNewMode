using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Together;

internal static class FireVisualPolicy
{
	public static bool KeepComponent(Type type)
	{
		if (!typeof(Transform).IsAssignableFrom(type) && !typeof(ParticleSystem).IsAssignableFrom(type))
		{
			return typeof(ParticleSystemRenderer).IsAssignableFrom(type);
		}
		return true;
	}
}
