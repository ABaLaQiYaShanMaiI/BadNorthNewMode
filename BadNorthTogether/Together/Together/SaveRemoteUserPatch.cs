using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Profile), "SaveUserSave")]
internal static class SaveRemoteUserPatch
{
	private static bool Prefix()
	{
		return !CampaignSync.HasIsolatedUserSave;
	}
}
