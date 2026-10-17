using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(LevelLeaver), "ReplayLevel")]
internal static class CampaignReplayPatch
{
	private static bool Prefix()
	{
		return CampaignSync.BeforeLeave(CampaignExit.Replay);
	}
}
