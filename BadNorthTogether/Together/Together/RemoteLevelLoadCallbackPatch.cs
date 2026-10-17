using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(LevelNode), "LevelLoadComplete")]
internal static class RemoteLevelLoadCallbackPatch
{
	private static bool Prefix(LevelNode __instance)
	{
		return CampaignSync.ClientLoadCallbackAllowed(__instance);
	}
}
