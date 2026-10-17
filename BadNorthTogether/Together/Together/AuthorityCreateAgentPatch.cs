using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Squad), "CreateAgent")]
internal static class AuthorityCreateAgentPatch
{
	private static void Postfix(Squad __instance, Agent prefab, Agent __result)
	{
		AgentAuthority.RegisterAgent(__instance, __result, prefab);
	}
}
