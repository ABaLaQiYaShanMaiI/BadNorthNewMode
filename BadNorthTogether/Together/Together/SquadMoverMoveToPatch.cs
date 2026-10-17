using HarmonyLib;
using Voxels.TowerDefense;

namespace Together;

[HarmonyPatch(typeof(SquadMover), "MoveTo")]
internal static class SquadMoverMoveToPatch
{
	private static bool Prefix(EnglishSquad squad, NavSpot newNavSpot)
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
			if (SquadCommands.SendClientMove(squad, newNavSpot))
			{
				Singleton<SquadSelector>.instance.SelectSquad((EnglishSquad)null, false);
			}
			return false;
		}
		if (squad.navSpotOccupant.canMove)
		{
			SquadCommands.TrackLocalOrder(squad);
		}
		return true;
	}
}
