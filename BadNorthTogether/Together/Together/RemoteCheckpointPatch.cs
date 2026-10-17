using HarmonyLib;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(MetaMenuHelpers), "LoadCheckpoint")]
internal static class RemoteCheckpointPatch
{
	private static bool Prefix()
	{
		if (!CampaignSync.IsRemoteCampaign)
		{
			return true;
		}
		Plugin.Log.LogWarning((object)"[战役] 临时联机战役不能读取本地检查点，请由主机重新选岛");
		return false;
	}
}
