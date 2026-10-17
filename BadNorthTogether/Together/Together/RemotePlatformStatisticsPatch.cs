using System;
using System.Collections.Generic;
using System.Reflection;
using CS.Platform;
using HarmonyLib;

namespace Together;

[HarmonyPatch]
internal static class RemotePlatformStatisticsPatch
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		Type[] types = typeof(BasePlatformManager).Assembly.GetTypes();
		foreach (Type type in types)
		{
			if (!typeof(BasePlatformManager).IsAssignableFrom(type))
			{
				continue;
			}
			MethodInfo[] methods = type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (MethodInfo methodInfo in methods)
			{
				if (!methodInfo.IsAbstract && (methodInfo.Name == "UnlockAchievement" || methodInfo.Name == "IncrementStatistic" || methodInfo.Name == "SetStatistic"))
				{
					yield return methodInfo;
				}
			}
		}
	}

	private static bool Prefix()
	{
		return !CampaignSync.ClientDecisionsBlocked;
	}
}
