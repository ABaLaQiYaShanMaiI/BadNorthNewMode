using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(EnglishSquad), "ReportDead")]
internal static class AuthorityEnglishReportDeadPatch
{
	private static bool Prefix(Agent agent)
	{
		return !AgentAuthority.BlockClientDamage(agent);
	}
}
