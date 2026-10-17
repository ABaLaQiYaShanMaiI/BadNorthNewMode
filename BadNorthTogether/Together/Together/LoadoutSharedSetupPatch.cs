using HarmonyLib;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(LoadoutUI), "Voxels.TowerDefense.IslandGameplayManager.ISetupIsland.OnSetup")]
internal static class LoadoutSharedSetupPatch
{
	private static void Prefix(LoadoutUI __instance)
	{
		DeploySync.ReleaseLoadoutUi(__instance);
	}

	private static void Postfix(LoadoutUI __instance, Island island)
	{
		DeploySync.OnLoadoutSetup(__instance, island);
	}
}
