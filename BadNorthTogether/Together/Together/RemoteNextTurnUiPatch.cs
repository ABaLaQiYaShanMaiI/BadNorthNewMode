using HarmonyLib;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(CampaignMapUI), "NextTurn")]
internal static class RemoteNextTurnUiPatch
{
	private static bool Prefix()
	{
		return !CampaignSync.ClientDecisionsBlocked;
	}
}
