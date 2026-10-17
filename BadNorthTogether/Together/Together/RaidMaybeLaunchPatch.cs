using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Raid), "MaybeLaunchWaves")]
internal static class RaidMaybeLaunchPatch
{
	private static bool Prefix(Raid __instance, ref bool __result)
	{
		return WaveSync.BeforeMaybeLaunch(__instance, ref __result);
	}
}
