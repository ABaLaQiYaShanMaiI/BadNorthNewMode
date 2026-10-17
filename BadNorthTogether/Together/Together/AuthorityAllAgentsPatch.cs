using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Agent), "UpdateAllAgents")]
internal static class AuthorityAllAgentsPatch
{
	private static bool Prefix()
	{
		if (!AgentAuthority.ClientPlayback)
		{
			return !AgentAuthority.HasFailure;
		}
		return false;
	}
}
