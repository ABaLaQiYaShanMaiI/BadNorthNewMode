using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(LevelLeaver), "CompleteLevel")]
internal static class CampaignCompletePatch
{
	private static bool Prefix()
	{
		return CampaignSync.BeforeLeave(CampaignExit.Complete);
	}
}
