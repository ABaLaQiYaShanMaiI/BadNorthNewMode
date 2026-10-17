using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(IslandWinConditions), "Update")]
internal static class AuthorityWinCheckPatch
{
	private static bool Prefix()
	{
		return !AgentAuthority.ClientPlayback;
	}
}
