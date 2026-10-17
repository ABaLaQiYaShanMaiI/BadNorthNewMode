using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.SpriteMagic;

namespace Together;

internal static class SceneVisuals
{
	private sealed class SceneTree : IVisualTree<Transform>
	{
		public string Name(Transform node)
		{
			return ((Object)node).name;
		}

		public int ChildCount(Transform node)
		{
			return node.childCount;
		}

		public Transform Child(Transform node, int index)
		{
			return node.GetChild(index);
		}

		public bool HasRenderer(Transform node)
		{
			return global::UnityObjectOps.Truthy((Object)(object)((Component)node).GetComponent<Renderer>());
		}

		public bool IsLocalOnly(Transform node)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)((Component)node).GetComponent<Agent>()) || global::UnityObjectOps.Truthy((Object)(object)((Component)node).GetComponent<CorpseObject>()) || VisualScenePolicy.IsTransient(node))
			{
				return true;
			}
			if (global::UnityObjectOps.Truthy((Object)(object)((Component)node).GetComponent<MeshRenderer>()) && global::UnityObjectOps.Truthy((Object)(object)((Component)node).GetComponent<MeshFilter>()) && global::UnityObjectOps.Truthy((Object)(object)node.parent) && global::UnityObjectOps.Truthy((Object)(object)((Component)node.parent).GetComponent<BatchedSprite>()))
			{
				return !global::UnityObjectOps.Truthy((Object)(object)((Component)node).GetComponent<BatchedSprite>());
			}
			return false;
		}
	}

	public const byte Actor = 0;

	public const byte Island = 1;

	public const byte Ship = 2;

	private static Island island;

	private static readonly HashSet<string> warnings = new HashSet<string>(StringComparer.Ordinal);

	private static readonly SceneTree tree = new SceneTree();

	public static bool Capturing
	{
		get
		{
			if (Session.IsHost && Session.OptionalEnabled && !CampaignSync.ClosingBattle && global::UnityObjectOps.Truthy((Object)(object)island) && DeploySync.BattleStarted)
			{
				return !AgentAuthority.HasFailure;
			}
			return false;
		}
	}

	public static void BeginIsland(Island value)
	{
		Reset();
		island = value;
	}

	public static void Reset()
	{
		island = null;
		warnings.Clear();
	}

	public static void Warn(string key, string message)
	{
		if (warnings.Count < 128 && warnings.Add(key))
		{
			Plugin.Log.LogWarning((object)("[场景装饰] " + message));
		}
	}

	public static bool Retired(byte kind, int id)
	{
		return kind switch
		{
			2 => WorldAuthority.IsShipRetired(id), 
			0 => AgentAuthority.IsRetired(id), 
			_ => false, 
		};
	}

	public static NavigationMesh Navigation(byte kind, int id)
	{
		object obj;
		switch (kind)
		{
		case 1:
			if (!global::UnityObjectOps.Truthy((Object)(object)island))
			{
				return null;
			}
			return island.navMesh;
		default:
			obj = null;
			break;
		case 2:
			obj = WorldAuthority.FindShip(id);
			break;
		}
		Longship val = (Longship)obj;
		if (!global::UnityObjectOps.Truthy((Object)(object)val))
		{
			return null;
		}
		return val.navigationMesh;
	}

	public static Transform Root(byte kind, int id)
	{
		switch (kind)
		{
		case 0:
		{
			Agent val2 = AgentAuthority.FindAgent(id);
			if (!global::UnityObjectOps.Truthy((Object)(object)val2))
			{
				return null;
			}
			return ((Component)val2).transform;
		}
		case 1:
			if (!global::UnityObjectOps.Truthy((Object)(object)island))
			{
				return null;
			}
			return ((Component)island).transform;
		default:
		{
			Longship val = WorldAuthority.FindShip(id);
			if (!global::UnityObjectOps.Truthy((Object)(object)val))
			{
				return null;
			}
			return ((Component)val).transform;
		}
		}
	}

	public static bool DescribeWorld(Transform target, out byte kind, out int id, out Transform root)
	{
		kind = 1;
		id = 0;
		root = null;
		if (!global::UnityObjectOps.Truthy((Object)(object)island) || !global::UnityObjectOps.Truthy((Object)(object)target) || global::UnityObjectOps.Truthy((Object)(object)((Component)target).GetComponentInParent<Agent>()))
		{
			return false;
		}
		Longship componentInParent = ((Component)target).GetComponentInParent<Longship>();
		if (global::UnityObjectOps.Truthy((Object)(object)componentInParent))
		{
			kind = 2;
			id = WorldAuthority.ShipId(componentInParent);
			if (id < 0)
			{
				return false;
			}
			root = ((Component)componentInParent).transform;
			return true;
		}
		root = ((Component)island).transform;
		if (!((Object)(object)target == (Object)(object)root))
		{
			return target.IsChildOf(root);
		}
		return true;
	}

	public static bool Describe(Transform target, out byte kind, out int id, out string[] names, out ushort[] ordinals)
	{
		kind = 0;
		if (AgentAuthority.TryDescribeSurface(target, out id, out names, out ordinals))
		{
			return true;
		}
		if (DescribeWorld(target, out kind, out id, out var root))
		{
			return SceneVisualPath.Describe(root, target, (Transform node) => node.parent, tree, out names, out ordinals);
		}
		return false;
	}

	public static Transform Resolve(byte kind, int id, string[] names, ushort[] ordinals)
	{
		if (kind == 0)
		{
			return AgentAuthority.ResolveSurface(id, names, ordinals);
		}
		Transform val = Root(kind, id);
		if (!global::UnityObjectOps.Truthy((Object)(object)val))
		{
			return null;
		}
		return SceneVisualPath.Resolve(val, names, ordinals, tree);
	}
}
