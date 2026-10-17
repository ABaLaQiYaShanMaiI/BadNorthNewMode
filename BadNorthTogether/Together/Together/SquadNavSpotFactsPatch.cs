using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(NavSpotController), "SetNavSpot")]
internal static class SquadNavSpotFactsPatch
{
	private static void Postfix(bool __result)
	{
		SquadCommands.AfterSetNavSpot(__result);
	}
}
