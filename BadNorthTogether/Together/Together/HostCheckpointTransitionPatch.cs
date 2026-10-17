using HarmonyLib;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(MetaMenuHelpers), "LoadCheckpoint")]
internal static class HostCheckpointTransitionPatch
{
	private static bool Prefix()
	{
		if (Session.IsHost)
		{
			return CampaignSync.BeforeLeave(CampaignExit.Checkpoint);
		}
		return true;
	}
}
