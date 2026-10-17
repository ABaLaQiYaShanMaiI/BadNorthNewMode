using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Island), "PlayIsland")]
internal static class AuthorityIslandPatch
{
	private static void Prefix(Island __instance)
	{
		AgentAuthority.BeginIsland(__instance);
	}
}
