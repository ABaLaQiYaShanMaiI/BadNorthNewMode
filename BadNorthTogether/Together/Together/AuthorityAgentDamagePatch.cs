using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Agent), "DealDamage")]
internal static class AuthorityAgentDamagePatch
{
	private static bool Prefix(Agent __instance, ref bool __result)
	{
		if (!AgentAuthority.BlockClientDamage(__instance))
		{
			return true;
		}
		__result = false;
		return false;
	}
}
