using System;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.SpriteMagic;

namespace Together;

internal static class VisualScenePolicy
{
	private static readonly Type[] transientTypes = new Type[10]
	{
		typeof(SelfPoolingPrefab),
		typeof(PoolablePrefab),
		typeof(ReusableEffect),
		typeof(Godray),
		typeof(SpriteStamperTrunk),
		typeof(SpriteStamper),
		typeof(ParticleSystem),
		typeof(TrailRenderer),
		typeof(LineRenderer),
		typeof(LocalVisualObject)
	};

	public static bool IsTransientComponent(Type type)
	{
		if (typeof(RectTransform).IsAssignableFrom(type))
		{
			return true;
		}
		Type[] array = transientTypes;
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i].IsAssignableFrom(type))
			{
				return true;
			}
		}
		return false;
	}

	public static bool IsTransient(Transform node)
	{
		if (node is RectTransform)
		{
			return true;
		}
		Type[] array = transientTypes;
		foreach (Type type in array)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)((Component)node).GetComponent(type)))
			{
				return true;
			}
		}
		return false;
	}
}
