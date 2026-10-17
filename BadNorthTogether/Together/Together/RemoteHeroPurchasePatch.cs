using HarmonyLib;

namespace Together;

[HarmonyPatch(/*Could not decode attribute arguments.*/)]
internal static class RemoteHeroPurchasePatch
{
	private static bool Prefix(ref bool __result)
	{
		if (!CampaignSync.ClientDecisionsBlocked)
		{
			return true;
		}
		__result = false;
		return false;
	}
}
