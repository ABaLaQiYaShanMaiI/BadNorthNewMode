using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(LevelPauser), "OnApplicationFocus")]
internal static class LevelPauserFocusPatch
{
	private static bool Prefix(bool focus)
	{
		if (!focus)
		{
			return !Session.Active;
		}
		return true;
	}
}
