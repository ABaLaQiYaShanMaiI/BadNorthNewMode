using HarmonyLib;

namespace Together;

[HarmonyPatch(typeof(TimeManager), "RemoveTimeScale")]
internal static class TimeManagerRemovePatch
{
	private static void Postfix(object requester)
	{
		TempoSync.LocalRequestChanged(requester);
	}
}
