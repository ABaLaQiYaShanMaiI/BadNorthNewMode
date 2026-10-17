using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(LevelPauser), "SetPause")]
internal static class LevelPauserSetPausePatch
{
	private static bool Prefix(bool pause, bool uiAudio)
	{
		return PauseSync.BeforeLocalPause(pause, uiAudio);
	}
}
