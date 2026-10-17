using HarmonyLib;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(UpgradesProxyExtensions), "IsUpgradingAllowed")]
internal static class RemoteUpgradeAllowedPatch
{
	private static void Postfix(ref bool __result)
	{
		if (CampaignSync.ClientDecisionsBlocked)
		{
			__result = false;
		}
	}
}
