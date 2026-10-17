using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Squad), "Update")]
internal static class AuthoritySquadUpdatePatch
{
	private static bool Prefix(Squad __instance)
	{
		return AgentAuthority.BeforeSquadUpdate(__instance);
	}
}
