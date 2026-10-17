using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using RTM.Utilities;
using Together.Net;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;
using Voxels.TowerDefense.UI;
using Voxels.TowerDefense.Upgrades;

namespace Together;

internal static class AbilityCommands
{
	private sealed class Pending
	{
		public string Key;

		public int Focus;

		public int Hero;

		public int Selection;

		public int Choice;

		public byte Action;
	}

	[HarmonyPatch(typeof(NavSpotTargetableAbility), "HandleScreenSpaceClick")]
	private static class NavClickPatch
	{
		private static bool Prefix(NavSpotTargetableAbility __instance, ref bool __result)
		{
			if (!AgentAuthority.ClientPlayback)
			{
				return true;
			}
			object value = hoverNavField.GetValue(__instance);
			TargetNavSpot val = (TargetNavSpot)((value is TargetNavSpot) ? value : null);
			__result = global::UnityObjectOps.Truthy((Object)(object)val) && global::UnityObjectOps.Truthy((Object)(object)val.navSpot);
			if (__result)
			{
				Request((ActiveAbility)(object)__instance, 1, val.navSpot.index);
			}
			else
			{
				Diagnose((ActiveAbility)(object)__instance, "鼠标未命中可选格", 0);
			}
			return false;
		}
	}

	[HarmonyPatch(typeof(NavSpotTargetableAbility), "OnJoystickSelect")]
	private static class NavJoystickPatch
	{
		private static bool Prefix(NavSpotTargetableAbility __instance, NavSpot navSpot)
		{
			if (AgentAuthority.ClientPlayback)
			{
				if (global::UnityObjectOps.Truthy((Object)(object)navSpot))
				{
					return Request((ActiveAbility)(object)__instance, 1, navSpot.index);
				}
				return false;
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(NavSpotTargetableAbility), "ShouldUpdateTargets")]
	private static class NavRefreshPatch
	{
		private static bool Prefix(NavSpotTargetableAbility __instance, ref bool __result)
		{
			if (!AgentAuthority.ClientPlayback)
			{
				return true;
			}
			bool flag = TryState((ActiveAbility)(object)__instance, out var state);
			if (flag)
			{
				InstallNavFacts(__instance, state);
			}
			__result = flag && ((ActiveAbility)__instance).focus != null && ((ActiveAbility)__instance).focus.active && dirtyTargets.Remove(state.Key);
			return false;
		}
	}

	[HarmonyPatch(typeof(NavSpotTargetableAbility), "UpdateTargets")]
	private static class NavTargetsPatch
	{
		private static void Prefix(NavSpotTargetableAbility __instance)
		{
			if (TryState((ActiveAbility)(object)__instance, out var state))
			{
				InstallNavFacts(__instance, state);
			}
		}
	}

	[HarmonyPatch(typeof(NavSpotTargetableAbility), "GetNotificationTerm")]
	private static class NavNotificationPatch
	{
		private static bool Prefix(NavSpotTargetableAbility __instance, ref string pn, ref string pv, ref string __result)
		{
			if (!AgentAuthority.ClientPlayback)
			{
				return true;
			}
			if (TryState((ActiveAbility)(object)__instance, out var state) && state.Origin >= 0)
			{
				return true;
			}
			pn = (pv = null);
			__result = "UPGRADES/COMMON/TOOLTIPS/NO_TARGETS";
			return false;
		}
	}

	[HarmonyPatch(typeof(PikeChargeAbility), "UpdateSquadSelected")]
	private static class PikeRefreshPatch
	{
		private static bool Prefix(PikeChargeAbility __instance)
		{
			if (!AgentAuthority.ClientPlayback)
			{
				return true;
			}
			if (TryState((ActiveAbility)(object)__instance, out var state))
			{
				AbilityModAdapters.ApplyFacts((ActiveAbility)(object)__instance, state);
				if (((ActiveAbility)__instance).focus.active && dirtyTargets.Remove(state.Key))
				{
					navUpdate.Invoke(__instance, null);
				}
			}
			return false;
		}
	}

	[HarmonyPatch(typeof(HouseTargetableAbility), "HandleScreenSpaceClick")]
	private static class HouseClickPatch
	{
		private static bool Prefix(HouseTargetableAbility __instance, Vector2 screenPos, ref bool __result)
		{
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			if (!AgentAuthority.ClientPlayback)
			{
				return true;
			}
			object obj = houseFromScreen.Invoke(__instance, new object[1] { screenPos });
			SquadReplenishLocation val = (SquadReplenishLocation)((obj is SquadReplenishLocation) ? obj : null);
			__result = global::UnityObjectOps.Truthy((Object)(object)val);
			if (global::UnityObjectOps.Truthy((Object)(object)val))
			{
				Request((ActiveAbility)(object)__instance, 2, HouseId(val));
			}
			else
			{
				Diagnose((ActiveAbility)(object)__instance, "鼠标未命中可选房屋", 0);
			}
			return false;
		}
	}

	[HarmonyPatch(typeof(ReplenishAbility), "SelectLocation")]
	private static class ReplenishPatch
	{
		private static bool Prefix(ReplenishAbility __instance, SquadReplenishLocation location)
		{
			return Request((ActiveAbility)(object)__instance, 2, HouseId(location));
		}
	}

	[HarmonyPatch(typeof(CallTroopsAbility), "SelectLocation")]
	private static class CallTroopsPatch
	{
		private static bool Prefix(CallTroopsAbility __instance, SquadReplenishLocation location)
		{
			return Request((ActiveAbility)(object)__instance, 2, HouseId(location));
		}
	}

	[HarmonyPatch(typeof(ReplenishAbility), "Cancel")]
	private static class ReplenishCancelPatch
	{
		private static void Prefix(ReplenishAbility __instance, out SquadReplenishLocation __state)
		{
			object obj;
			if (!Session.IsHost || !((ActiveAbility)__instance).isCancelable)
			{
				obj = null;
			}
			else
			{
				object value = houseLocationField.GetValue(__instance);
				obj = ((value is SquadReplenishLocation) ? value : null);
			}
			__state = (SquadReplenishLocation)obj;
		}

		private static void Postfix(ReplenishAbility __instance, SquadReplenishLocation __state)
		{
			if (Session.IsHost && global::UnityObjectOps.Truthy((Object)(object)__state) && !((Object)(object)__state.currentSquad != (Object)(object)((UpgradeComponent)__instance).squad) && !__state.squadExiting)
			{
				AccessTools.Method(typeof(SquadReplenishLocation), "StartExitHouse", (Type[])null, (Type[])null).Invoke(__state, new object[1] { 0f });
				nextState = 0f;
			}
		}
	}

