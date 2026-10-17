using HarmonyLib;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(LoadoutUI), "HandleDeployButton")]
internal static class LoadoutDeployPatch
{
	private static bool Prefix(LoadoutUI __instance)
	{
		return DeploySync.HandleDeployPressed(__instance);
	}
}
