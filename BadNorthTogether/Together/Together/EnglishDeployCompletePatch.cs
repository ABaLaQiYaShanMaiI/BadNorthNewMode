using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(IslandGameplayManager), "EnglishDeployComplete")]
internal static class EnglishDeployCompletePatch
{
	private static void Postfix()
	{
		DeploySync.NotifyDeployFinished();
	}
}