	[HarmonyPatch(typeof(EvacuateAbility), "SelectLocation")]
	private static class EvacuatePatch
	{
		private static bool Prefix(EvacuateAbility __instance, SquadEvacuationLocation location)
		{
			object ship;
			if (!global::UnityObjectOps.Truthy((Object)(object)location))
			{
				ship = null;
			}
			else
			{
				object value = shipField.GetValue(location);
				ship = ((value is Longship) ? value : null);
			}
			return Request((ActiveAbility)(object)__instance, 3, WorldAuthority.ShipId((Longship)ship));
		}
	}

	[HarmonyPatch(typeof(SquadReplenishLocation), "get_available")]
	private static class HouseAvailablePatch
	{
		private static bool Prefix(SquadReplenishLocation __instance, ref bool __result)
		{
			if (!AgentAuthority.ClientPlayback || !houseFactsReady)
			{
				return true;
			}
			__result = availableHouses.Contains(HouseId(__instance));
			return false;
		}

		[HarmonyPriority(0)]
		private static void Postfix(SquadReplenishLocation __instance, ref bool __result)
		{
			if (AgentAuthority.ClientPlayback && houseFactsReady)
			{
				__result = availableHouses.Contains(HouseId(__instance));
			}
		}
	}

	[HarmonyPatch(typeof(EnglishSquad), "get_selectable")]
	private static class SelectablePatch
	{
		[HarmonyPriority(0)]
		private static void Postfix(EnglishSquad __instance, ref bool __result)
		{
			if (AgentAuthority.ClientPlayback && __instance.hero != null && selectableSquads.TryGetValue(__instance.hero.id, out var value))
			{
				__result = value && ((Squad)__instance).alive && !CampaignSync.ClosingBattle;
			}
		}
	}

	[HarmonyPatch(typeof(HouseTargetableAbility), "UpdateTargets")]
	private static class HouseTargetsPatch
	{
		private static bool Prefix(HouseTargetableAbility __instance)
		{
			if (!AgentAuthority.ClientPlayback)
			{
				return true;
			}
			bool flag = TryState((ActiveAbility)(object)__instance, out var state);
			foreach (SquadReplenishLocation location in __instance.locations)
			{
				if (global::UnityObjectOps.Truthy((Object)(object)location))
				{
					location.SetHighlight(flag && state.CanUse(__instance is ReplenishAbility && ReplenishAbility.InstantMode) && state.HasTarget(HouseId(location)));
				}
			}
			return false;
		}
	}

	[HarmonyPatch(typeof(EvacuateAbility), "UpdateTargets")]
	private static class EvacTargetsPatch
	{
		private static bool Prefix(EvacuateAbility __instance)
		{
			if (!AgentAuthority.ClientPlayback)
			{
				return true;
			}
			List<SquadEvacuationLocation> list = (List<SquadEvacuationLocation>)evacLocationsField.GetValue(__instance);
			foreach (SquadEvacuationLocation item in list)
			{
				if (global::UnityObjectOps.Truthy((Object)(object)item))
				{
					item.SetHighlight(false);
				}
			}
			list.Clear();
			if (TryState((ActiveAbility)(object)__instance, out var state))
			{
				int[] targets = state.Targets;
				for (int i = 0; i < targets.Length; i++)
				{
					Longship val = WorldAuthority.FindShip(targets[i]);
					if (global::UnityObjectOps.Truthy((Object)(object)val) && global::UnityObjectOps.Truthy((Object)(object)val.evacuationLocation))
					{
						list.Add(val.evacuationLocation);
						val.evacuationLocation.SetHighlight(((ActiveAbility)__instance).focus.active && state.Available);
					}
				}
			}
			return false;
		}
	}

	[HarmonyPatch(typeof(SquadEvacuationLocation), "IsAvailable")]
	private static class ShipAvailablePatch
	{
		private static bool Prefix(SquadEvacuationLocation __instance, ref bool __result)
		{
			if (!AgentAuthority.ClientPlayback)
			{
				return true;
			}
			MonoBehaviour currentConfirmMono = ConfirmButton.currentConfirmMono;
			ActiveAbilityButton val = (ActiveAbilityButton)(object)((currentConfirmMono is ActiveAbilityButton) ? currentConfirmMono : null);
			if (!global::UnityObjectOps.Truthy((Object)(object)val) || !(val.activeAbility is EvacuateAbility) || !TryState(val.activeAbility, out var state))
			{
				return true;
			}
			int num;
			if (state.Available)
			{
				AbilityWire.State state2 = state;
				object value = shipField.GetValue(__instance);
				num = (state2.HasTarget(WorldAuthority.ShipId((Longship)((value is Longship) ? value : null))) ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			__result = (byte)num != 0;
			return false;
		}

		[HarmonyPriority(0)]
		private static void Postfix(SquadEvacuationLocation __instance, ref bool __result)
		{
			Prefix(__instance, ref __result);
		}
	}

	[HarmonyPatch(typeof(ActiveAbilityButton), "OnGainedFocus")]
	private static class FocusPatch
	{
		private static bool Prefix(ActiveAbilityButton __instance)
		{
			if (!AgentAuthority.ClientPlayback || !global::UnityObjectOps.Truthy((Object)(object)__instance.activeAbility))
			{
				return true;
			}
			focusChoiceRevision++;
			if (__instance.activeAbility is JoystickMoveAbility)
			{
				return true;
			}
			focusRevision++;
			dirtyTargets.Add(Key(__instance.activeAbility));
			Diagnose(__instance.activeAbility, "按钮聚焦", 0);
			if (Supported(__instance.activeAbility))
			{
				ApplySquadUiFacts(((UpgradeComponent)__instance.activeAbility).squad);
				return true;
			}
			StatusText = "第三方技能未接入联机：" + ((object)__instance.activeAbility).GetType().Name;
			return false;
		}

		private static void Postfix(ActiveAbilityButton __instance)
		{
			if (AgentAuthority.ClientPlayback && global::UnityObjectOps.Truthy((Object)(object)__instance.activeAbility))
			{
				Diagnose(__instance.activeAbility, "目标刷新完成", 0);
			}
		}
	}

	[HarmonyPatch(typeof(ActiveAbilityButton), "OnLostFocus")]
	private static class LostFocusPatch
	{
		private static void Prefix(ActiveAbilityButton __instance)
		{
			if (AgentAuthority.ClientPlayback)
			{
				focusRevision++;
			}
			if (global::UnityObjectOps.Truthy((Object)(object)__instance.activeAbility))
			{
				AbilityModAdapters.ClearSelection(__instance.activeAbility);
			}
		}
	}

	[HarmonyPatch(typeof(ActiveAbilityButton), "OnConfirmed")]
	private static class ButtonConfirmPatch
	{
		private static bool Prefix(ActiveAbilityButton __instance)
		{
			ActiveAbility activeAbility = __instance.activeAbility;
			if (!AgentAuthority.ClientPlayback || !global::UnityObjectOps.Truthy((Object)(object)activeAbility) || activeAbility is JoystickMoveAbility)
			{
				return true;
			}
			if (activeAbility.isCancelable)
			{
				Request(activeAbility, 4, -1);
			}
			else if (!(activeAbility is TargetableAbility))
			{
				Request(activeAbility, 0, -1);
			}
			return false;
		}
	}

	[HarmonyPatch]
	private static class ConfirmPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			return AbilityModAdapters.Methods("OnConfirmed");
		}

