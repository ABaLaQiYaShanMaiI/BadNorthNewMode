using HarmonyLib;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(LoadoutUIListBanner), "UpdateHero")]
internal static class LoadoutSharedHeroPatch
{
	private static bool Prefix(LoadoutUIListBanner __instance, HeroDefinition heroDef)
	{
		return DeploySync.BeforeBannerUpdate(__instance, heroDef);
	}
}
