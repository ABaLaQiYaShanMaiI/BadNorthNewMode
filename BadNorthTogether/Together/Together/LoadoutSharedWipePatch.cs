using HarmonyLib;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(LoadoutUI), "Voxels.TowerDefense.IslandGameplayManager.IWipeIsland.OnWipe")]
internal static class LoadoutSharedWipePatch
{
	private static void Prefix(LoadoutUI __instance)
	{
		DeploySync.ReleaseLoadoutUi(__instance);
	}
}
