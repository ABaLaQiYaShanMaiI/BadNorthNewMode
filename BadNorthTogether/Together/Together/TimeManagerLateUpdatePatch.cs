using HarmonyLib;

namespace Together;

[HarmonyPatch(typeof(TimeManager), "LateUpdate")]
internal static class TimeManagerLateUpdatePatch
{
	private static bool Prefix()
	{
		return TempoSync.ApplyClientScale();
	}
}
