using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(Squad), "ReportDead")]
internal static class AuthorityReportDeadPatch
{
	private static bool Prefix(Agent agent)
	{
		return !AgentAuthority.BlockClientDamage(agent);
	}
}
