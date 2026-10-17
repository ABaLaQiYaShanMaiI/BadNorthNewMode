using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Raid), "BeginNextWave")]
internal static class RaidBeginNextWavePatch
{
	private static bool Prefix(Raid __instance)
	{
		return WaveSync.BeforeBeginWave(__instance);
	}
}
