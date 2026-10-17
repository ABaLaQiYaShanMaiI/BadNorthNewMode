using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(LevelLeaver), "ExitToMainMenu")]
internal static class CampaignMainMenuPatch
{
	private static bool Prefix()
	{
		return CampaignSync.BeforeLeave(CampaignExit.MainMenu);
	}
}
