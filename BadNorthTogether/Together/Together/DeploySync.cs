using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using RTM.UISystem;
using Together.Net;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;

namespace Together;

internal static class DeploySync
{
	private static readonly FieldInfo bannersField = AccessTools.Field(typeof(LoadoutUI), "heroBanners");

	private static readonly MethodInfo canDeployMethod = AccessTools.Method(typeof(LoadoutUI), "CanDeploy", Type.EmptyTypes, (Type[])null);

	private static readonly FieldInfo popupField = AccessTools.Field(typeof(LoadoutUI), "replacePopup");

	private static bool initialized;

	private static int levelIndex = -1;

	private static int epoch;

	private static LoadoutUI localUi;

	private static Island loadoutIsland;

	private static int[] roster;

	private static int rosterRevision;

	private static int localReadyRevision;

	private static int peerReadyRevision;

	private static int nextRequest;

	private static int lastRequest;

	private static bool applyingRoster;

	private static bool pendingRoster;

	private static bool closePopup;

	private static float nextRosterSend;

	private static readonly Dictionary<int, int> pendingChanges = new Dictionary<int, int>();

	private static bool localReady;

	private static bool peerReady;

	private static bool committed;

	private static bool applyingCommit;

	private static bool localFinished;

	private static bool peerFinished;

	private static bool waveStarted;

	private static bool applyingWave;

	private static bool hostWaveRequested;

	private static bool pendingCommit;

	private static bool pendingWave;

	private static float nextReadyRetry;

	private static float nextFinishedRetry;

	public static string StatusText { get; private set; } = "未进入岛屿";

	public static bool BattleStarted
	{
		get
		{
			if (Session.Active && !CampaignSync.ClosingBattle)
			{
				return waveStarted;
			}
			return false;
		}
	}