		private static bool Prefix(ActiveAbility __instance)
		{
			return Request(__instance, 0, -1);
		}
	}

	[HarmonyPatch(typeof(ActiveAbility), "OnActivated")]
	private static class ActivationPatch
	{
		private static bool Prefix()
		{
			return !AgentAuthority.ClientPlayback;
		}

		private static void Postfix(ActiveAbility __instance)
		{
			if (Session.IsHost && Supported(__instance) && global::UnityObjectOps.Truthy((Object)(object)((UpgradeComponent)__instance).squad) && ((UpgradeComponent)__instance).squad.hero != null)
			{
				string key = Key(__instance);
				activations.TryGetValue(key, out var value);
				activations[key] = value + 1;
				nextState = 0f;
			}
		}
	}

	[HarmonyPatch(typeof(SquadSelector), "SelectSquad", new Type[]
	{
		typeof(EnglishSquad),
		typeof(bool)
	})]
	private static class PreserveHostSelectionPatch
	{
		private static bool Prefix(SquadSelector __instance, EnglishSquad newSelection, out EnglishSquad __state)
		{
			__state = __instance.selectedSquad;
			if (Session.IsHost && executing)
			{
				return global::UnityObjectOps.Truthy((Object)(object)newSelection);
			}
			return true;
		}

		private static void Postfix(SquadSelector __instance, EnglishSquad __state)
		{
			if (AgentAuthority.ClientPlayback && !((Object)(object)__state == (Object)(object)__instance.selectedSquad))
			{
				selectionRevision++;
				Plugin.Log.LogInfo((object)("[选中] 客户端英雄=" + (global::UnityObjectOps.Truthy((Object)(object)__instance.selectedSquad) ? __instance.selectedSquad.hero.id : (-1)) + " 修订=" + selectionRevision));
			}
		}
	}

	[HarmonyPatch]
	private static class CancelPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			return AbilityModAdapters.Methods("Cancel");
		}

