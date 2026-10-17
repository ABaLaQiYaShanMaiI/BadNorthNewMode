using HarmonyLib;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace Together;

[HarmonyPatch(/*Could not decode attribute arguments.*/)]
internal static class AuthorityShipGroupPatch
{
	private static void Postfix(ShipGroup __instance, Squad __result)
	{
		AgentAuthority.RegisterGroup(__instance, __result);
	}
}
