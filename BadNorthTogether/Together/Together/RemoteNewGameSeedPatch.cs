using System;
using HarmonyLib;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(MetaMenuHelpers), "NewGame", new Type[] { typeof(int) })]
internal static class RemoteNewGameSeedPatch
{
	private static bool Prefix()
	{
		return !CampaignSync.ClientDecisionsBlocked;
	}
}
