using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(IslandGameplayManager), "CallFirstWave")]
internal static class CallFirstWavePatch
{
	private static bool Prefix()
	{
		return DeploySync.BeforeFirstWave();
	}
}
