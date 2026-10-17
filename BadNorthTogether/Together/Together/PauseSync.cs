using System.IO;
using Together.Net;
using UnityEngine;
using Voxels.TowerDefense;

namespace Together;

internal static class PauseSync
{
	private static readonly CommandPauseState state = new CommandPauseState();

	private static bool initialized;

	private static int epoch;

	private static int localIntent;

	private static bool applyPending;

	private static float nextState;

	public static bool AuthoritativePaused
	{
		get
		{
			if (state.Known)
			{
				return state.Paused;
			}
			return false;
		}
	}

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			Session.On(MsgType.PauseState, ReceivePause);
			Session.Stopped += Reset;
		}
	}

	private static void Reset()
	{
		epoch = (localIntent = 0);
		state.Reset();
		applyPending = false;
		nextState = 0f;
	}

	private static void EnsureBattle()
	{
		if (epoch != CampaignSync.CurrentBattleEpoch)
		{
			Reset();
			epoch = CampaignSync.CurrentBattleEpoch;
			if (Session.IsHost)
			{
				state.Decide(pause: false);
				applyPending = true;
			}
		}
	}

	public static bool BeforeLocalPause(bool pause, bool uiAudio)
	{
		if (uiAudio && CampaignSync.IsRemoteCampaign && !Session.IsClient)
		{
			return false;
		}
		if (!Session.Active || Session.ApplyingRemote || !uiAudio || !InLevel())
		{
			return true;
		}
		if (CampaignSync.ClosingBattle || AgentAuthority.HasFailure)
		{
			return false;
		}
		EnsureBattle();
		if (Session.IsClient)
		{
			MessageWriter messageWriter = Protocol.Begin(MsgType.PauseState);
			messageWriter.Write(14);
			messageWriter.Write(epoch);
			messageWriter.Write(Profile.campaign.lastPlayedLevelIdx);
			messageWriter.Write(value: false);
			messageWriter.Write(++localIntent);
			messageWriter.Write(pause);
			Session.Send(messageWriter.ToArray());
		}
		else
		{
			state.Decide(pause);
			applyPending = true;
			Broadcast();
		}
		return false;
	}

	private static void ReceivePause(BinaryReader reader)
	{
		if (reader.ReadInt32() != 14)
		{
			throw new IOException("暂停版本不一致");
		}
		int num = reader.ReadInt32();
		int level = reader.ReadInt32();
		bool flag = reader.ReadBoolean();
		int num2 = reader.ReadInt32();
		bool pause = reader.ReadBoolean();
		reader.RequireEnd();
		if (num2 <= 0)
		{
			throw new IOException("暂停序号非法");
		}
		if (!InLevel() || !CommandIdentity.Matches(num, CampaignSync.CurrentBattleEpoch, level, Profile.campaign.lastPlayedLevelIdx, CampaignSync.ClosingBattle))
		{
			return;
		}
		EnsureBattle();
		if (Session.IsHost && !flag)
		{
			if (state.AcceptIntent(num2, pause))
			{
				applyPending = true;
			}
			Broadcast();
		}
		else if ((Session.IsClient & flag) && state.AcceptState(num2, pause))
		{
			applyPending = true;
		}
	}

	private static void Broadcast()
	{
		MessageWriter messageWriter = Protocol.Begin(MsgType.PauseState);
		messageWriter.Write(14);
		messageWriter.Write(epoch);
		messageWriter.Write(Profile.campaign.lastPlayedLevelIdx);
		messageWriter.Write(value: true);
		messageWriter.Write(state.Sequence);
		messageWriter.Write(state.Paused);
		Session.Send(messageWriter.ToArray());
		nextState = Time.unscaledTime + 1f;
	}

	public static void Tick()
	{
		if (!Session.Active || !InLevel() || CampaignSync.ClosingBattle || AgentAuthority.HasFailure)
		{
			return;
		}
		EnsureBattle();
		if (Session.IsHost && Time.unscaledTime >= nextState)
		{
			Broadcast();
		}
		if (applyPending)
		{
			LevelPauser levelPauser = Singleton<IslandGameplayManager>.instance.levelPauser;
			bool applyingRemote = Session.ApplyingRemote;
			Session.ApplyingRemote = true;
			try
			{
				levelPauser.SetPause(state.Paused, false);
			}
			finally
			{
				Session.ApplyingRemote = applyingRemote;
			}
			if (levelPauser.isPaused == state.Paused)
			{
				applyPending = false;
				Plugin.Log.LogInfo((object)("[暂停] 主机裁决 #" + state.Sequence + "，战斗 " + epoch + "，暂停=" + state.Paused));
			}
		}
	}

	private static bool InLevel()
	{
		Stack instance = Singleton<Stack>.instance;
		IslandGameplayManager instance2 = Singleton<IslandGameplayManager>.instance;
		if (global::UnityObjectOps.Truthy((Object)(object)instance) && instance.stateLevel.active && Profile.campaign != null && global::UnityObjectOps.Truthy((Object)(object)instance2))
		{
			return global::UnityObjectOps.Truthy((Object)(object)instance2.island);
		}
		return false;
	}
}
