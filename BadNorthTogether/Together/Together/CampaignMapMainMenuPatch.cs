using HarmonyLib;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(MetaMenuHelpers), "ExitToMainMenu")]
internal static class CampaignMapMainMenuPatch
{
	private static bool Prefix()
	{
		return CampaignSync.BeforeLeave(CampaignExit.MainMenu);
	}
}
