using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(LevelLeaver), "ExitLevel")]
internal static class CampaignExitPatch
{
	private static bool Prefix()
	{
		return CampaignSync.BeforeLeave(CampaignExit.Exit);
	}
}
