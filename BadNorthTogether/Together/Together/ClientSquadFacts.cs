using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;

namespace Together;

internal static class ClientSquadFacts
{
	private struct TargetFact
	{
		public byte Kind;

		public int Id;

		public int Revision;

		public float Time;

		public bool HasProgress;

		public PathProgress Progress;
	}

	[HarmonyPatch(typeof(EnglishPatherSquad), "SetPatherTarget")]
	private static class TargetRevisionPatch
	{
		private static void Postfix(EnglishPatherSquad __instance)
		{
			if (!Session.IsHost || AgentAuthority.Epoch <= 0)
			{
				return;
			}
			Squad squad = ((SquadCoordinator)__instance).squad;
			EnglishSquad val = (EnglishSquad)(object)((squad is EnglishSquad) ? squad : null);
			if (global::UnityObjectOps.Truthy((Object)(object)val))
			{
				int num = TargetRevision(val);
				if (num == int.MaxValue)
				{
					AgentAuthority.Fail("导航目标修订耗尽");
				}
				else
				{
					sourceRevisions[val.hero.id] = num + 1;
				}
			}
		}
	}

	[HarmonyPatch(typeof(EnglishPatherVisualizer), "LateUpdate")]
	private static class ClientPathProgressPatch
	{
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d9: Expected O, but got Unknown
			//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fc: Expected O, but got Unknown
			//IL_010a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0110: Expected O, but got Unknown
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo getMethod = typeof(EnglishSquad).GetProperty("isSelected").GetGetMethod();
			MethodInfo getMethod2 = typeof(EnglishPatherVisualizer).GetProperty("enSquad", BindingFlags.Instance | BindingFlags.NonPublic).GetGetMethod(nonPublic: true);
			int num = -1;
			for (int i = 2; i < list.Count; i++)
			{
				if (CodeInstructionExtensions.Calls(list[i], getMethod) && CodeInstructionExtensions.Calls(list[i - 1], getMethod2) && list[i - 2].opcode == OpCodes.Ldarg_0)
				{
					num = i - 2;
					break;
				}
			}
			if (num < 0)
			{
				throw new InvalidOperationException("实际路径显示器缺少选中动画入口，不能安装进度补丁");
			}
			Label label = generator.DefineLabel();
			list[num].labels.Add(label);
			list.InsertRange(0, (IEnumerable<CodeInstruction>)(object)new CodeInstruction[3]
			{
				new CodeInstruction(OpCodes.Ldarg_0, (object)null),
				new CodeInstruction(OpCodes.Call, (object)typeof(ClientSquadFacts).GetMethod("UseClientPathProgress", BindingFlags.Static | BindingFlags.NonPublic)),
				new CodeInstruction(OpCodes.Brtrue, (object)label)
			});
			return list;
		}
	}

	private static readonly Dictionary<int, int> sourceRevisions = new Dictionary<int, int>();

	private static readonly Dictionary<int, int> appliedRevisions = new Dictionary<int, int>();

	private static readonly Dictionary<int, float> appliedTimes = new Dictionary<int, float>();

	private static readonly Dictionary<EnglishSquad, TargetFact> targets = new Dictionary<EnglishSquad, TargetFact>();

	private static readonly Dictionary<int, TargetFact> displayedTargets = new Dictionary<int, TargetFact>();

	private static readonly FieldInfo patherTarget = AccessTools.Field(typeof(EnglishPatherSquad), "_target");

	private static readonly FieldInfo navSpot = AccessTools.Field(typeof(NavSpotController), "_navSpot");

	private static readonly FieldInfo distance = AccessTools.Field(typeof(EnglishPatherVisualizer), "navDistance");

	private static readonly FieldInfo path = AccessTools.Field(typeof(EnglishPatherVisualizer), "path");

	private static readonly FieldInfo pathEnd = AccessTools.Field(typeof(EnglishPatherVisualizer), "pathEnd");

	internal static void CaptureTarget(Agent agent, out byte kind, out int id)
	{
		kind = byte.MaxValue;
		id = 0;
		Squad squad = agent.squad;
		EnglishSquad val = (EnglishSquad)(object)((squad is EnglishSquad) ? squad : null);
		if (!global::UnityObjectOps.Truthy((Object)(object)val) || (Object)(object)val.heroAgent != (Object)(object)agent || !global::UnityObjectOps.Truthy((Object)(object)val.pather))
		{
			return;
		}
		IPathTarget target = val.pather.target;
		if (target == null)
		{
			kind = 0;
			return;
		}
		NavSpot val2 = (NavSpot)(object)((target is NavSpot) ? target : null);
		if (global::UnityObjectOps.Truthy((Object)(object)val2))
		{
			kind = 1;
			id = val2.index;
			return;
		}
		SquadReplenishLocation val3 = (SquadReplenishLocation)(object)((target is SquadReplenishLocation) ? target : null);
		if (global::UnityObjectOps.Truthy((Object)(object)val3))
		{
			House[] houses = ((IslandComponent)((Squad)val).faction).island.village.houses;
			for (int i = 0; i < houses.Length; i++)
			{
				if ((Object)(object)((Component)houses[i]).GetComponent<SquadReplenishLocation>() == (Object)(object)val3)
				{
					kind = 2;
					id = i;
					return;
				}
			}
		}
		SquadEvacuationLocation val4 = (SquadEvacuationLocation)(object)((target is SquadEvacuationLocation) ? target : null);
		if (global::UnityObjectOps.Truthy((Object)(object)val4))
		{
			int num = WorldAuthority.ShipId(((Component)val4).GetComponentInParent<Longship>());
			if (num >= 0)
			{
				kind = 3;
				id = num;
				return;
			}
		}
		SceneVisuals.Warn("path-target", "未登记的路径显示目标类型：" + ((object)target).GetType().FullName);
	}

	internal static PathProgress CaptureProgress(Agent agent, byte kind)
	{
		Squad squad = agent.squad;
		EnglishSquad val = (EnglishSquad)(object)((squad is EnglishSquad) ? squad : null);
		if (kind == byte.MaxValue || kind == 0 || !global::UnityObjectOps.Truthy((Object)(object)val) || (Object)(object)val.heroAgent != (Object)(object)agent)
		{
			return default(PathProgress);
		}
		EnglishPatherVisualizer val2 = PathVisualizer(val);
		if (!global::UnityObjectOps.Truthy((Object)(object)val2))
		{
			SceneVisuals.Warn("path-binding", "路径显示器暂不可用，生命/位置继续同步");
			return default(PathProgress);
		}
		float value = (float)distance.GetValue(val2);
		PathProgress result = PathProgress.FromDistance(value);
		if (!result.Known)
		{
			SceneVisuals.Warn("path-progress-" + val.hero.id, "路径显示进度未知：英雄=" + val.hero.id + " 修订=" + TargetRevision(val) + " navDistance=" + value + "；继续同步人物事实，等待有效显示进度");
		}
		return result;
	}

	internal static void BeginFrame()
	{
		targets.Clear();
	}

	internal static void Reset()
	{
		targets.Clear();
		displayedTargets.Clear();
		sourceRevisions.Clear();
		appliedRevisions.Clear();
		appliedTimes.Clear();
	}

	internal static int TargetRevision(EnglishSquad squad)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)squad) || !sourceRevisions.TryGetValue(squad.hero.id, out var value))
		{
			return 0;
		}
		return value;
	}

	internal static void ApplyCommandOccupancy(EnglishSquad squad, int navId, float time, int revision, Island island)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)squad) || !((Squad)squad).alive || !CanApply(squad, revision, time))
		{
			return;
		}
		if (navId < 0)
		{
			if (!appliedRevisions.TryGetValue(squad.hero.id, out var value) || revision > value)
			{
				displayedTargets.Remove(squad.hero.id);
			}
			ClearOccupancy(squad);
			appliedRevisions[squad.hero.id] = revision;
			appliedTimes[squad.hero.id] = time;
		}
		else
		{
			ApplyTargetFact(squad, island, new TargetFact
			{
				Kind = 1,
				Id = navId,
				Time = time,
				Revision = revision
			});
		}
	}

	private static bool CanApply(EnglishSquad squad, int revision, float time)
	{
		if (!appliedRevisions.TryGetValue(squad.hero.id, out var value))
		{
			return true;
		}
		appliedTimes.TryGetValue(squad.hero.id, out var value2);
		return AcceptRevision(value, value2, revision, time);
	}

	internal static bool AcceptRevision(int previous, float previousTime, int incoming, float time)
	{
		if (incoming <= previous)
		{
			if (incoming == previous)
			{
				return time >= previousTime;
			}
			return false;
		}
		return true;
	}

	private static void ClearOccupancy(EnglishSquad squad)
	{
		NavSpotController navSpotOccupant = squad.navSpotOccupant;
		if (global::UnityObjectOps.Truthy((Object)(object)navSpotOccupant))
		{
			NavSpot val = navSpotOccupant.navSpot;
			if (global::UnityObjectOps.Truthy((Object)(object)val) && (Object)(object)val.occupant == (Object)(object)navSpotOccupant)
			{
				val.occupant = null;
			}
			navSpot.SetValue(navSpotOccupant, null);
		}
	}

	internal static void RecordTarget(EnglishSquad squad, byte kind, int id, float time, int revision, PathProgress progress)
	{
		if (global::UnityObjectOps.Truthy((Object)(object)squad) && kind != byte.MaxValue)
		{
			targets[squad] = new TargetFact
			{
				Kind = kind,
				Id = id,
				Time = time,
				Revision = revision,
				HasProgress = true,
				Progress = progress
			};
		}
	}

	internal static void ApplyTarget(EnglishSquad squad, Island island)
	{
		if (((Squad)squad).alive && global::UnityObjectOps.Truthy((Object)(object)squad.pather) && targets.TryGetValue(squad, out var value))
		{
			ApplyTargetFact(squad, island, value);
		}
	}

	private static void ApplyTargetFact(EnglishSquad squad, Island island, TargetFact fact)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)squad.pather) || !CanApply(squad, fact.Revision, fact.Time))
		{
			return;
		}
		IPathTarget val = null;
		if (fact.Kind == 1 && fact.Id < island.navSpotter.navSpots.Count)
		{
			val = (IPathTarget)(object)island.navSpotter.navSpots[fact.Id];
		}
		else if (fact.Kind == 2 && fact.Id < island.village.houses.Length)
		{
			val = (IPathTarget)(object)((Component)island.village.houses[fact.Id]).GetComponent<SquadReplenishLocation>();
		}
		else if (fact.Kind == 3)
		{
			Longship val2 = WorldAuthority.FindShip(fact.Id);
			if (global::UnityObjectOps.Truthy((Object)(object)val2))
			{
				val = (IPathTarget)(object)val2.evacuationLocation;
			}
		}
		if (fact.Kind != 0 && val == null)
		{
			displayedTargets.Remove(squad.hero.id);
			return;
		}
		bool flag = !appliedRevisions.TryGetValue(squad.hero.id, out var value) || fact.Revision > value;
		appliedRevisions[squad.hero.id] = fact.Revision;
		appliedTimes[squad.hero.id] = fact.Time;
		displayedTargets.TryGetValue(squad.hero.id, out var value2);
		displayedTargets[squad.hero.id] = MergeProgress(fact, value2);
		if (squad.pather.target == val && !flag)
		{
			return;
		}
		NavSpotController navSpotOccupant = squad.navSpotOccupant;
		if (global::UnityObjectOps.Truthy((Object)(object)navSpotOccupant))
		{
			NavSpot val3 = navSpotOccupant.navSpot;
			NavSpot val4 = (NavSpot)(object)((val is NavSpot) ? val : null);
			if (global::UnityObjectOps.Truthy((Object)(object)val3) && (Object)(object)val3.occupant == (Object)(object)navSpotOccupant)
			{
				val3.occupant = null;
			}
			navSpot.SetValue(navSpotOccupant, val4);
			if (global::UnityObjectOps.Truthy((Object)(object)val4))
			{
				val4.occupant = navSpotOccupant;
			}
		}
		patherTarget.SetValue(squad.pather, val);
		EnglishPatherVisualizer val5 = PathVisualizer(squad);
		if (global::UnityObjectOps.Truthy((Object)(object)val5) && val != null)
		{
			val5.OnNewTarget(val, true);
		}
		else if (global::UnityObjectOps.Truthy((Object)(object)val5))
		{
			val5.navSpot = null;
			distance.SetValue(val5, 0f);
		}
	}

	private static TargetFact MergeProgress(TargetFact incoming, TargetFact previous)
	{
		bool flag = incoming.Revision == previous.Revision && incoming.Kind == previous.Kind && incoming.Id == previous.Id;
		incoming.Progress = (incoming.HasProgress ? incoming.Progress.Canonical() : (flag ? previous.Progress.Canonical() : default(PathProgress)));
		incoming.HasProgress = true;
		return incoming;
	}

	private static bool UseClientPathProgress(EnglishPatherVisualizer visual)
	{
		if (!AgentAuthority.ClientPlayback)
		{
			return false;
		}
		Squad squad = ((SquadComponent)visual).squad;
		EnglishSquad val = (EnglishSquad)(object)((squad is EnglishSquad) ? squad : null);
		PathProgress pathProgress = ((global::UnityObjectOps.Truthy((Object)(object)val) && ((Squad)val).alive && displayedTargets.TryGetValue(val.hero.id, out var value)) ? value.Progress.Canonical() : default(PathProgress));
		distance.SetValue(visual, pathProgress.Known ? pathProgress.Distance : 0f);
		return true;
	}

	internal static void ClearIfDead(EnglishSquad squad)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)squad) || ((Squad)squad).alive)
		{
			return;
		}
		displayedTargets.Remove(squad.hero.id);
		NavSpotController navSpotOccupant = squad.navSpotOccupant;
		if (global::UnityObjectOps.Truthy((Object)(object)navSpotOccupant))
		{
			NavSpot val = navSpotOccupant.navSpot;
			if (global::UnityObjectOps.Truthy((Object)(object)val) && (Object)(object)val.occupant == (Object)(object)navSpotOccupant)
			{
				val.occupant = null;
			}
			navSpot.SetValue(navSpotOccupant, null);
		}
		EnglishPatherVisualizer val2 = PathVisualizer(squad);
		if (global::UnityObjectOps.Truthy((Object)(object)val2))
		{
			val2.navSpot = null;
			distance.SetValue(val2, 0f);
			object value = path.GetValue(val2);
			GameObject val3 = (GameObject)((value is GameObject) ? value : null);
			object value2 = pathEnd.GetValue(val2);
			GameObject val4 = (GameObject)((value2 is GameObject) ? value2 : null);
			if (global::UnityObjectOps.Truthy((Object)(object)val3))
			{
				val3.SetActive(false);
			}
			if (global::UnityObjectOps.Truthy((Object)(object)val4))
			{
				val4.SetActive(false);
			}
		}
		SquadSelector instance = Singleton<SquadSelector>.instance;
		if (global::UnityObjectOps.Truthy((Object)(object)instance) && (Object)(object)instance.selectedSquad == (Object)(object)squad)
		{
			instance.SelectSquad((EnglishSquad)null, false);
		}
	}

	private static EnglishPatherVisualizer PathVisualizer(EnglishSquad squad)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)squad) || !global::UnityObjectOps.Truthy((Object)(object)squad.pather))
		{
			return null;
		}
		return ((Component)squad.pather).GetComponent<EnglishPatherVisualizer>();
	}
}
