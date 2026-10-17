using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Together;

internal static class ClientDriverPolicy
{
	[HarmonyPatch]
	private static class OrdinaryDriverPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			string[] names = ClientDriverPolicy.names;
			foreach (string text in names)
			{
				Type type = AccessTools.TypeByName(text);
				if (type == null)
				{
					continue;
				}
				string[] array = new string[4] { "Start", "Update", "DriveAgent", "Setup" };
				foreach (string text2 in array)
				{
					MethodInfo methodInfo = AccessTools.Method(type, text2, (Type[])null, (Type[])null);
					if (methodInfo != null)
					{
						yield return methodInfo;
					}
				}
			}
		}

		private static bool Prefix(MonoBehaviour __instance)
		{
			if (!AgentAuthority.ClientPlayback)
			{
				return true;
			}
			__instance.StopAllCoroutines();
			((Behaviour)__instance).enabled = false;
			return false;
		}
	}

	[HarmonyPatch]
	private static class KnownModCallbackPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			string[,] callbacks = new string[2, 2]
			{
				{ "Voxels.TowerDefense.FireArrowMod", "OnAgentDealDamagePostfix" },
				{ "You_Ji_Dui.Agent_FixedUpdate_Patch", "Postfix" }
			};
			for (int i = 0; i < callbacks.GetLength(0); i++)
			{
				Type type = AccessTools.TypeByName(callbacks[i, 0]);
				MethodInfo methodInfo = ((type == null) ? null : AccessTools.Method(type, callbacks[i, 1], (Type[])null, (Type[])null));
				if (methodInfo != null)
				{
					yield return methodInfo;
				}
			}
		}

		private static bool Prefix()
		{
			return !AgentAuthority.ClientPlayback;
		}
	}

	private static readonly string[] names = new string[5] { "Voxels.TowerDefense.Upgrades.RegenerativeBuff", "Voxels.TowerDefense.Upgrades.RegenerativeSpearBuff", "Voxels.TowerDefense.Upgrades.RegenerativeJumpResponder", "Voxels.TowerDefense.BurningAgent", "You_Ji_Dui.GuerrillaArcheryEnforcer" };

	internal static bool IsDriver(Type type)
	{
		string[] array = names;
		foreach (string text in array)
		{
			if (type.FullName == text)
			{
				return true;
			}
		}
		return false;
	}
}
