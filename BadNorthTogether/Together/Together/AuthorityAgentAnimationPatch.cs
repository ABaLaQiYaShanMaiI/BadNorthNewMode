using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Agent), "DoAnimationAction")]
internal static class AuthorityAgentAnimationPatch
{
	private static bool Prefix(Agent __instance)
	{
		if (!AgentAuthority.ClientPlayback)
		{
			return true;
		}
		AgentAuthority.ClearAnimationAction(__instance);
		return false;
	}
}
