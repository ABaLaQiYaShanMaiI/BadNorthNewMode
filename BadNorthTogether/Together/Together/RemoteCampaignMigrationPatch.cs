using HarmonyLib;
using Voxels.TowerDefense.ProfileInternals;

namespace Together;

[HarmonyPatch(typeof(CampaignSave), "PostLoad")]
internal static class RemoteCampaignMigrationPatch
{
	private static bool Prefix()
	{
		return !CampaignSync.DecodingSnapshot;
	}
}
