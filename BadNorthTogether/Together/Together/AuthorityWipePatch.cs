using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Island), "WipeIsland")]
internal static class AuthorityWipePatch
{
	private static void Prefix()
	{
		AgentAuthority.Reset();
		WorldAuthority.Reset();
		AbilityCommands.Reset();
		CombatEvents.Reset();
	}
}
