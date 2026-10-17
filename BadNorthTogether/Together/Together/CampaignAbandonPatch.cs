using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(LevelLeaver), "AbandonLevel")]
internal static class CampaignAbandonPatch
{
	private static bool Prefix()
	{
		return CampaignSync.BeforeLeave(CampaignExit.Abandon);
	}
}