		private static bool Prefix(ActiveAbility __instance)
		{
			return Request(__instance, 4, -1);
		}
	}

	[HarmonyPatch]
	private static class BlockLocalActionPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			return AbilityModAdapters.Methods("DoTargetedAction", "OnEffectEnded");
		}

		private static bool Prefix()
		{
			return !AgentAuthority.ClientPlayback;
		}
	}

	[HarmonyPatch]
	private static class BoolStatePatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			return AbilityModAdapters.Methods("get_isActive", "get_isCoolingDown", "get_idle", "IsBlocked", "IsAvailable", "CanDisplay", "get_blocksMove", "get_isCancelable");
		}

		private static bool Prefix(ActiveAbility __instance, MethodBase __originalMethod, ref bool __result)
		{
			if (!AgentAuthority.ClientPlayback || __instance is JoystickMoveAbility)
			{
				return true;
			}
			Apply(__instance, __originalMethod.Name, ref __result);
			return false;
		}

		[HarmonyPriority(0)]
		private static void Postfix(ActiveAbility __instance, MethodBase __originalMethod, ref bool __result)
		{
			if (AgentAuthority.ClientPlayback && !(__instance is JoystickMoveAbility))
			{
				Apply(__instance, __originalMethod.Name, ref __result);
			}
		}

		private static void Apply(ActiveAbility ability, string name, ref bool result)
		{
			bool flag = TryState(ability, out var state);
			switch (name)
			{
			case "get_isActive":
				result = flag && state.Active;
				break;
			case "get_isCoolingDown":
				result = flag && state.Cooling;
				break;
			case "get_idle":
				result = flag && !state.Active && !state.Cooling;
				break;
			case "get_blocksMove":
				result = flag && state.BlocksMove;
				break;
			case "get_isCancelable":
				result = flag && state.Cancelable;
				break;
			case "CanDisplay":
				result = flag && state.Display;
				break;
			case "IsAvailable":
				result = flag && state.CanUse(ability is ReplenishAbility && ReplenishAbility.InstantMode) && !CampaignSync.ClosingBattle;
				break;
			default:
				result = !flag || state.Blocked || CampaignSync.ClosingBattle;
				break;
			}
		}
	}

	[HarmonyPatch(typeof(ActiveAbility), "get_chargesRemaining")]
	private static class ChargesPatch
	{
		private static bool Prefix(ActiveAbility __instance, ref int __result)
		{
			if (!TryState(__instance, out var state))
			{
				return true;
			}
			__result = state.Charges;
			return false;
		}
	}

	[HarmonyPatch(typeof(ActiveAbility), "get_cooldownRemaining")]
	private static class CooldownPatch
	{
		private static bool Prefix(ActiveAbility __instance, ref float __result)
		{
			if (!TryState(__instance, out var state))
			{
				return true;
			}
			__result = Mathf.Max(0f, state.Remaining - Mathf.Max(0f, AgentAuthority.PlaybackTime - state.Time));
			return false;
		}
	}

	private static Island island;

	private static bool initialized;

	private static bool executing;

	private static bool houseFactsReady;

	private static float nextState;

	private static int requestId;

	private static int lastRequestId;

	private static int focusRevision;

	private static int selectionRevision;

	private static int focusChoiceRevision;

	private static readonly Dictionary<string, AbilityWire.State> states = new Dictionary<string, AbilityWire.State>();

	private static readonly Dictionary<string, AbilityWire.State> hostStates = new Dictionary<string, AbilityWire.State>();

	private static readonly Dictionary<string, ActiveAbility> bindings = new Dictionary<string, ActiveAbility>();

	private static readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();

	private static readonly Dictionary<string, int> activations = new Dictionary<string, int>();

	private static readonly HashSet<int> availableHouses = new HashSet<int>();

	private static readonly Dictionary<int, bool> selectableSquads = new Dictionary<int, bool>();

	private static readonly HashSet<string> dirtyTargets = new HashSet<string>();

	private static readonly FieldInfo stateActiveField = AccessTools.Field(typeof(AgentState), "_active");

	private static readonly FieldInfo targetCacheField = AccessTools.Field(typeof(NavSpotTargetableAbility), "targetCache");

	private static readonly FieldInfo heroNavField = AccessTools.Field(typeof(NavSpotTargetableAbility), "<heroNavSpot>k__BackingField");

	private static readonly FieldInfo hoverNavField = AccessTools.Field(typeof(NavSpotTargetableAbility), "hoverTarget");

	private static readonly FieldInfo candidatesField = AccessTools.Field(typeof(NavSpotTargetableAbility), "candidates");

	private static readonly FieldInfo currentAbilityField = AccessTools.Field(typeof(SquadUpgradeManager), "<currentAbility>k__BackingField");

	private static readonly FieldInfo houseLocationField = AccessTools.Field(typeof(HouseTargetableAbility), "location");

	private static readonly FieldInfo evacLocationsField = AccessTools.Field(typeof(EvacuateAbility), "locations");

	private static readonly FieldInfo shipField = AccessTools.Field(typeof(SquadEvacuationLocation), "ship");

	private static readonly MethodInfo activate = AccessTools.Method(typeof(ActiveAbility), "OnActivated", (Type[])null, (Type[])null);

	private static readonly MethodInfo houseFromScreen = AccessTools.Method(typeof(HouseTargetableAbility), "GetLocationFromScreenPos", (Type[])null, (Type[])null);

	private static readonly MethodInfo navUpdate = AccessTools.Method(typeof(NavSpotTargetableAbility), "UpdateTargets", (Type[])null, (Type[])null);

	public static string StatusText { get; private set; } = "技能由主机裁决";

	internal static Island CurrentIsland => island;

	internal static bool Executing => executing;

	private static bool Ready
	{
		get
		{
			if (Session.Active && global::UnityObjectOps.Truthy((Object)(object)island) && DeploySync.BattleStarted && !AgentAuthority.HasFailure && !CampaignSync.ClosingBattle && AgentAuthority.Epoch > 0)
			{
				return AgentAuthority.Epoch == CampaignSync.CurrentBattleEpoch;
			}
			return false;
		}
	}

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			AbilityModAdapters.Initialize();
			Session.On(MsgType.AbilityCommand, ReceiveCommand);
			Session.On(MsgType.AbilityState, ReceiveState);
			Session.Stopped += Reset;
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
		states.Clear();
		hostStates.Clear();
		bindings.Clear();
		pending.Clear();
		activations.Clear();
		availableHouses.Clear();
		selectableSquads.Clear();
		dirtyTargets.Clear();
		houseFactsReady = (executing = false);
		requestId = (lastRequestId = (focusRevision = (selectionRevision = (focusChoiceRevision = 0))));
		nextState = 0f;
		AbilityModAdapters.Reset();
		AbilityVisuals.Reset();
		StatusText = "技能由主机裁决";
	}

	private static List<ActiveAbility> Abilities()
	{
		List<ActiveAbility> list = new List<ActiveAbility>();
		bindings.Clear();
		if (!global::UnityObjectOps.Truthy((Object)(object)island) || (Object)(object)island.english == (Object)null)
		{
			return list;
		}
		foreach (Squad allSquad in island.english.allSquads)
		{
			EnglishSquad val = (EnglishSquad)(object)((allSquad is EnglishSquad) ? allSquad : null);
			if (!global::UnityObjectOps.Truthy((Object)(object)val) || val.hero == null)
			{
				continue;
			}
			ActiveAbility[] componentsInChildren = ((Component)val).GetComponentsInChildren<ActiveAbility>(true);
			foreach (ActiveAbility val2 in componentsInChildren)
			{
				if (Supported(val2) && val2.focus != null && val2.active != null)
				{
					if (list.Count >= 512)
					{
						throw new IOException("技能数量超出联机预算");
					}
					list.Add(val2);
					bindings[Key(val2)] = val2;
				}
			}
		}
		return list;
	}

	private static int Ordinal(ActiveAbility ability)
	{
		int num = 0;
		ActiveAbility[] componentsInChildren = ((Component)((UpgradeComponent)ability).squad).GetComponentsInChildren<ActiveAbility>(true);
		foreach (ActiveAbility val in componentsInChildren)
		{
			if (((object)val).GetType() == ((object)ability).GetType())
			{
				if ((Object)(object)val == (Object)(object)ability)
				{
					return num;
				}
				num++;
			}
		}
		return -1;
	}

	internal static string Key(ActiveAbility ability)
	{
		return ((UpgradeComponent)ability).squad.hero.id + ":" + ((object)ability).GetType().FullName + ":" + Ordinal(ability);
	}

	private static ActiveAbility Find(int hero, string type, int ordinal)
	{
		bindings.TryGetValue(hero + ":" + type + ":" + ordinal, out var value);
		return value;
	}

	internal static bool Supported(ActiveAbility ability)
	{
		if (global::UnityObjectOps.Truthy((Object)(object)ability) && !(ability is JoystickMoveAbility))
		{
			if (((object)ability).GetType().Assembly != typeof(ActiveAbility).Assembly)
			{
				return AbilityModAdapters.Supported(((object)ability).GetType());
			}
			return true;
		}
		return false;
	}

	internal static bool TryState(ActiveAbility ability, out AbilityWire.State state)
	{
		state = null;
		if (AgentAuthority.ClientPlayback && global::UnityObjectOps.Truthy((Object)(object)ability) && global::UnityObjectOps.Truthy((Object)(object)((UpgradeComponent)ability).squad))
		{
			return states.TryGetValue(Key(ability), out state);
		}
		return false;
	}

	internal static EnglishSquad FindSquad(int hero)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)island) || (Object)(object)island.english == (Object)null)
		{
			return null;
		}
		foreach (Squad allSquad in island.english.allSquads)
		{
			EnglishSquad val = (EnglishSquad)(object)((allSquad is EnglishSquad) ? allSquad : null);
			if (global::UnityObjectOps.Truthy((Object)(object)val) && val.hero != null && val.hero.id == hero)
			{
				return val;
			}
		}
		return null;
	}

	internal static int HouseId(SquadReplenishLocation location)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)location) || !global::UnityObjectOps.Truthy((Object)(object)island))
		{
			return -1;
		}
		return Array.IndexOf(island.village.houses, ((Component)location).GetComponent<House>());
	}

	internal static SquadReplenishLocation FindHouse(int id)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)island) || id < 0 || id >= island.village.houses.Length || !global::UnityObjectOps.Truthy((Object)(object)island.village.houses[id]))
		{
			return null;
		}
		return ((Component)island.village.houses[id]).GetComponent<SquadReplenishLocation>();
	}

	internal static bool Request(ActiveAbility ability, byte action, int target, int source = -1)
	{
		if (!AgentAuthority.ClientPlayback)
		{
			return true;
		}
		if (executing || Session.ApplyingRemote)
		{
			return false;
		}
		if (!Ready || !global::UnityObjectOps.Truthy((Object)(object)ability) || !global::UnityObjectOps.Truthy((Object)(object)((UpgradeComponent)ability).squad) || !((Squad)((UpgradeComponent)ability).squad).alive || !((UpgradeComponent)ability).squad.selectable || !Supported(ability))
		{
			StatusText = "战斗或技能未就绪，已阻止本地执行";
			Diagnose(ability, "拒绝输入", 0);
			return false;
		}
		if (!TryState(ability, out var state) || ((action == 4) ? (!state.Cancelable) : (!state.CanUse(ability is ReplenishAbility && ReplenishAbility.InstantMode))))
		{
			StatusText = "主机条件尚未就绪或技能当前不可用";
			Diagnose(ability, "条件阻塞", 0);
			return false;
		}
		foreach (Pending value in pending.Values)
		{
			if (value.Key == state.Key)
			{
				return false;
			}
		}
		if (pending.Count >= 64)
		{
			StatusText = "技能请求等待主机回复";
			return false;
		}
		AbilityWire.Command command = new AbilityWire.Command
		{
			Epoch = AgentAuthority.Epoch,
			Time = AgentAuthority.HostTime,
			Request = ++requestId,
			Hero = ((UpgradeComponent)ability).squad.hero.id,
			Type = ((object)ability).GetType().FullName,
			Ordinal = Ordinal(ability),
			Action = action,
			Target = target,
			Source = source,
			Instant = (ability is ReplenishAbility && ReplenishAbility.InstantMode)
		};
		using (MessageWriter messageWriter = Protocol.Begin(MsgType.AbilityCommand))
		{
			command.Write(messageWriter);
			if (!Session.Send(messageWriter.ToArray()))
			{
				AgentAuthority.Fail("技能请求发送失败");
				return false;
			}
		}
		pending.Add(command.Request, new Pending
		{
			Key = state.Key,
			Focus = focusRevision,
			Hero = command.Hero,
			Selection = selectionRevision,
			Choice = focusChoiceRevision,
			Action = action
		});
		Diagnose(ability, "提交 action=" + action + " target=" + target + " source=" + source + " instant=" + command.Instant, command.Request);
		StatusText = "已向主机提交技能请求 #" + command.Request;
		return false;
	}

	private static void ReceiveCommand(BinaryReader reader)
	{
		//IL_02bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c6: Expected O, but got Unknown
		//IL_0259: Unknown result type (might be due to invalid IL or missing references)
		AbilityWire.Command command = AbilityWire.Command.Read(reader);
		reader.RequireEnd();
		if (!Session.IsHost || !Ready || command.Epoch != AgentAuthority.Epoch || command.Request <= lastRequestId)
		{
			return;
		}
		lastRequestId = command.Request;
		Abilities();
		ActiveAbility val = Find(command.Hero, command.Type, command.Ordinal);
		if (!global::UnityObjectOps.Truthy((Object)(object)val) || !((Squad)((UpgradeComponent)val).squad).alive || !((UpgradeComponent)val).squad.selectable || !Supported(val))
		{
			Reply(command.Request, accepted: false, "找不到可用的英雄或技能");
			return;
		}
		if (command.Instant && !(val is ReplenishAbility))
		{
			Reply(command.Request, accepted: false, "即刻模式只允许补充能力");
			return;
		}
		bool instantMode = ReplenishAbility.InstantMode;
		bool applyingRemote = Session.ApplyingRemote;
		executing = true;
		Session.ApplyingRemote = true;
		if (val is ReplenishAbility)
		{
			ReplenishAbility.InstantMode = command.Instant;
		}
		try
		{
			AbilityWire.State state = Capture(val);
			if (command.Action == 4)
			{
				if (!val.isCancelable)
				{
					Reply(command.Request, accepted: false, "技能当前不可取消");
					return;
				}
				val.Cancel();
			}
			else
			{
				if (!state.CanUse(command.Instant))
				{
					Reply(command.Request, accepted: false, "冷却、次数、阵型或小队状态不允许执行");
					return;
				}
				if (command.Action == 0 && !(val is TargetableAbility))
				{
					val.OnConfirmed();
				}
				else if (command.Action == 1 && val is NavSpotTargetableAbility)
				{
					if (!state.HasTarget(command.Target))
					{
						Reply(command.Request, accepted: false, "导航目标不在主机可用范围内");
						return;
					}
					AccessTools.Method(((object)val).GetType(), "DoTargetedAction", (Type[])null, (Type[])null).Invoke(val, new object[2]
					{
						((UpgradeComponent)val).squad.GetHeroNavSpot(),
						island.navSpotter.navSpots[command.Target]
					});
					activate.Invoke(val, null);
				}
				else if (command.Action == 2 && val is HouseTargetableAbility)
				{
					SquadReplenishLocation val2 = FindHouse(command.Target);
					if (!global::UnityObjectOps.Truthy((Object)(object)val2) || !state.HasTarget(command.Target) || !val2.available || !val.IsAvailable())
					{
						Reply(command.Request, accepted: false, "房屋被占用、失火或补充条件不允许执行");
						return;
					}
					((HouseTargetableAbility)val).SelectLocation(val2);
				}
				else if (command.Action == 3 && val is EvacuateAbility)
				{
					Longship val3 = WorldAuthority.FindShip(command.Target);
					if (!global::UnityObjectOps.Truthy((Object)(object)val3) || !state.HasTarget(command.Target) || !global::UnityObjectOps.Truthy((Object)(object)val3.evacuationLocation) || !val3.evacuationLocation.IsAvailable(SquadArea((EvacuateAbility)val)))
					{
						Reply(command.Request, accepted: false, "撤退船不在本岛可用列表中或容量不足");
						return;
					}
					AccessTools.Method(typeof(EvacuateAbility), "SelectLocation", (Type[])null, (Type[])null).Invoke(val, new object[1] { val3.evacuationLocation });
				}
				else
				{
					if (command.Action != 5 || !AbilityModAdapters.IsRing(val))
					{
						Reply(command.Request, accepted: false, "技能与目标类型不匹配");
						return;
					}
					if (!state.HasTarget(command.Target) || Array.IndexOf(state.Sources, command.Source) < 0 || !AbilityModAdapters.ExecuteRing(val, FindSquad(command.Source), FindHouse(command.Target)))
					{
						Reply(command.Request, accepted: false, "指挥戒指的模板队伍或出兵房屋不可用");
						return;
					}
				}
			}
			Reply(command.Request, accepted: true, "主机已执行");
			nextState = 0f;
			Plugin.Log.LogInfo((object)("[技能] 主机裁决 epoch=" + command.Epoch + " request=" + command.Request + " hero=" + command.Hero + " type=" + command.Type + " action=" + command.Action + " target=" + command.Target + " source=" + command.Source + " instant=" + command.Instant));
		}
		catch (Exception ex)
		{
			AgentAuthority.Fail("主机技能执行失败：" + ex.GetBaseException().Message);
		}
		finally
		{
			ReplenishAbility.InstantMode = instantMode;
			executing = false;
			Session.ApplyingRemote = applyingRemote;
		}
	}

	private static void Reply(int id, bool accepted, string reason)
	{
		Plugin.Log.LogInfo((object)("[技能] 回复 request=" + id + " accepted=" + accepted + " reason=" + reason));
		using MessageWriter messageWriter = Protocol.Begin(MsgType.AbilityState);
		messageWriter.Write((byte)2);
		messageWriter.Write(AgentAuthority.Epoch);
		messageWriter.Write(AgentAuthority.HostTime);
		messageWriter.Write(-1);
		messageWriter.Write(id);
		messageWriter.Write(accepted);
		messageWriter.Write(reason);
		if (!Session.Send(messageWriter.ToArray()))
		{
			AgentAuthority.Fail("技能裁决回复发送失败");
		}
	}

	private static float SquadArea(EvacuateAbility ability)
	{
		return (float)AccessTools.Method(typeof(EvacuateAbility), "GetSquadArea", (Type[])null, (Type[])null).Invoke(ability, null);
	}

	private static int[] HouseTargets()
	{
		List<int> list = new List<int>();
		for (int i = 0; i < island.village.houses.Length; i++)
		{
			SquadReplenishLocation val = FindHouse(i);
			if (global::UnityObjectOps.Truthy((Object)(object)val) && val.available)
			{
				list.Add(i);
			}
		}
		return list.ToArray();
	}

	private static AbilityWire.State Capture(ActiveAbility ability)
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Expected O, but got Unknown
		//IL_0392: Unknown result type (might be due to invalid IL or missing references)
		//IL_0399: Expected O, but got Unknown
		//IL_039c: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a6: Expected I4, but got Unknown
		NavSpotTargetableAbility val = (NavSpotTargetableAbility)(object)((ability is NavSpotTargetableAbility) ? ability : null);
		NavSpot val2 = (global::UnityObjectOps.Truthy((Object)(object)val) ? ((UpgradeComponent)ability).squad.GetHeroNavSpot() : null);
		if (global::UnityObjectOps.Truthy((Object)(object)val))
		{
			heroNavField.SetValue(val, val2);
		}
		if (ability is PikeChargeAbility)
		{
			AbilityModAdapters.RefreshPikeFormation((PikeChargeAbility)ability, val2);
		}
		AbilityWire.State state = new AbilityWire.State
		{
			Hero = ((UpgradeComponent)ability).squad.hero.id,
			Type = ((object)ability).GetType().FullName,
			Ordinal = Ordinal(ability),
			Charges = ability.chargesRemaining,
			Active = ability.isActive,
			Cooling = ability.isCoolingDown,
			Blocked = (ability.IsBlocked() || !((Squad)((UpgradeComponent)ability).squad).alive || !((UpgradeComponent)ability).squad.selectable),
			BlocksMove = ability.blocksMove,
			Cancelable = ability.isCancelable,
			Selectable = ((UpgradeComponent)ability).squad.selectable,
			Display = ability.CanDisplay(),
			Remaining = ability.cooldownRemaining,
			Period = ability.cooldownPeriod,
			EffectEnd = AgentAuthority.HostTime + Mathf.Max(0f, ability.timeWhenEffectWearsOff - Time.time),
			MaxCount = ((UpgradeComponent)ability).squad.maxCount,
			Current = ((Object)(object)((UpgradeComponent)ability).squad.upgradeManager.currentAbility == (Object)(object)ability),
			Time = AgentAuthority.HostTime
		};
		activations.TryGetValue(state.Key, out var value);
		state.Activations = value;
		if (hostStates.TryGetValue(state.Key, out var value2))
		{
			state.Cooldowns = value2.Cooldowns + ((value2.Cooling && !state.Cooling) ? 1 : 0);
		}
		if (global::UnityObjectOps.Truthy((Object)(object)val))
		{
			state.Origin = (global::UnityObjectOps.Truthy((Object)(object)val2) ? val2.index : (-1));
			List<int> list = new List<int>();
			int num = -1;
			if (global::UnityObjectOps.Truthy((Object)(object)val2) && val.targeter != null)
			{
				if (island.navSpotter.navSpots.Count > 192)
				{
					throw new IOException("原生技能目标超过 BitMask192 容量");
				}
				for (int i = 0; i < island.navSpotter.navSpots.Count; i++)
				{
					int val3 = num;
					if (val.targeter.IsTargetable(val2, island.navSpotter.navSpots[i], ref val3))
					{
						list.Add(i);
					}
					num = Math.Max(num, val3);
				}
			}
			state.Targets = list.ToArray();
			state.Error = ((list.Count > 0) ? string.Empty : ((val.targeter != null && num >= 0) ? val.targeter.GetErrorTerm(num) : "UPGRADES/COMMON/TOOLTIPS/NO_TARGETS"));
			InstallNavFacts(val, state);
			state.Available = !state.Blocked && global::UnityObjectOps.Truthy((Object)(object)val2) && list.Count > 0 && AvailableWithoutFocus(ability);
		}
		else if (ability is HouseTargetableAbility)
		{
			state.Targets = HouseTargets();
			object value3 = houseLocationField.GetValue(ability);
			state.Location = HouseId((SquadReplenishLocation)((value3 is SquadReplenishLocation) ? value3 : null));
			bool instantMode = ReplenishAbility.InstantMode;
			try
			{
				ReplenishAbility.InstantMode = false;
				state.Available = !state.Blocked && AvailableWithoutFocus(ability);
				ReplenishAbility.InstantMode = true;
				state.InstantAvailable = !state.Blocked && AvailableWithoutFocus(ability);
			}
			finally
			{
				ReplenishAbility.InstantMode = instantMode;
			}
		}
		else if (ability is EvacuateAbility)
		{
			EvacuateAbility val4 = (EvacuateAbility)ability;
			state.EvacState = (int)val4.state;
			List<int> list2 = new List<int>();
			List<SquadEvacuationLocation> list3 = new List<SquadEvacuationLocation>();
			float num2 = SquadArea(val4);
			if (AccessTools.Field(typeof(EvacuateAbility), "landings").GetValue(val4) is Landing[] array)
			{
				Landing[] array2 = array;
				foreach (Landing val5 in array2)
				{
					Longship val6 = (global::UnityObjectOps.Truthy((Object)(object)val5) ? val5.spawnedShip : null);
					if (global::UnityObjectOps.Truthy((Object)(object)val6) && global::UnityObjectOps.Truthy((Object)(object)val6.evacuationLocation) && val6.evacuationLocation.IsAvailable(num2))
					{
						int num3 = WorldAuthority.ShipId(val6);
						if (num3 >= 0 && !list2.Contains(num3))
						{
							list2.Add(num3);
							list3.Add(val6.evacuationLocation);
						}
					}
				}
			}
			state.Targets = list2.ToArray();
			object value4 = evacLocationsField.GetValue(ability);
			try
			{
				evacLocationsField.SetValue(ability, list3);
				state.Available = !state.Blocked && list2.Count > 0 && AvailableWithoutFocus(ability);
			}
			finally
			{
				evacLocationsField.SetValue(ability, value4);
			}
		}
		else if (AbilityModAdapters.IsRing(ability))
		{
			state.Targets = HouseTargets();
			state.Sources = AbilityModAdapters.RingSources(ability);
			state.Available = !state.Blocked && state.Targets.Length != 0 && state.Sources.Length != 0 && AvailableWithoutFocus(ability);
		}
		else
		{
			state.Available = !state.Blocked && ability.IsAvailable();
		}
		if (!(ability is HouseTargetableAbility))
		{
			state.InstantAvailable = state.Available;
		}
		AbilityModAdapters.Capture(ability, state);
		return state;
	}

	private static bool AvailableWithoutFocus(ActiveAbility ability)
	{
		bool active = ability.focus.active;
		stateActiveField.SetValue(ability.focus, false);
		try
		{
			return ability.IsAvailable();
		}
		finally
		{
			stateActiveField.SetValue(ability.focus, active);
		}
	}

	public static void Tick()
	{
		if (!Session.IsHost || !Ready || Time.unscaledTime < nextState)
		{
			return;
		}
		nextState = Time.unscaledTime + 0.2f;
		List<ActiveAbility> list = Abilities();
		using MessageWriter messageWriter = Protocol.Begin(MsgType.AbilityState);
		messageWriter.Write((byte)2);
		messageWriter.Write(AgentAuthority.Epoch);
		messageWriter.Write(AgentAuthority.HostTime);
		messageWriter.Write(list.Count);
		foreach (ActiveAbility item in list)
		{
			AbilityWire.State state = Capture(item);
			hostStates[state.Key] = state;
			state.Write(messageWriter);
		}
		if (!Session.Send(messageWriter.ToArray()))
		{
			AgentAuthority.Fail("技能事实发送失败");
		}
	}

	private static void ReceiveState(BinaryReader reader)
	{
		byte b = reader.ReadByte();
		int num = reader.ReadInt32();
		float num2 = reader.ReadSingle();
		AbilityWire.Finite(num2);
		if (!Session.IsClient || !global::UnityObjectOps.Truthy((Object)(object)island) || b != 2 || num != AgentAuthority.Epoch || num != CampaignSync.CurrentBattleEpoch || CampaignSync.ClosingBattle)
		{
			return;
		}
		int num3 = reader.ReadInt32();
		if (num3 == -1)
		{
			int key = reader.ReadInt32();
			bool flag = reader.ReadBoolean();
			string text = reader.ReadString();
			reader.RequireEnd();
			if (text.Length > 512)
			{
				throw new IOException("技能回复过长");
			}
			if (pending.TryGetValue(key, out var value))
			{
				pending.Remove(key);
				StatusText = (flag ? "主机确认：" : "主机拒绝：") + text;
				Plugin.Log.LogInfo((object)("[技能] 收到裁决 epoch=" + num + " request=" + key + " accepted=" + flag + " reason=" + text));
				MonoBehaviour currentConfirmMono = ConfirmButton.currentConfirmMono;
				ActiveAbilityButton val = (ActiveAbilityButton)(object)((currentConfirmMono is ActiveAbilityButton) ? currentConfirmMono : null);
				bool flag2 = global::UnityObjectOps.Truthy((Object)(object)val) && global::UnityObjectOps.Truthy((Object)(object)val.activeAbility) && AbilityWire.MatchesFocus(value.Key, value.Focus, Key(val.activeAbility), focusRevision);
				SquadSelector instance = Singleton<SquadSelector>.instance;
				EnglishSquad val2 = (global::UnityObjectOps.Truthy((Object)(object)instance) ? instance.selectedSquad : null);
				bool num4 = flag && (!global::UnityObjectOps.Truthy((Object)(object)ConfirmButton.currentConfirmMono) | flag2) && global::UnityObjectOps.Truthy((Object)(object)val2) && AbilityWire.CompletesSelection(value.Action, value.Hero, value.Selection, value.Choice, val2.hero.id, selectionRevision, focusChoiceRevision);
				if (flag & flag2)
				{
					ConfirmButton.SetCurrent((MonoBehaviour)null);
				}
				if (num4)
				{
					instance.SelectSquad((EnglishSquad)null, false);
					Plugin.Log.LogInfo((object)("[技能输入] 成功收尾 request=" + key + " hero=" + value.Hero + "，已取消本机队伍选中"));
				}
			}
			return;
		}
		if (num3 < 0 || num3 > 512)
		{
			throw new IOException("技能快照数量异常");
		}
		List<AbilityWire.State> list = new List<AbilityWire.State>();
		HashSet<string> hashSet = new HashSet<string>();
		for (int i = 0; i < num3; i++)
		{
			AbilityWire.State state = AbilityWire.State.Read(reader, num2);
			if (!hashSet.Add(state.Key))
			{
				throw new IOException("技能快照身份重复");
			}
			list.Add(state);
		}
		reader.RequireEnd();
		Abilities();
		foreach (string item2 in new List<string>(states.Keys))
		{
			if (!hashSet.Contains(item2))
			{
				states.Remove(item2);
			}
		}
		foreach (AbilityWire.State item3 in list)
		{
			if (states.TryGetValue(item3.Key, out var value2) && item3.Time < value2.Time)
			{
				continue;
			}
			if (value2 == null || value2.Origin != item3.Origin || value2.Blocked != item3.Blocked || value2.Available != item3.Available || !SameTargets(value2.Targets, item3.Targets))
			{
				dirtyTargets.Add(item3.Key);
			}
			states[item3.Key] = item3;
			selectableSquads[item3.Hero] = item3.Selectable;
			ActiveAbility val3 = Find(item3.Hero, item3.Type, item3.Ordinal);
			if (!global::UnityObjectOps.Truthy((Object)(object)val3))
			{
				continue;
			}
			if (val3 is HouseTargetableAbility || AbilityModAdapters.IsRing(val3))
			{
				availableHouses.Clear();
				int[] targets = item3.Targets;
				foreach (int item in targets)
				{
					availableHouses.Add(item);
				}
				houseFactsReady = true;
			}
			AbilityVisuals.Enqueue(val3, item3);
		}
		foreach (Squad allSquad in island.english.allSquads)
		{
			EnglishSquad val4 = (EnglishSquad)(object)((allSquad is EnglishSquad) ? allSquad : null);
			if (global::UnityObjectOps.Truthy((Object)(object)val4))
			{
				ApplySquadUiFacts(val4);
			}
		}
	}

	public static void ApplySquadUiFacts(EnglishSquad squad)
	{
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Expected O, but got Unknown
		if (!AgentAuthority.ClientPlayback || !global::UnityObjectOps.Truthy((Object)(object)squad) || !global::UnityObjectOps.Truthy((Object)(object)squad.upgradeManager))
		{
			return;
		}
		ActiveAbility value = null;
		ActiveAbility[] componentsInChildren = ((Component)squad).GetComponentsInChildren<ActiveAbility>(true);
		foreach (ActiveAbility val in componentsInChildren)
		{
			if (TryState(val, out var state))
			{
				squad.maxCount = state.MaxCount;
				AccessTools.Field(typeof(ActiveAbility), "_chargesRemaining").SetValue(val, state.Charges);
				AccessTools.Field(typeof(ActiveAbility), "_cooldownPeriod").SetValue(val, state.Period);
				AccessTools.Field(typeof(ActiveAbility), "_timeWhenEffectWearsOff").SetValue(val, Time.time + Mathf.Max(0f, state.EffectEnd - AgentAuthority.PlaybackTime));
				if (state.Current)
				{
					value = val;
				}
				if (val is EvacuateAbility)
				{
					AccessTools.Field(typeof(EvacuateAbility), "<state>k__BackingField").SetValue(val, (object)(EvacuateAbility.State)state.EvacState);
				}
				if (val is HouseTargetableAbility)
				{
					houseLocationField.SetValue(val, FindHouse(state.Location));
				}
				AbilityModAdapters.ApplyFacts(val, state);
				if (val is NavSpotTargetableAbility)
				{
					InstallNavFacts((NavSpotTargetableAbility)val, state);
				}
			}
		}
		currentAbilityField.SetValue(squad.upgradeManager, value);
	}

	private static void InstallNavFacts(NavSpotTargetableAbility ability, AbilityWire.State state)
	{
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Unknown result type (might be due to invalid IL or missing references)
		if (!global::UnityObjectOps.Truthy((Object)(object)island))
		{
			return;
		}
		NavSpot val = ((state.Origin >= 0 && state.Origin < island.navSpotter.navSpots.Count) ? island.navSpotter.navSpots[state.Origin] : null);
		heroNavField.SetValue(ability, val);
		object value = targetCacheField.GetValue(ability);
		NavSpotTargetCache val2 = (NavSpotTargetCache)((value is NavSpotTargetCache) ? value : null);
		if (val2 == null || !global::UnityObjectOps.Truthy((Object)(object)val))
		{
			return;
		}
		val2.Resize(island.navSpotter.navSpots.Count);
		NavSpotTargetCache.Entry value2 = new NavSpotTargetCache.Entry
		{
			errorMessage = state.Error
		};
		int[] targets = state.Targets;
		foreach (int num in targets)
		{
			if (num >= island.navSpotter.navSpots.Count || num >= 192)
			{
				throw new IOException("技能导航目标越界");
			}
			value2.targetMask.Set(num, true);
		}
		val2.entries[val.index] = value2;
	}

	public static void LateTick()
	{
		if (AgentAuthority.ClientPlayback)
		{
			AbilityVisuals.LateTick();
		}
	}

	private static bool SameTargets(int[] a, int[] b)
	{
		if (a.Length != b.Length)
		{
			return false;
		}
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i])
			{
				return false;
			}
		}
		return true;
	}

	private static void Diagnose(ActiveAbility ability, string phase, int id)
	{
		bool flag = TryState(ability, out var state);
		NavSpotTargetableAbility val = (NavSpotTargetableAbility)(object)((ability is NavSpotTargetableAbility) ? ability : null);
		object obj;
		if (!global::UnityObjectOps.Truthy((Object)(object)val))
		{
			obj = null;
		}
		else
		{
			object value = targetCacheField.GetValue(val);
			obj = ((value is NavSpotTargetCache) ? value : null);
		}
		NavSpotTargetCache val2 = (NavSpotTargetCache)obj;
		ICollection collection = (global::UnityObjectOps.Truthy((Object)(object)val) ? (candidatesField.GetValue(val) as ICollection) : null);
		Plugin.Log.LogInfo((object)("[技能输入] phase=" + phase + " epoch=" + AgentAuthority.Epoch + " request=" + id + " hero=" + ((global::UnityObjectOps.Truthy((Object)(object)ability) && global::UnityObjectOps.Truthy((Object)(object)((UpgradeComponent)ability).squad) && ((UpgradeComponent)ability).squad.hero != null) ? ((UpgradeComponent)ability).squad.hero.id.ToString() : "?") + " type=" + (global::UnityObjectOps.Truthy((Object)(object)ability) ? ((object)ability).GetType().FullName : "?") + " factsReady=" + flag + " blocked=" + (!flag || state.Blocked) + " origin=" + (flag ? state.Origin : (-1)) + " candidates=" + (flag ? state.Targets.Length : 0) + " drawn=" + (collection?.Count ?? (-1)) + " cacheReady=" + (((val2 != null) & flag) && state.Origin >= 0 && state.Origin < val2.entries.Count) + " targeterReady=" + (global::UnityObjectOps.Truthy((Object)(object)val) && val.targeter != null) + " available=" + (flag && state.Available) + " instantAvailable=" + (flag && state.InstantAvailable) + " error=" + (flag ? state.Error : "等待主机事实")));
	}
}
