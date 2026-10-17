using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(SquadMover), "SwapWith")]
internal static class SquadMoverSwapPatch
{
	private static bool Prefix(EnglishSquad squad, NavSpotController other)
	{
		if (CampaignSync.IsRemoteCampaign && !Session.IsClient && !Session.ApplyingRemote)
		{
			return false;
		}
		if (!Session.Active || Session.ApplyingRemote)
		{
			return true;
		}
		if (CampaignSync.ClosingBattle)
		{
			return false;
		}
		if (Session.IsClient)
		{
			if (SquadCommands.SendClientSwap(squad, other))
			{
				Singleton<SquadSelector>.instance.SelectSquad((EnglishSquad)null, false);
			}
			return false;
		}
		SquadCommands.TrackLocalOrder(squad, other);
		SquadCommands.BeginOperation();
		return true;
	}

	private static void Postfix()
	{
		SquadCommands.EndOperation();
	}
}
