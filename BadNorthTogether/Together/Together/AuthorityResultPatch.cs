using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(EndOfLevel), "ProcessEOL")]
internal static class AuthorityResultPatch
{
	private static bool Prefix()
	{
		return AgentAuthority.BeforeResult();
	}

	private static void Postfix(EndOfLevel.Reason reason)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		AgentAuthority.AfterResult(reason);
	}
}
