using System;
using HarmonyLib;
using Voxels.TowerDefense.UI.UpgradeScreen;

namespace Together;

[HarmonyPatch(typeof(SuperUpgradeMenu), "OnClicked", new Type[] { typeof(UpgradeToken) })]
internal static class RemoteUpgradeClickPatch
{
	private static bool Prefix()
	{
		return !CampaignSync.ClientDecisionsBlocked;
	}
}
