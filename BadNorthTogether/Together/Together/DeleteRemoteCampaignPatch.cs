using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Profile), "DeleteCurrentCampaign")]
internal static class DeleteRemoteCampaignPatch
{
	private static bool Prefix()
	{
		return !CampaignSync.IsRemoteCampaign;
	}
}
