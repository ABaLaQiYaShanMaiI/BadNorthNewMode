using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.Upgrades;

namespace Together;

internal static class AbilityModAdapters
{
	[HarmonyPatch]
	private static class ShieldFactsPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			if (types.TryGetValue("ShieldSpear.ShieldSpearAbility", out var type))
			{
				yield return type.GetMethod("NeedsHouseTrip", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				yield return type.GetMethod("AnyEquipped", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			}
		}

		private static bool Prefix(ActiveAbility __instance, MethodBase __originalMethod, ref bool __result)
		{
			if (!AbilityCommands.TryState(__instance, out var state))
			{
				return true;
			}
			__result = ((__originalMethod.Name == "NeedsHouseTrip") ? state.NeedsHouse : state.Equipped);
			return false;
		}
	}

	[HarmonyPatch]
	private static class RingExecutePatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			if (types.TryGetValue("GiantCommandRing.GiantCommandRingAbility", out var value))
			{
				yield return value.GetMethod("TryExecuteSummon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			}
		}

		private static bool Prefix(ActiveAbility __instance)
		{
			if (!AgentAuthority.ClientPlayback)
			{
				return true;
			}
			Type type = ((object)__instance).GetType();
			object value = type.GetField("selectedSourceSquad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(__instance);
			EnglishSquad val = (EnglishSquad)((value is EnglishSquad) ? value : null);
			object value2 = type.GetField("selectedLocation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(__instance);
			SquadReplenishLocation val2 = (SquadReplenishLocation)((value2 is SquadReplenishLocation) ? value2 : null);
			if (global::UnityObjectOps.Truthy((Object)(object)val) && global::UnityObjectOps.Truthy((Object)(object)val2))
			{
				AbilityCommands.Request(__instance, 5, AbilityCommands.HouseId(val2), val.hero.id);
			}
			return false;
		}
	}

	[HarmonyPatch(typeof(ActiveAbility), "get_joystickCursor")]
	private static class RingJoystickPatch
	{
		private static bool Prefix(ActiveAbility __instance, ref CursorManager.IJoystickCursor __result)
		{
			if ((!Session.Active && !AgentAuthority.ClientPlayback) || !IsRing(__instance))
			{
				return true;
			}
			if (!cursors.TryGetValue(__instance, out var value))
			{
				value = new RingCursor(__instance);
				cursors.Add(__instance, value);
			}
			__result = (CursorManager.IJoystickCursor)(object)value;
			return false;
		}
	}

	private sealed class RingCursor : CursorManager.IJoystickCursor, CursorManager.ICursor
	{
		private readonly ActiveAbility ability;

		private readonly TopDownWeighter weighter;

		private readonly List<Component> candidates;

		private Component target;

		private WorldSpaceCursorIcon icon;

		private bool active;

		private bool moved;

		private bool HasSource
		{
			get
			{
				if (global::UnityObjectOps.Truthy((Object)(object)ability))
				{
					object value = ((object)ability).GetType().GetField("selectedSourceSquad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(ability);
					return global::UnityObjectOps.Truthy((Object)((value is EnglishSquad) ? value : null));
				}
				return false;
			}
		}

		public RingCursor(ActiveAbility value)
		{
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0015: Expected O, but got Unknown
			weighter = new TopDownWeighter(70f, float.MaxValue);
			candidates = new List<Component>();
			ability = value;
		}

		public void SetActive(bool value)
		{
			active = value;
			if (!value)
			{
				target = null;
				if (global::UnityObjectOps.Truthy((Object)(object)icon))
				{
					icon.Deactivate();
				}
				icon = null;
			}
			else
			{
				moved = true;
				Refresh();
				SelectClosest();
			}
		}

		private void Refresh()
		{
			candidates.Clear();
			if (!global::UnityObjectOps.Truthy((Object)(object)ability) || !ability.IsAvailable())
			{
				return;
			}
			bool flag = AbilityCommands.TryState(ability, out var state);
			int[] array = ((!HasSource) ? (flag ? state.Sources : RingSources(ability)) : (flag ? state.Targets : HouseIds()));
			foreach (int num in array)
			{
				Component val = (Component)(HasSource ? ((object)AbilityCommands.FindHouse(num)) : ((object)AbilityCommands.FindSquad(num)));
				if (global::UnityObjectOps.Truthy((Object)(object)val))
				{
					candidates.Add(val);
				}
			}
			if (global::UnityObjectOps.Truthy((Object)(object)target) && !candidates.Contains(target))
			{
				target = null;
			}
		}

		private int[] HouseIds()
		{
			List<int> list = new List<int>();
			Island currentIsland = AbilityCommands.CurrentIsland;
			if (global::UnityObjectOps.Truthy((Object)(object)currentIsland))
			{
				for (int i = 0; i < currentIsland.village.houses.Length; i++)
				{
					SquadReplenishLocation val = AbilityCommands.FindHouse(i);
					if (global::UnityObjectOps.Truthy((Object)(object)val) && val.available)
					{
						list.Add(i);
					}
				}
			}
			return list.ToArray();
		}

		private Vector3 Position(Component candidate)
		{
			//IL_0049: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			//IL_0042: Unknown result type (might be due to invalid IL or missing references)
			SquadReplenishLocation val = (SquadReplenishLocation)(object)((candidate is SquadReplenishLocation) ? candidate : null);
			EnglishSquad val2 = (EnglishSquad)(object)((candidate is EnglishSquad) ? candidate : null);
			if (!global::UnityObjectOps.Truthy((Object)(object)val))
			{
				if (!global::UnityObjectOps.Truthy((Object)(object)val2) || !global::UnityObjectOps.Truthy((Object)(object)val2.heroAgent))
				{
					return candidate.transform.position;
				}
				return ((Component)val2.heroAgent).transform.position;
			}
			return val.GetWSCursorPos();
		}

		private void SetTarget(Component candidate)
		{
			//IL_009d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0086: Unknown result type (might be due to invalid IL or missing references)
			target = candidate;
			if (!global::UnityObjectOps.Truthy((Object)(object)target))
			{
				return;
			}
			if (!global::UnityObjectOps.Truthy((Object)(object)icon) && global::UnityObjectOps.Truthy((Object)(object)ability.wsCursorIcons.Target))
			{
				icon = ability.wsCursorIcons.Target.GetInstance(((UpgradeComponent)ability).squad);
			}
			if (global::UnityObjectOps.Truthy((Object)(object)icon))
			{
				if (icon.isNew)
				{
					icon.SetCurrentPos(Position(target));
				}
				icon.UpdateFloorPos(Position(target), false);
			}
		}

		private void SelectClosest()
		{
			//IL_0045: Unknown result type (might be due to invalid IL or missing references)
			//IL_005f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0064: Unknown result type (might be due to invalid IL or missing references)
			//IL_0069: Unknown result type (might be due to invalid IL or missing references)
			if (!global::UnityObjectOps.Truthy((Object)(object)ability) || !global::UnityObjectOps.Truthy((Object)(object)((UpgradeComponent)ability).squad.heroAgent))
			{
				return;
			}
			float num = float.MaxValue;
			Component val = null;
			foreach (Component candidate in candidates)
			{
				Vector3 val2 = Position(candidate) - ((Component)((UpgradeComponent)ability).squad.heroAgent).transform.position;
				float sqrMagnitude = val2.sqrMagnitude;
				if (sqrMagnitude < num)
				{
					num = sqrMagnitude;
					val = candidate;
				}
			}
			SetTarget(val);
		}

		public void SetMoveInput(Vector2 input)
		{
			//IL_006c: Unknown result type (might be due to invalid IL or missing references)
			//IL_006d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0072: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
			if (!active)
			{
				return;
			}
			Refresh();
			if (!global::UnityObjectOps.Truthy((Object)(object)target))
			{
				SelectClosest();
			}
			if (input.magnitude < 0.5f)
			{
				moved = false;
			}
			else
			{
				if (moved || !global::UnityObjectOps.Truthy((Object)(object)target))
				{
					return;
				}
				moved = true;
				if (!global::UnityObjectOps.Truthy((Object)(object)Singleton<LevelCamera>.instance))
				{
					return;
				}
				Vector3 topDownInputVector = CameraExtensions.GetTopDownInputVector(Singleton<LevelCamera>.instance.cameraRef, input);
				topDownInputVector.y = 0f;
				topDownInputVector.Normalize();
				float num = float.MinValue;
				Component val = null;
				foreach (Component candidate in candidates)
				{
					if (!((Object)(object)candidate == (Object)(object)target))
					{
						float weight = weighter.GetWeight(Position(target), Position(candidate), topDownInputVector);
						if (weight > num)
						{
							num = weight;
							val = candidate;
						}
					}
				}
				if (global::UnityObjectOps.Truthy((Object)(object)val))
				{
					SetTarget(val);
				}
			}
		}

		public void OnSelectButtonDown()
		{
			if (!active || !global::UnityObjectOps.Truthy((Object)(object)ability) || !ability.IsAvailable())
			{
				return;
			}
			Refresh();
			if (!global::UnityObjectOps.Truthy((Object)(object)target))
			{
				SelectClosest();
			}
			if (global::UnityObjectOps.Truthy((Object)(object)target))
			{
				Type type = ((object)ability).GetType();
				if (!HasSource)
				{
					FieldInfo field = type.GetField("selectedSourceSquad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					ActiveAbility obj = ability;
					Component obj2 = target;
					field.SetValue(obj, (obj2 is EnglishSquad) ? obj2 : null);
					target = null;
					Refresh();
					SelectClosest();
				}
				else
				{
					FieldInfo field2 = type.GetField("selectedLocation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					ActiveAbility obj3 = ability;
					Component obj4 = target;
					field2.SetValue(obj3, (obj4 is SquadReplenishLocation) ? obj4 : null);
					type.GetMethod("TryExecuteSummon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(ability, null);
				}
				type.GetMethod("UpdateTargets", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(ability, null);
			}
		}

		public void OnSelectButtonUp()
		{
		}
	}

	internal const string Shield = "ShieldSpear.ShieldSpearAbility";

	internal const string Guerrilla = "You_Ji_Dui.GuerrillaArcheryAbility";

	internal const string Sage = "SageTimeFreezeMod.SageTimeFreezeAbility";

	internal const string Ring = "GiantCommandRing.GiantCommandRingAbility";

	private static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();

	private static readonly Dictionary<ActiveAbility, RingCursor> cursors = new Dictionary<ActiveAbility, RingCursor>();

	private static readonly FieldInfo formationField = AccessTools.Field(typeof(PikeChargeAbility), "inFormation");

	private static readonly FieldInfo formationMembersField = AccessTools.Field(typeof(PikeChargeAbility), "compoonentsInFormation");

	private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	internal static void Initialize()
	{
		string[] array = new string[4] { "ShieldSpear.ShieldSpearAbility", "You_Ji_Dui.GuerrillaArcheryAbility", "SageTimeFreezeMod.SageTimeFreezeAbility", "GiantCommandRing.GiantCommandRingAbility" };
		foreach (string text in array)
		{
			Type type = AccessTools.TypeByName(text);
			if (type != null)
			{
				string text2 = CheckContract(type);
				if (text2 != null)
				{
					Plugin.Log.LogWarning((object)("[技能适配] 拒绝不匹配的 " + text + "，缺少 " + text2));
					continue;
				}
				types[text] = type;
				Plugin.Log.LogInfo((object)("[技能适配] 已接入 " + text + "，程序集=" + type.Assembly.GetName().Name));
			}
		}
	}

	internal static string CheckContract(Type type)
	{
		if (!typeof(ActiveAbility).IsAssignableFrom(type))
		{
			return "ActiveAbility 基类";
		}
		if (type.FullName == "GiantCommandRing.GiantCommandRingAbility")
		{
			string[] array = new string[3] { "selectedSourceSquad", "selectedLocation", "locations" };
			foreach (string text in array)
			{
				if (type.GetField(text, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null)
				{
					return text;
				}
			}
			array = new string[6] { "TryExecuteSummon", "IsSelectableSourceSquad", "ResolveSourcePrefab", "GetLocationFromScreenPos", "ResetSelectionOnly", "UpdateTargets" };
			foreach (string text2 in array)
			{
				if (type.GetMethod(text2, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null)
				{
					return text2;
				}
			}
		}
		else
		{
			if (!typeof(NavSpotTargetableAbility).IsAssignableFrom(type) || type.GetMethod("DoTargetedAction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null)
			{
				return "导航目标动作";
			}
			if (type.FullName == "ShieldSpear.ShieldSpearAbility")
			{
				if (type.GetField("switchOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null)
				{
					return "switchOn";
				}
				string[] array = new string[2] { "NeedsHouseTrip", "AnyEquipped" };
				foreach (string text3 in array)
				{
					if (type.GetMethod(text3, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null)
					{
						return text3;
					}
				}
			}
			if (type.FullName == "SageTimeFreezeMod.SageTimeFreezeAbility")
			{
				string[] array = new string[3] { "castsThisIsland", "tier2NoRegenActive", "tier3CrippledActive" };
				foreach (string text4 in array)
				{
					if (type.GetField(text4, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null)
					{
						return text4;
					}
				}
			}
		}
		return null;
	}

	internal static bool Supported(Type type)
	{
		if (types.TryGetValue(type.FullName, out var value))
		{
			return type == value;
		}
		return false;
	}

	internal static bool IsRing(ActiveAbility ability)
	{
		if (global::UnityObjectOps.Truthy((Object)(object)ability) && ((object)ability).GetType().FullName == "GiantCommandRing.GiantCommandRingAbility")
		{
			return Supported(((object)ability).GetType());
		}
		return false;
	}

	internal static IEnumerable<MethodBase> Methods(params string[] names)
	{
		List<Type> list = new List<Type>(typeof(ActiveAbility).Assembly.GetTypes());
		list.AddRange(types.Values);
		foreach (Type type in list)
		{
			if (!typeof(ActiveAbility).IsAssignableFrom(type) || type == typeof(JoystickMoveAbility))
			{
				continue;
			}
			foreach (string name in names)
			{
				MethodInfo method = type.GetMethod(name, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (method != null && !method.IsAbstract)
				{
					yield return method;
				}
			}
		}
	}

	internal static void RefreshPikeFormation(PikeChargeAbility ability, NavSpot origin)
	{
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0134: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0140: Unknown result type (might be due to invalid IL or missing references)
		//IL_019a: Unknown result type (might be due to invalid IL or missing references)
		//IL_019f: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a5: Unknown result type (might be due to invalid IL or missing references)
		List<PikeChargeComponent> list = (List<PikeChargeComponent>)formationMembersField.GetValue(ability);
		formationField.SetValue(ability, false);
		list?.Clear();
		Agent val = (global::UnityObjectOps.Truthy((Object)(object)((UpgradeComponent)ability).squad) ? ((UpgradeComponent)ability).squad.heroAgent : null);
		if (list == null || ability.components == null || !global::UnityObjectOps.Truthy((Object)(object)val) || val.aliveAndGrounded == null || !global::UnityObjectOps.Truthy((Object)(object)origin))
		{
			SceneVisuals.Warn("pike-formation-" + ((global::UnityObjectOps.Truthy((Object)(object)((UpgradeComponent)ability).squad) && ((UpgradeComponent)ability).squad.hero != null) ? ((UpgradeComponent)ability).squad.hero.id.ToString() : "unknown"), "长矛阵型暂不可用：英雄=" + global::UnityObjectOps.Truthy((Object)(object)val) + " 导航格=" + global::UnityObjectOps.Truthy((Object)(object)origin) + " 成员表=" + (ability.components != null) + "；本次仅标记技能不可用，继续同步");
		}
		else
		{
			if (!val.aliveAndGrounded.active)
			{
				return;
			}
			Vector3 position = ((Component)val).transform.position;
			Vector3 val2 = origin.bounds.ClosestPoint(position) - position;
			if (val2.sqrMagnitude >= 0.040000003f)
			{
				return;
			}
			foreach (PikeChargeComponent component in ability.components)
			{
				if (!global::UnityObjectOps.Truthy((Object)(object)component) || !global::UnityObjectOps.Truthy((Object)(object)((AgentComponent)component).agent))
				{
					continue;
				}
				if (global::UnityObjectOps.Truthy((Object)(object)component.spear))
				{
					val2 = ((Component)((AgentComponent)component).agent).transform.position - position;
					if (!(val2.sqrMagnitude < 1f))
					{
						continue;
					}
				}
				list.Add(component);
			}
			formationField.SetValue(ability, list.Count > 1 + ability.components.Count / 3);
		}
	}

	internal static void Capture(ActiveAbility ability, AbilityWire.State state)
	{
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		if (ability is PikeChargeAbility)
		{
			state.Formation = (bool)formationField.GetValue(ability);
		}
		if (ability is ShieldChargeAbility)
		{
			state.PlungeUsed = ((ShieldChargeAbility)ability).PlungeHasBeenUsed;
		}
		Type type = ((object)ability).GetType();
		if (type.FullName == "ShieldSpear.ShieldSpearAbility")
		{
			state.SwitchOn = (bool)type.GetField("switchOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(ability);
			state.NeedsHouse = (bool)type.GetMethod("NeedsHouseTrip", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(ability, null);
			state.Equipped = (bool)type.GetMethod("AnyEquipped", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(ability, null);
		}
		if (type.FullName == "SageTimeFreezeMod.SageTimeFreezeAbility")
		{
			state.Casts = (int)type.GetField("castsThisIsland", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(ability);
			state.NoRegen = (bool)type.GetField("tier2NoRegenActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(ability);
			state.Crippled = (bool)type.GetField("tier3CrippledActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(ability);
		}
	}

	internal static void ApplyFacts(ActiveAbility ability, AbilityWire.State state)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		if (ability is PikeChargeAbility)
		{
			formationField.SetValue(ability, state.Formation);
		}
		if (ability is ShieldChargeAbility)
		{
			((ShieldChargeAbility)ability).PlungeHasBeenUsed = state.PlungeUsed;
		}
		Type type = ((object)ability).GetType();
		if (type.FullName == "ShieldSpear.ShieldSpearAbility")
		{
			type.GetField("switchOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(ability, state.SwitchOn);
		}
		if (type.FullName == "SageTimeFreezeMod.SageTimeFreezeAbility")
		{
			type.GetField("castsThisIsland", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(ability, state.Casts);
			type.GetField("tier2NoRegenActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(ability, state.NoRegen);
			type.GetField("tier3CrippledActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(ability, state.Crippled);
		}
	}

	internal static int[] RingSources(ActiveAbility ability)
	{
		List<int> list = new List<int>();
		Island currentIsland = AbilityCommands.CurrentIsland;
		if (!global::UnityObjectOps.Truthy((Object)(object)currentIsland) || (Object)(object)currentIsland.english == (Object)null)
		{
			return list.ToArray();
		}
		MethodInfo method = ((object)ability).GetType().GetMethod("IsSelectableSourceSquad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (Squad livingSquad in currentIsland.english.livingSquads)
		{
			EnglishSquad val = (EnglishSquad)(object)((livingSquad is EnglishSquad) ? livingSquad : null);
			if (global::UnityObjectOps.Truthy((Object)(object)val) && ((Squad)val).alive && (bool)method.Invoke(ability, new object[1] { val }))
			{
				list.Add(val.hero.id);
			}
		}
		return list.ToArray();
	}

	internal static bool ExecuteRing(ActiveAbility ability, EnglishSquad source, SquadReplenishLocation location)
	{
		Type type = ((object)ability).GetType();
		if (global::UnityObjectOps.Truthy((Object)(object)source) && ((Squad)source).alive && global::UnityObjectOps.Truthy((Object)(object)location) && location.available && (bool)type.GetMethod("IsSelectableSourceSquad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(ability, new object[1] { source }))
		{
			object obj = type.GetMethod("ResolveSourcePrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(ability, new object[1] { source });
			if (global::UnityObjectOps.Truthy((Object)((obj is Agent) ? obj : null)))
			{
				FieldInfo field = type.GetField("selectedSourceSquad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				FieldInfo field2 = type.GetField("selectedLocation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				object value = field.GetValue(ability);
				object value2 = field2.GetValue(ability);
				try
				{
					field.SetValue(ability, source);
					field2.SetValue(ability, location);
					type.GetMethod("TryExecuteSummon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(ability, null);
					return true;
				}
				finally
				{
					field.SetValue(ability, value);
					field2.SetValue(ability, value2);
				}
			}
		}
		return false;
	}

	internal static void ClearSelection(ActiveAbility ability)
	{
		if (IsRing(ability))
		{
			((object)ability).GetType().GetMethod("ResetSelectionOnly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(ability, null);
		}
	}

	internal static void Reset()
	{
		foreach (RingCursor value in cursors.Values)
		{
			value.SetActive(value: false);
		}
		cursors.Clear();
	}
}
