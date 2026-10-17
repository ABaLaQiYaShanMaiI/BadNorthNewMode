using System;
using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

// 目标：HeroDefinition.TryPurchase(HeroUpgradeDefinition upgradeDef, int level, ref int coinBank)
[HarmonyPatch(typeof(HeroDefinition), "TryPurchase",
    new Type[] { typeof(HeroUpgradeDefinition), typeof(int), typeof(int) },
    new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref })]
internal static class RemoteHeroPurchasePatch
{
	private static bool Prefix(ref bool __result)
	{
		if (!CampaignSync.ClientDecisionsBlocked)
		{
			return true;
		}
		__result = false;
		return false;
	}
}
