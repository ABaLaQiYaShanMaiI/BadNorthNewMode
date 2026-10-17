using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Agent), "FinalDeath")]
internal static class AuthorityFinalDeathPatch
{
	private static bool Prefix(Agent __instance)
	{
		return AgentAuthority.BeforeFinalDeath(__instance);
	}
}
