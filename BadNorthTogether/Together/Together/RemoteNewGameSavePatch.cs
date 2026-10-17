using System;
using HarmonyLib;
using Voxels.TowerDefense.ProfileInternals;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(MetaMenuHelpers), "NewGame", new Type[] { typeof(CampaignSave) })]
internal static class RemoteNewGameSavePatch
{
	private static bool Prefix()
	{
		return !CampaignSync.ClientDecisionsBlocked;
	}
}
