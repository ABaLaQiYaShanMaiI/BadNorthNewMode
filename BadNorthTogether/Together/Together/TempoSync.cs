using System.IO;
using System.Reflection;
using HarmonyLib;
using Together.Net;
using UnityEngine;
using Voxels.TowerDefense;

namespace Together;

internal static class TempoSync
{
	private static readonly object remoteRequester = new object();

	private static readonly FieldInfo currentField = AccessTools.Field(typeof(TimeManager), "current");

	private static int levelIndex = -1;

	private static int epoch;

	private static int stateSequence;

	private static int receivedSequence;

	private static bool manualSlow;

	private static bool peerSlow;

	private static float hostScale = 1f;

	private static bool hostStateSent;

	private static float nextIntentSend;

	private static float nextStateSend;

	private static bool initialized;

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			Session.On(MsgType.SlowIntent, ReceiveIntent);
			Session.On(MsgType.SlowState, ReceiveState);
			Session.Connected += UpdateSelectionTimeScale;
			Session.Stopped += Reset;
			Session.Stopped += UpdateSelectionTimeScale;
			TimeManager.onTimeScaleChanged += OnTimeScaleChanged;
		}
	}

	private static void UpdateSelectionTimeScale()
	{
		SquadSelector instance = Singleton<SquadSelector>.instance;
		if (Session.Active)
		{
			if ((Object)(object)instance != (Object)null && TimeManager.HasTimeScaleRequest((object)instance))
			{
				TimeManager.RemoveTimeScale((object)instance);
			}
			Plugin.Log.LogInfo((object)"[时间] 联机选中不再缓速，保留手动缓速和暂停");
		}
		else if (InLevel() && !CampaignSync.IsRemoteCampaign && (Object)(object)instance != (Object)null && (Object)(object)instance.selectedSquad != (Object)null)
		{
			TimeManager.RequestTimeScale((object)instance, 0.1f);
			Plugin.Log.LogInfo((object)"[时间] 已恢复单机选中缓速");
		}
	}

	private static void Reset()
	{
		TimeManager.RemoveTimeScale(remoteRequester);
		levelIndex = -1;
		epoch = (stateSequence = (receivedSequence = 0));
		manualSlow = (peerSlow = false);
		hostScale = 1f;
		hostStateSent = false;
		nextIntentSend = (nextStateSend = 0f);
	}

	public static void Tick()
	{
		if (!Session.Active || !InLevel() || CampaignSync.ClosingBattle)
		{
			if (levelIndex >= 0)
			{
				Reset();
			}
			return;
		}
		int lastPlayedLevelIdx = Profile.campaign.lastPlayedLevelIdx;
		if (levelIndex != lastPlayedLevelIdx || epoch != CampaignSync.CurrentBattleEpoch)
		{
			TimeManager.RemoveTimeScale(remoteRequester);
			levelIndex = lastPlayedLevelIdx;
			epoch = CampaignSync.CurrentBattleEpoch;
			stateSequence = (receivedSequence = 0);
			peerSlow = false;
			hostScale = 1f;
			hostStateSent = false;
			GameController componentInChildren = ((Component)Singleton<IslandGameplayManager>.instance).GetComponentInChildren<GameController>(true);
			manualSlow = (Object)(object)componentInChildren != (Object)null && TimeManager.HasTimeScaleRequest((object)componentInChildren);
			if (Session.IsHost)
			{
				BroadcastHostState((float)currentField.GetValue(null));
			}
			else
			{
				SendIntent();
			}
			Plugin.Log.LogInfo((object)("[时间] 进入岛屿 " + lastPlayedLevelIdx + "，本地手动缓速=" + manualSlow + "，选中不缓速"));
		}
		if (Session.IsHost && Time.unscaledTime >= nextStateSend)
		{
			BroadcastHostState((float)currentField.GetValue(null), force: true);
		}
		else if (Session.IsClient && Time.unscaledTime >= nextIntentSend)
		{
			SendIntent();
		}
	}

	public static void LocalRequestChanged(object requester)
	{
		if (Session.Active && InLevel() && !CampaignSync.ClosingBattle && requester is GameController)
		{
			manualSlow = TimeManager.HasTimeScaleRequest(requester);
			if (levelIndex == Profile.campaign.lastPlayedLevelIdx && Session.IsClient)
			{
				SendIntent();
			}
		}
	}

	private static void SendIntent()
	{
		MessageWriter messageWriter = Protocol.Begin(MsgType.SlowIntent);
		messageWriter.Write(14);
		messageWriter.Write(epoch);
		messageWriter.Write(levelIndex);
		messageWriter.Write(manualSlow);
		Session.Send(messageWriter.ToArray());
		nextIntentSend = Time.unscaledTime + 1f;
	}

	private static void ReceiveIntent(BinaryReader reader)
	{
		if (reader.ReadInt32() != 14)
		{
			throw new IOException("缓速意图版本不一致");
		}
		int num = reader.ReadInt32();
		int level = reader.ReadInt32();
		bool flag = reader.ReadBoolean();
		reader.RequireEnd();
		if (Session.IsHost && CommandIdentity.Matches(num, epoch, level, levelIndex, CampaignSync.ClosingBattle) && peerSlow != flag)
		{
			peerSlow = flag;
			if (peerSlow)
			{
				TimeManager.RequestTimeScale(remoteRequester, 0.1f);
			}
			else
			{
				TimeManager.RemoveTimeScale(remoteRequester);
			}
		}
	}

	private static void OnTimeScaleChanged(float scale)
	{
		if (Session.IsHost && levelIndex >= 0 && InLevel() && !CampaignSync.ClosingBattle)
		{
			BroadcastHostState(scale);
		}
	}

	private static void BroadcastHostState(float scale, bool force = false)
	{
		bool flag = scale != hostScale || !hostStateSent;
		if (flag || force)
		{
			hostScale = scale;
			hostStateSent = true;
			nextStateSend = Time.unscaledTime + 1f;
			MessageWriter messageWriter = Protocol.Begin(MsgType.SlowState);
			messageWriter.Write(14);
			messageWriter.Write(epoch);
			messageWriter.Write(levelIndex);
			messageWriter.Write(++stateSequence);
			messageWriter.Write(hostScale);
			Session.Send(messageWriter.ToArray());
			if (flag)
			{
				Plugin.Log.LogInfo((object)("[时间] 主机决定 " + hostScale.ToString("0.###") + " 倍速"));
			}
		}
	}

	private static void ReceiveState(BinaryReader reader)
	{
		if (reader.ReadInt32() != 14)
		{
			throw new IOException("缓速状态版本不一致");
		}
		int num = reader.ReadInt32();
		int level = reader.ReadInt32();
		int num2 = reader.ReadInt32();
		float num3 = reader.ReadSingle();
		reader.RequireEnd();
		if (num2 <= 0 || float.IsNaN(num3) || float.IsInfinity(num3) || num3 < 0f || num3 > 5f)
		{
			throw new IOException("主机时间状态异常");
		}
		if (Session.IsClient && CommandIdentity.Matches(num, epoch, level, levelIndex, CampaignSync.ClosingBattle) && num2 > receivedSequence)
		{
			receivedSequence = num2;
			bool num4 = !hostStateSent || num3 != hostScale;
			hostScale = num3;
			hostStateSent = true;
			if (num4)
			{
				Plugin.Log.LogInfo((object)("[时间] 已采用主机的 " + hostScale.ToString("0.###") + " 倍速"));
			}
		}
	}

	public static bool ApplyClientScale()
	{
		if (!Session.IsClient || !InLevel())
		{
			return true;
		}
		Time.timeScale = ((AgentAuthority.HasFailure || CampaignSync.ClosingBattle || PauseSync.AuthoritativePaused) ? 0f : hostScale);
		return false;
	}

	private static bool InLevel()
	{
		Stack instance = Singleton<Stack>.instance;
		if ((Object)(object)instance != (Object)null && instance.stateLevel.active)
		{
			return Profile.campaign != null;
		}
		return false;
	}
}
