using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(CampaignManager), "ClearCampaign")]
internal static class ClearRemoteCampaignPatch
{
	private static void Postfix()
	{
		CampaignSync.RestoreLocalProfileIfIdle();
	}
}
