using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Profile), "SaveCampaign")]
internal static class SaveCampaignPatch
{
	private static bool Prefix()
	{
		return !CampaignSync.IsRemoteCampaign;
	}

	private static void Postfix()
	{
		if (Session.IsHost)
		{
			CampaignSync.SendCampaignState();
		}
	}
}
