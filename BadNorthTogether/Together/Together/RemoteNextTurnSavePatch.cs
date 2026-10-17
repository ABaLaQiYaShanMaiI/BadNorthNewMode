using HarmonyLib;
using Voxels.TowerDefense.ProfileInternals;

namespace Together;

[HarmonyPatch(typeof(CampaignSave), "NextTurn")]
internal static class RemoteNextTurnSavePatch
{
	private static bool Prefix()
	{
		return !CampaignSync.ClientDecisionsBlocked;
	}
}
