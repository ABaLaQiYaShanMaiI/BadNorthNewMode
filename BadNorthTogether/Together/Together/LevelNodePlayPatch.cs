using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(LevelNode), "Play")]
internal static class LevelNodePlayPatch
{
	private static bool Prefix(LevelNode __instance)
	{
		return CampaignSync.BeforePlay(__instance);
	}
}
