using HarmonyLib;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(CampaignMapUI), "OnEnable")]
internal static class RemoteMapActivationPatch
{
	private static bool Prefix()
	{
		return !CampaignSync.IsRemoteCampaign;
	}
}
