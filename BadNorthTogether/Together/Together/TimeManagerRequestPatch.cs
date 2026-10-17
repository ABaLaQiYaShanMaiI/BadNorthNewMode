using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(TimeManager), "RequestTimeScale")]
internal static class TimeManagerRequestPatch
{
	private static bool Prefix(object requester)
	{
		if (Session.Active)
		{
			return !(requester is SquadSelector);
		}
		return true;
	}

	private static void Postfix(object requester)
	{
		TempoSync.LocalRequestChanged(requester);
	}
}
