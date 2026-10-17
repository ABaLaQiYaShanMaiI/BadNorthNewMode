using HarmonyLib;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(MetaMenuHelpers), "LoadGame")]
internal static class RemoteLoadGamePatch
{
	private static bool Prefix(ref bool __result)
	{
		if (!CampaignSync.ClientDecisionsBlocked)
		{
			return true;
		}
		__result = false;
		return false;
	}
}
