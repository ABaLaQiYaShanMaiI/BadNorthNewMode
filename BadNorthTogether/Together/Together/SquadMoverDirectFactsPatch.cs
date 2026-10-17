using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(SquadMover), "MoveToDirect")]
internal static class SquadMoverDirectFactsPatch
{
	private static void Prefix()
	{
		SquadCommands.BeginOperation();
	}

	private static void Postfix()
	{
		SquadCommands.EndOperation();
	}
}
