using HarmonyLib;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(EnemyLineup), "Prepare")]
internal static class AuthorityResultLineupPatch
{
	private static void Postfix(EnemyLineup __instance)
	{
		AgentAuthority.OnLineupPrepared(__instance);
	}
}