	public static void BeginIsland(int index)
	{
		Reset();
		levelIndex = index;
		epoch = CampaignSync.CurrentBattleEpoch;
		StatusText = "请选择出战队伍，双方点击部署表示准备";
	}

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			Session.On(MsgType.DeployReady, ReceiveReady);
			Session.On(MsgType.DeployCommit, ReceiveCommit);
			Session.On(MsgType.DeployFinished, ReceiveFinished);
			Session.On(MsgType.FirstWave, ReceiveFirstWave);
			Session.On(MsgType.DeployRoster, ReceiveRoster);
			Session.Stopped += Reset;
		}
	}

	private static void Reset()
	{
		levelIndex = -1;
		epoch = 0;
		localUi = null;
		loadoutIsland = null;
		roster = null;
		rosterRevision = (localReadyRevision = (peerReadyRevision = (nextRequest = (lastRequest = 0))));
		applyingRoster = (pendingRoster = (closePopup = false));
		nextRosterSend = 0f;
		pendingChanges.Clear();
		localReady = (peerReady = (committed = (applyingCommit = false)));
		localFinished = (peerFinished = (waveStarted = (applyingWave = false)));
		hostWaveRequested = (pendingCommit = (pendingWave = false));
		nextReadyRetry = (nextFinishedRetry = 0f);
		StatusText = "未进入岛屿";
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
			Reset();
			levelIndex = lastPlayedLevelIdx;
			epoch = CampaignSync.CurrentBattleEpoch;
			StatusText = "请选择出战队伍，双方点击部署表示准备";
		}
		IslandGameplayManager instance = Singleton<IslandGameplayManager>.instance;
		if (!committed && (Object)(object)instance.island != (Object)null && instance.island.levelNode.wantsTutorial)
		{
			committed = true;
			StatusText = "教程关自动部署，等待两端完成";
			Plugin.Log.LogInfo((object)"[部署] 教程关沿用原版自动部署，等待双端完成");
		}
		if (Session.IsClient && pendingCommit)
		{
			ApplyClientCommit();
		}
		if (!committed && global::UnityObjectOps.Truthy((Object)(object)localUi) && pendingRoster)
		{
			ApplyRoster(localUi);
			pendingRoster = false;
		}
		if (closePopup && global::UnityObjectOps.Truthy((Object)(object)localUi))
		{
			closePopup = false;
			object value = popupField.GetValue(localUi);
			LoadoutReplacePopup val = (LoadoutReplacePopup)((value is LoadoutReplacePopup) ? value : null);
			if (global::UnityObjectOps.Truthy((Object)(object)val) && ((UIMenu)val).isOpen)
			{
				((UIMenu)val).CloseMenu();
			}
		}
		if (Session.IsHost && !committed && roster != null && Time.unscaledTime >= nextRosterSend)
		{
			BroadcastRoster(0, accepted: true, string.Empty);
		}
		if (Session.IsClient && pendingWave)
		{
			ApplyClientFirstWave();
		}
		if (Session.IsClient && localReady && !committed && Time.unscaledTime >= nextReadyRetry)
		{
			nextReadyRetry = Time.unscaledTime + 1f;
			SendReady();
		}
		if (Session.IsClient && localFinished && !waveStarted && Time.unscaledTime >= nextFinishedRetry)
		{
			nextFinishedRetry = Time.unscaledTime + 1f;
			using MessageWriter messageWriter = Protocol.Begin(MsgType.DeployFinished);
			messageWriter.Write(14);
			messageWriter.Write(epoch);
			messageWriter.Write(levelIndex);
			Session.Send(messageWriter.ToArray());
		}
		if (Session.IsHost)
		{
			TryStartFirstWave();
		}
	}

	public static bool HandleDeployPressed(LoadoutUI ui)
	{
		if (CampaignSync.IsRemoteCampaign && !Session.IsClient)
		{
			return false;
		}
		if (Session.Active && CampaignSync.ClosingBattle)
		{
			return false;
		}
		if (!Session.Active || !InLevel() || applyingCommit)
		{
			return true;
		}
		if (localReady || committed)
		{
			return false;
		}
		if ((Object)(object)localUi != (Object)(object)ui || roster == null || rosterRevision <= 0 || pendingChanges.Count > 0 || pendingRoster)
		{
			StatusText = "等待共享名单或换人回复后再准备";
			return false;
		}
		if (!(bool)canDeployMethod.Invoke(ui, null))
		{
			return false;
		}
		localReady = true;
		localReadyRevision = rosterRevision;
		if (Session.IsHost)
		{
			StatusText = "主机已准备，等待客户端";
			Plugin.Log.LogInfo((object)("[部署] 主机准备名单修订 " + rosterRevision));
			TryCommit();
		}
		else
		{
			SendReady();
			StatusText = "已准备，等待主机部署";
			Plugin.Log.LogInfo((object)("[部署] 客户端准备名单修订 " + rosterRevision));
		}
		return false;
	}

	private static void ReceiveReady(BinaryReader reader)
	{
		if (reader.ReadInt32() != 14)
		{
			throw new IOException("部署准备版本不一致");
		}
		int num = reader.ReadInt32();
		int level = reader.ReadInt32();
		int num2 = reader.ReadInt32();
		reader.RequireEnd();
		if (Session.IsHost && CommandIdentity.Matches(num, epoch, level, levelIndex, CampaignSync.ClosingBattle))
		{
			if (roster == null || num2 != rosterRevision)
			{
				BroadcastRoster(0, accepted: false, "名单已变化，请重新准备");
				return;
			}
			peerReady = true;
			peerReadyRevision = num2;
			StatusText = (localReady ? "双方已准备，开始部署" : "客户端已准备，等待主机");
			Plugin.Log.LogInfo((object)("[部署] 收到客户端准备，岛屿 " + level));
			TryCommit();
		}
	}

	private static void TryCommit()
	{
		if (Session.IsHost && localReady && peerReady && !committed && DeploymentRoster.CanCommit(rosterRevision, localReadyRevision, peerReadyRevision))
		{
			MessageWriter messageWriter = Protocol.Begin(MsgType.DeployCommit);
			messageWriter.Write(14);
			messageWriter.Write(epoch);
			messageWriter.Write(levelIndex);
			messageWriter.Write(rosterRevision);
			DeploymentRoster.WriteRoster(messageWriter, roster);
			if (Session.Send(messageWriter.ToArray()))
			{
				committed = true;
				StatusText = "正在部署双方确认的队伍";
				Plugin.Log.LogInfo((object)"[部署] 双方准备完成，主机开始统一部署");
				ApplyOriginalDeploy(localUi);
			}
		}
	}

	private static void ReceiveCommit(BinaryReader reader)
	{
		if (reader.ReadInt32() != 14)
		{
			throw new IOException("部署名单版本不一致");
		}
		int num = reader.ReadInt32();
		int level = reader.ReadInt32();
		int num2 = reader.ReadInt32();
		int[] array = DeploymentRoster.ReadRoster(reader);
		reader.RequireEnd();
		if (Session.IsClient && CommandIdentity.Matches(num, epoch, level, levelIndex, CampaignSync.ClosingBattle) && localReady && !committed)
		{
			if (num2 != localReadyRevision || num2 != rosterRevision)
			{
				throw new IOException("部署提交与本机准备名单不一致");
			}
			roster = array;
			committed = true;
			pendingCommit = true;
			StatusText = "收到主机出战名单，正在部署";
			Plugin.Log.LogInfo((object)("[部署] 收到统一部署命令，修订 " + num2 + "，槽位 " + array.Length));
		}
	}

	private static void ApplyClientCommit()
	{
		if (!((Object)(object)localUi == (Object)null))
		{
			pendingCommit = false;
			ApplyOriginalDeploy(localUi);
		}
	}

	private static void ApplyRoster(LoadoutUI ui)
	{
		LoadoutUIListBanner[] array = (LoadoutUIListBanner[])bannersField.GetValue(ui);
		if (array.Length != roster.Length)
		{
			throw new IOException("两端出战槽位数量不同");
		}
		bool flag = applyingRoster;
		applyingRoster = true;
		try
		{
			for (int i = 0; i < array.Length; i++)
			{
				HeroDefinition val = ResolveHero(roster[i]);
				if (roster[i] >= 0 && !(bool)(val))
				{
					throw new IOException("共享名单找不到英雄 " + roster[i]);
				}
				if (array[i].heroDef != val)
				{
					array[i].UpdateHero(val);
				}
			}
		}
		finally
		{
			applyingRoster = flag;
		}
	}

	private static void ApplyOriginalDeploy(LoadoutUI ui)
	{
		ApplyRoster(ui);
		if (!(bool)canDeployMethod.Invoke(ui, null))
		{
			throw new IOException("共享出战名单不可部署");
		}
		applyingCommit = true;
		try
		{
			ui.HandleDeployButton();
		}
		finally
		{
			applyingCommit = false;
		}
	}

	internal static void OnLoadoutSetup(LoadoutUI ui, Island value)
	{
		if (!Session.Active || CampaignSync.ClosingBattle || value.levelNode.wantsTutorial)
		{
			return;
		}
		localUi = ui;
		loadoutIsland = value;
		if (Session.IsHost)
		{
			LoadoutUIListBanner[] array = (LoadoutUIListBanner[])bannersField.GetValue(ui);
			roster = new int[array.Length];
			for (int i = 0; i < array.Length; i++)
			{
				roster[i] = ((bool)(array[i].heroDef) ? array[i].heroDef.id : (-1));
			}
			DeploymentRoster.ValidateRoster(roster);
			rosterRevision = 1;
			BroadcastRoster(0, accepted: true, string.Empty);
			Plugin.Log.LogInfo((object)("[部署选人] 主机初始化 epoch=" + epoch + " 修订=" + rosterRevision + " 名单=" + RosterText()));
		}
		else if (roster != null)
		{
			pendingRoster = true;
		}
		StatusText = "双方可更换出战队伍，名单确认后点击部署";
	}

	internal static void ReleaseLoadoutUi(LoadoutUI ui)
	{
		if ((Object)(object)localUi == (Object)(object)ui)
		{
			localUi = null;
			loadoutIsland = null;
			closePopup = false;
		}
	}

	private static HeroDefinition ResolveHero(int id)
	{
		if (id < 0)
		{
			return null;
		}
		HeroDefinition val = (global::UnityObjectOps.Truthy((Object)(object)loadoutIsland) ? loadoutIsland.levelNode.heroDefinition : null);
		if (!(bool)(val) || val.id != id)
		{
			return Profile.campaign.TryGetHeroDefinition(id);
		}
		return val;
	}

	private static int LockedSlot()
	{
		HeroDefinition val = (global::UnityObjectOps.Truthy((Object)(object)loadoutIsland) ? loadoutIsland.levelNode.heroDefinition : null);
		if (!(bool)(val) || !val.recruitable)
		{
			return -1;
		}
		return roster.Length - 1;
	}

	private static bool HeroAvailable(int id)
	{
		HeroDefinition val = ResolveHero(id);
		if ((bool)(val))
		{
			return val.availableThisTurn;
		}
		return false;
	}

	internal static bool BeforeBannerUpdate(LoadoutUIListBanner banner, HeroDefinition hero)
	{
		if (!Session.Active || applyingRoster || applyingCommit || !global::UnityObjectOps.Truthy((Object)(object)localUi))
		{
			return true;
		}
		int num = Array.IndexOf((LoadoutUIListBanner[])bannersField.GetValue(localUi), banner);
		if (num < 0)
		{
			return true;
		}
		if (committed || CampaignSync.ClosingBattle || roster == null)
		{
			return false;
		}
		int num2 = ((bool)(hero) ? hero.id : (-1));
		if (num2 == roster[num])
		{
			return true;
		}
		if (Session.IsHost)
		{
			ReplaceSlot(num, roster[num], num2, 0);
		}
		else
		{
			if (pendingChanges.ContainsKey(num))
			{
				StatusText = "该槽位换人正在等待主机确认";
				return false;
			}
			DeploymentRoster.Message message = new DeploymentRoster.Message
			{
				Epoch = epoch,
				Level = levelIndex,
				Request = ++nextRequest,
				Slot = num,
				Expected = roster[num],
				Hero = num2
			};
			using (MessageWriter messageWriter = Protocol.Begin(MsgType.DeployRoster))
			{
				message.Write(messageWriter);
				if (!Session.Send(messageWriter.ToArray()))
				{
					return false;
				}
			}
			pendingChanges.Add(num, message.Request);
			StatusText = "换人请求已发送，等待主机确认";
			Plugin.Log.LogInfo((object)("[部署选人] 客户端请求 " + message.Request + " 槽位=" + num + " 英雄=" + num2 + " 原英雄=" + message.Expected));
		}
		return false;
	}

	private static void ReplaceSlot(int slot, int expected, int hero, int request)
	{
		int num = roster[slot];
		bool flag = DeploymentRoster.TryReplace(roster, slot, expected, hero, LockedSlot(), HeroAvailable, out var reason);
		if (flag && num != roster[slot])
		{
			rosterRevision++;
			localReady = (peerReady = false);
			localReadyRevision = (peerReadyRevision = 0);
			ApplyRoster(localUi);
			closePopup = true;
			StatusText = "名单已更新，双方请按新名单准备";
		}
		else if (!flag)
		{
			StatusText = reason;
		}
		BroadcastRoster(request, flag, reason);
		Plugin.Log.LogInfo((object)("[部署选人] 主机裁决 request=" + request + " 槽位=" + slot + " 英雄=" + hero + " 接受=" + flag + " 修订=" + rosterRevision + " " + reason));
	}

	private static void BroadcastRoster(int request, bool accepted, string reason)
	{
		if (Session.IsHost && roster != null && !CampaignSync.ClosingBattle)
		{
			DeploymentRoster.Message message = new DeploymentRoster.Message
			{
				State = true,
				Epoch = epoch,
				Level = levelIndex,
				Request = request,
				Revision = rosterRevision,
				Accepted = accepted,
				Reason = reason,
				Heroes = roster
			};
			using (MessageWriter messageWriter = Protocol.Begin(MsgType.DeployRoster))
			{
				message.Write(messageWriter);
				Session.Send(messageWriter.ToArray());
			}
			nextRosterSend = Time.unscaledTime + 1f;
		}
	}

	private static void ReceiveRoster(BinaryReader reader)
	{
		DeploymentRoster.Message message = DeploymentRoster.Message.Read(reader);
		reader.RequireEnd();
		if (!CommandIdentity.Matches(message.Epoch, epoch, message.Level, levelIndex, CampaignSync.ClosingBattle))
		{
			return;
		}
		if (Session.IsHost && !message.State)
		{
			if (roster == null || !global::UnityObjectOps.Truthy((Object)(object)localUi))
			{
				return;
			}
			if (committed || message.Request <= lastRequest)
			{
				BroadcastRoster(message.Request, accepted: false, "名单已锁定或请求已处理");
				return;
			}
			lastRequest = message.Request;
			if (message.Slot >= roster.Length)
			{
				BroadcastRoster(message.Request, accepted: false, "该部署槽位不存在");
			}
			else
			{
				ReplaceSlot(message.Slot, message.Expected, message.Hero, message.Request);
			}
		}
		else
		{
			if (!Session.IsClient || !message.State)
			{
				return;
			}
			int num = -1;
			foreach (KeyValuePair<int, int> pendingChange in pendingChanges)
			{
				if (pendingChange.Value == message.Request)
				{
					num = pendingChange.Key;
					break;
				}
			}
			if (num >= 0)
			{
				pendingChanges.Remove(num);
			}
			if (!committed && message.Revision >= rosterRevision)
			{
				bool flag = message.Revision > rosterRevision;
				roster = message.Heroes;
				rosterRevision = message.Revision;
				if (flag)
				{
					localReady = (peerReady = false);
					localReadyRevision = (peerReadyRevision = 0);
					pendingRoster = true;
					closePopup = true;
				}
				if ((message.Request > 0) | flag)
				{
					StatusText = (message.Accepted ? "共享名单已确认，请按当前名单准备" : message.Reason);
					Plugin.Log.LogInfo((object)("[部署选人] 客户端收到 request=" + message.Request + " 接受=" + message.Accepted + " 修订=" + rosterRevision + " 名单=" + RosterText() + " " + message.Reason));
				}
			}
		}
	}

	private static void SendReady()
	{
		using MessageWriter messageWriter = Protocol.Begin(MsgType.DeployReady);
		messageWriter.Write(14);
		messageWriter.Write(epoch);
		messageWriter.Write(levelIndex);
		messageWriter.Write(localReadyRevision);
		Session.Send(messageWriter.ToArray());
	}

	private static string RosterText()
	{
		return string.Join(",", Array.ConvertAll(roster, (int id) => id.ToString()));
	}

	public static void NotifyDeployFinished()
	{
		if (Session.Active && InLevel() && !CampaignSync.ClosingBattle && committed && !localFinished)
		{
			localFinished = true;
			StatusText = "本机部署完成，等待另一端";
			Plugin.Log.LogInfo((object)("[部署] 本机部署完成，岛屿 " + levelIndex));
			if (Session.IsClient)
			{
				MessageWriter messageWriter = Protocol.Begin(MsgType.DeployFinished);
				messageWriter.Write(14);
				messageWriter.Write(epoch);
				messageWriter.Write(levelIndex);
				Session.Send(messageWriter.ToArray());
			}
			else
			{
				TryStartFirstWave();
			}
		}
	}

	private static void ReceiveFinished(BinaryReader reader)
	{
		if (reader.ReadInt32() != 14)
		{
			throw new IOException("部署完成版本不一致");
		}
		int num = reader.ReadInt32();
		int level = reader.ReadInt32();
		reader.RequireEnd();
		if (Session.IsHost && CommandIdentity.Matches(num, epoch, level, levelIndex, CampaignSync.ClosingBattle) && committed)
		{
			peerFinished = true;
			Plugin.Log.LogInfo((object)("[部署] 客户端部署完成，岛屿 " + level));
			TryStartFirstWave();
		}
	}

	private static void TryStartFirstWave()
	{
		if (Session.IsHost && !CampaignSync.ClosingBattle && localFinished && peerFinished && !waveStarted)
		{
			IslandGameplayManager instance = Singleton<IslandGameplayManager>.instance;
			if (instance.states.Prepare.active || hostWaveRequested)
			{
				instance.CallFirstWave();
			}
		}
	}

	public static bool BeforeFirstWave()
	{
		if (CampaignSync.IsRemoteCampaign && !Session.IsClient)
		{
			return false;
		}
		if (!Session.Active || !InLevel())
		{
			return true;
		}
		if (CampaignSync.ClosingBattle)
		{
			return false;
		}
		if (Session.IsClient)
		{
			return applyingWave;
		}
		if (waveStarted)
		{
			return false;
		}
		if (!localFinished || !peerFinished)
		{
			hostWaveRequested = true;
			return false;
		}
		if (!AgentAuthority.PrepareBattle())
		{
			hostWaveRequested = true;
			StatusText = "部署完成，等待地图与人物表现校验";
			return false;
		}
		MessageWriter messageWriter = Protocol.Begin(MsgType.FirstWave);
		messageWriter.Write(14);
		messageWriter.Write(epoch);
		messageWriter.Write(levelIndex);
		if (!Session.Send(messageWriter.ToArray()))
		{
			return false;
		}
		waveStarted = true;
		StatusText = "战斗进行中";
		Plugin.Log.LogInfo((object)("[部署] 主机开启第一波，岛屿 " + levelIndex));
		return true;
	}

	private static void ReceiveFirstWave(BinaryReader reader)
	{
		if (reader.ReadInt32() != 14)
		{
			throw new IOException("第一波消息版本不一致");
		}
		int num = reader.ReadInt32();
		int level = reader.ReadInt32();
		reader.RequireEnd();
		if (Session.IsClient && CommandIdentity.Matches(num, epoch, level, levelIndex, CampaignSync.ClosingBattle) && committed && !waveStarted)
		{
			pendingWave = true;
		}
	}

	private static void ApplyClientFirstWave()
	{
		if (localFinished)
		{
			pendingWave = false;
			applyingWave = true;
			try
			{
				Singleton<IslandGameplayManager>.instance.CallFirstWave();
			}
			finally
			{
				applyingWave = false;
			}
			waveStarted = true;
			StatusText = "战斗进行中";
			Plugin.Log.LogInfo((object)"[部署] 客户端按主机命令开启第一波");
		}
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
