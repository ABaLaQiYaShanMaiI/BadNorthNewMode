using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(LevelPreparePhase), "Update")]
internal static class LevelPrepareUpdatePatch
{
	private static bool Prefix()
	{
		return !Session.Active;
	}
}
