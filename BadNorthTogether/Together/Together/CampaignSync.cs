using System;
using System.Collections;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using RTM.UISystem;
using Together.Net;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.CampaignGeneration;
using Voxels.TowerDefense.ProfileInternals;
using Voxels.TowerDefense.UI;

namespace Together;

internal static class CampaignSync
{
	private sealed class Transfer
	{
		public MsgType Type;

		public int Id;

		public int LevelIndex;

		public int Epoch;

		public int CampaignId;

		public int Revision;

		public int Transition;

		public int Offset;

		public byte[] Data;
	}

	private const int ChunkSize = 8192;

	private const int ChunksPerTick = 4;

	private const int MaxCampaignBytes = 33554432;

	private static bool initialized;

	private static bool loading;

	private static bool awaitingLevelEntry;

	private static bool warnedActiveCampaign;

	private static bool clientCleanupPending;

	private static bool hostLocalReady;

	private static bool hostPeerReady;

	private static bool transitionSnapshotSent;

	private static int nextTransferId;

	private static int lastCompletedTransfer;

	private static int loadGeneration;

	private static int nextBattleEpoch;

	private static int nextCampaignId;

	private static int campaignId;

	private static int revision;

	private static int appliedRevision;

	private static int transitionId;

	private static int acknowledgedRevision;

	private static int closingCampaignId;

	private static int closingEpoch;

	private static CampaignExit exitKind;

	private static CampaignSave hostCampaign;

	private static CampaignSave remoteCampaign;

	private static CampaignSaveMeta remoteMeta;

	private static CampaignSaveMeta originalCampaignMeta;

	private static UserSave originalUserSave;

	private static UserSave isolatedUserSave;

	private static Transfer outgoing;

	private static Transfer latestState;

	private static Transfer incoming;

	private static Transfer pendingStart;

	private static Transfer pendingState;

	private static LevelNode queuedHostLevel;

	private static int queuedReplayLevel = -1;

	private static IEnumerator silentApply;

	private static Action silentComplete;

	private static float transitionStarted;

	private static float cleanupFinished;

	public static string StatusText { get; private set; } = "未连接";

	public static int CurrentBattleEpoch { get; private set; }

	public static bool ClosingBattle { get; private set; }

	public static bool DecodingSnapshot { get; private set; }

	public static bool IsRemoteCampaign
	{
		get
		{
			if (remoteCampaign != null)
			{
				return Profile.campaign == remoteCampaign;
			}
			return false;
		}
	}

	public static bool HasIsolatedUserSave
	{
		get
		{
			if (isolatedUserSave != null)
			{
				return Profile.userSave == isolatedUserSave;
			}
			return false;
		}
	}

	public static bool ClientDecisionsBlocked
	{
		get
		{
			if (!Session.IsClient && !IsRemoteCampaign && !HasIsolatedUserSave)
			{
				return DecodingSnapshot;
			}
			return true;
		}
	}

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			Session.On(MsgType.CampaignStart, delegate(BinaryReader reader)
			{
				Receive(MsgType.CampaignStart, reader);
			});
			Session.On(MsgType.CampaignState, delegate(BinaryReader reader)
			{
				Receive(MsgType.CampaignState, reader);
			});
			Session.On(MsgType.CampaignControl, ReceiveControl);
			Session.On(MsgType.CampaignReady, ReceiveReady);
			Session.Connected += delegate
			{
				hostCampaign = null;
				StatusText = (Session.IsHost ? "等待选择岛屿" : "等待主机战役");
			};
			Session.Stopped += Reset;
		}
	}

	private static void Reset()
	{
		loadGeneration++;
		outgoing = (latestState = (incoming = (pendingStart = (pendingState = null))));
		StopSilentApply();
		loading = (awaitingLevelEntry = (warnedActiveCampaign = (clientCleanupPending = false)));
		hostLocalReady = (hostPeerReady = (transitionSnapshotSent = false));
		lastCompletedTransfer = (campaignId = (revision = (appliedRevision = (transitionId = (acknowledgedRevision = (CurrentBattleEpoch = 0))))));
		ClosingBattle = (DecodingSnapshot = false);
		hostCampaign = null;
		queuedHostLevel = null;
		queuedReplayLevel = -1;
		RestoreLocalProfileIfIdle();
		StatusText = "未连接";
	}

	private static void EnsureHostCampaign(CampaignSave save)
	{
		if (hostCampaign != save)
		{
			hostCampaign = save;
			campaignId = ++nextCampaignId;
			revision = 0;
			Plugin.Log.LogInfo((object)("[战役] 主机战役身份 " + campaignId + "，种子 " + save.seed));
		}
	}

	public static bool BeforePlay(LevelNode level)
	{
		if (Session.ApplyingRemote)
		{
			return true;
		}
		if (IsRemoteCampaign && !Session.IsClient)
		{
			Plugin.Log.LogWarning((object)"[战役] 断线后的临时战役不能继续选岛，请返回主菜单");
			return false;
		}
		if (!Session.Active)
		{
			return true;
		}
		if (Session.IsClient)
		{
			Plugin.Log.LogWarning((object)"[战役] 客户端等待主机选岛");
			return false;
		}
		if (ClosingBattle)
		{
			queuedHostLevel = level;
			StatusText = "等待客户端地图确认后进入下一岛";
			return false;
		}
		SendCampaignStart(level);
		return outgoing != null;
	}

	public static void SendCampaignStart(LevelNode level)
	{
		if (Session.IsHost && global::UnityObjectOps.Truthy((Object)(object)level) && global::UnityObjectOps.Truthy((Object)(object)((CampaignComponent)level).campaign) && ((CampaignComponent)level).campaign.campaignSave != null)
		{
			CampaignSave campaignSave = ((CampaignComponent)level).campaign.campaignSave;
			EnsureHostCampaign(campaignSave);
			if (nextBattleEpoch == int.MaxValue)
			{
				Session.Fail("战斗编号耗尽，请重启联机");
				return;
			}
			CurrentBattleEpoch = ++nextBattleEpoch;
			campaignSave.lastPlayedLevelIdx = level.index;
			outgoing = Serialize(MsgType.CampaignStart, campaignSave, level.index, 0);
			latestState = null;
		}
	}

	public static void SendCampaignState()
	{
		if (Session.IsHost && Profile.campaign != null && Profile.campaign.levelStates != null && !ClosingBattle)
		{
			EnsureHostCampaign(Profile.campaign);
			latestState = Serialize(MsgType.CampaignState, Profile.campaign, -1, 0);
		}
	}

	public static bool BeforeLeave(CampaignExit kind)
	{
		if (!Session.Active || Session.ApplyingRemote)
		{
			return true;
		}
		if (Session.IsClient)
		{
			Plugin.Log.LogWarning((object)"[战役] 结束/退出/重玩由主机决定");
			return false;
		}
		if (ClosingBattle)
		{
			return false;
		}
		if (Profile.campaign == null)
		{
			return true;
		}
		EnsureHostCampaign(Profile.campaign);
		ClosingBattle = true;
		exitKind = kind;
		transitionId++;
		transitionStarted = Time.realtimeSinceStartup;
		closingCampaignId = campaignId;
		closingEpoch = CurrentBattleEpoch;
		hostLocalReady = (hostPeerReady = (transitionSnapshotSent = false));
		acknowledgedRevision = 0;
		outgoing = (latestState = null);
		queuedHostLevel = null;
		queuedReplayLevel = ((kind == CampaignExit.Replay) ? Profile.campaign.lastPlayedLevelIdx : (-1));
		MessageWriter messageWriter = Protocol.Begin(MsgType.CampaignControl);
		messageWriter.Write(14);
		messageWriter.Write(transitionId);
		messageWriter.Write(campaignId);
		messageWriter.Write(CurrentBattleEpoch);
		messageWriter.Write((byte)kind);
		messageWriter.Write(Profile.campaign.lastPlayedLevelIdx);
		Session.Send(messageWriter.ToArray());
		StatusText = "正在结束战斗，等待双端地图";
		Plugin.Log.LogInfo((object)("[战役] 主机过渡 #" + transitionId + "，战斗 " + CurrentBattleEpoch + "，方式 " + kind));
		if (kind != CampaignExit.Replay)
		{
			return true;
		}
		BeginHostReplayCleanup();
		return false;
	}

	private static void BeginHostReplayCleanup()
	{
		ITransport transport = Session.Transport;
		int generation = ++loadGeneration;
		loading = true;
		LoadingScreen.BeginLoadingPhase("正在重玩联机岛屿...", (Action)delegate
		{
			if (generation == loadGeneration && transport == Session.Transport && Session.IsHost)
			{
				loading = false;
				Singleton<Voxels.TowerDefense.Stack>.instance.stateCampaign.SetActive(true);
			}
		}, new IEnumerator[1] { Drain(Singleton<IslandGameplayManager>.instance.LeaveIslandRoutine(true), transport, generation) });
	}

	private static void ReceiveControl(BinaryReader reader)
	{
		int num = reader.ReadInt32();
		int num2 = reader.ReadInt32();
		int num3 = reader.ReadInt32();
		int num4 = reader.ReadInt32();
		byte b = reader.ReadByte();
		int num5 = reader.ReadInt32();
		reader.RequireEnd();
		if (num != 14 || num2 <= 0 || num3 <= 0 || num4 < 0 || b > 5 || num5 < 0)
		{
			throw new IOException("战役控制标头非法");
		}
		if (Session.IsClient && num2 > transitionId && num3 >= campaignId && (campaignId != num3 || num4 >= CurrentBattleEpoch))
		{
			transitionId = num2;
			exitKind = (CampaignExit)b;
			closingCampaignId = num3;
			closingEpoch = num4;
			ClosingBattle = true;
			clientCleanupPending = true;
			awaitingLevelEntry = false;
			transitionStarted = Time.realtimeSinceStartup;
			cleanupFinished = 0f;
			pendingStart = (pendingState = null);
			Plugin.Log.LogInfo((object)("[战役] 收到过渡 #" + num2 + "，战斗 " + num4 + "，方式 " + exitKind));
		}
	}

	private static void ReceiveReady(BinaryReader reader)
	{
		int num = reader.ReadInt32();
		int num2 = reader.ReadInt32();
		int num3 = reader.ReadInt32();
		int num4 = reader.ReadInt32();
		int num5 = reader.ReadInt32();
		byte b = reader.ReadByte();
		reader.RequireEnd();
		if (num != 14 || num2 < 0 || num3 <= 0 || num4 < 0 || num5 < 0 || b > 2)
		{
			throw new IOException("战役确认标头非法");
		}
		if (Session.IsHost && ClosingBattle && CampaignFence.ReadyMatches(num2, num3, num4, num5, transitionId, campaignId, CurrentBattleEpoch, (exitKind != CampaignExit.MainMenu) ? acknowledgedRevision : 0) && ((exitKind == CampaignExit.MainMenu && b == 2) || (b == 0 && transitionSnapshotSent && num5 == acknowledgedRevision)))
		{
			hostPeerReady = true;
			Plugin.Log.LogInfo((object)("[战役] 客户端确认过渡 #" + num2 + "，修订 " + num5));
		}
	}

	private static void SendReady(int transition, int identity, int battle, int readyRevision, byte kind)
	{
		MessageWriter messageWriter = Protocol.Begin(MsgType.CampaignReady);
		messageWriter.Write(14);
		messageWriter.Write(transition);
		messageWriter.Write(identity);
		messageWriter.Write(battle);
		messageWriter.Write(readyRevision);
		messageWriter.Write(kind);
		Session.Send(messageWriter.ToArray());
	}

	public static void Tick()
	{
		RestoreLocalProfileIfIdle();
		if (Session.IsHost)
		{
			TickHostTransition();
			if (!ClosingBattle && Profile.campaign != null && Profile.campaign != hostCampaign)
			{
				EnsureHostCampaign(Profile.campaign);
				SendCampaignState();
			}
			SendChunks();
		}
		else if (Session.IsClient)
		{
			TickSilentApply();
			if (clientCleanupPending && !loading && !Singleton<Voxels.TowerDefense.Stack>.instance.stateLoading.active)
			{
				BeginClientCleanup();
			}
			TryApply();
		}
	}

	private static void TickHostTransition()
	{
		if (!ClosingBattle || loading)
		{
			return;
		}
		Voxels.TowerDefense.Stack instance = Singleton<Voxels.TowerDefense.Stack>.instance;
		IslandGameplayManager instance2 = Singleton<IslandGameplayManager>.instance;
		if (!global::UnityObjectOps.Truthy((Object)(object)instance) || instance.stateLoading.active || (global::UnityObjectOps.Truthy((Object)(object)instance2) && global::UnityObjectOps.Truthy((Object)(object)instance2.island)))
		{
			return;
		}
		if (exitKind == CampaignExit.MainMenu)
		{
			if (Profile.campaign != null)
			{
				return;
			}
			hostLocalReady = true;
		}
		else
		{
			if (Profile.campaign == null || !instance.stateCampaign.active)
			{
				return;
			}
			CampaignMapUI componentInChildren = ((Component)Singleton<CampaignManager>.instance).GetComponentInChildren<CampaignMapUI>(true);
			if (global::UnityObjectOps.Truthy((Object)(object)componentInChildren) && ((UIMenu)componentInChildren).blockingInput)
			{
				return;
			}
			hostLocalReady = true;
			if (!transitionSnapshotSent)
			{
				EnsureHostCampaign(Profile.campaign);
				latestState = Serialize(MsgType.CampaignState, Profile.campaign, -1, transitionId);
				if (latestState == null)
				{
					return;
				}
				acknowledgedRevision = latestState.Revision;
				transitionSnapshotSent = true;
				Plugin.Log.LogInfo((object)("[战役耗时] 主机结算就绪 过渡=" + transitionId + " 秒=" + (Time.realtimeSinceStartup - transitionStarted).ToString("F2")));
			}
		}
		if (!hostLocalReady || !hostPeerReady)
		{
			return;
		}
		ClosingBattle = false;
		StatusText = "双端地图已就绪";
		Plugin.Log.LogInfo((object)("[战役] 过渡 #" + transitionId + " 已完成，可进入下一岛"));
		LevelNode val = queuedHostLevel;
		queuedHostLevel = null;
		if (!global::UnityObjectOps.Truthy((Object)(object)val) && queuedReplayLevel >= 0)
		{
			CampaignManager instance3 = Singleton<CampaignManager>.instance;
			if (global::UnityObjectOps.Truthy((Object)(object)instance3) && global::UnityObjectOps.Truthy((Object)(object)instance3.campaign) && queuedReplayLevel < instance3.campaign.levels.Count)
			{
				val = instance3.campaign.levels[queuedReplayLevel];
			}
		}
		queuedReplayLevel = -1;
		if (global::UnityObjectOps.Truthy((Object)(object)val))
		{
			val.Play();
		}
	}

	private static void BeginClientCleanup()
	{
		CampaignProgress.PrepareClientMapForChange();
		clientCleanupPending = false;
		loading = true;
		int generation = ++loadGeneration;
		ITransport transport = Session.Transport;
		int transition = transitionId;
		int identity = closingCampaignId;
		int battle = closingEpoch;
		float started = Time.realtimeSinceStartup;
		LoadingScreen.BeginLoadingPhase("正在同步联机战役过渡...", (Action)delegate
		{
			if (generation == loadGeneration && transport == Session.Transport && Session.IsClient)
			{
				loading = false;
				cleanupFinished = Time.realtimeSinceStartup;
				Plugin.Log.LogInfo((object)("[战役耗时] 客户端清场 过渡=" + transition + " 秒=" + (cleanupFinished - started).ToString("F2")));
				if (exitKind == CampaignExit.MainMenu)
				{
					Singleton<Voxels.TowerDefense.Stack>.instance.stateMeta.SetActive(true);
					SendReady(transition, identity, battle, 0, 2);
					ClosingBattle = false;
					RestoreLocalProfileIfIdle();
				}
			}
		}, new IEnumerator[1] { ClientCleanup(transport, generation) });
	}

	private static IEnumerator ClientCleanup(ITransport transport, int generation)
	{
		IslandGameplayManager instance = Singleton<IslandGameplayManager>.instance;
		if (global::UnityObjectOps.Truthy((Object)(object)instance) && global::UnityObjectOps.Truthy((Object)(object)instance.island))
		{
			IEnumerator leave = instance.LeaveIslandRoutine(false);
			while (ValidLoad(transport, generation) && leave.MoveNext())
			{
				yield return null;
			}
		}
		if (!ValidLoad(transport, generation))
		{
			yield break;
		}
		Singleton<Voxels.TowerDefense.Stack>.instance.stateLevel.SetActive(false);
		if (exitKind == CampaignExit.MainMenu)
		{
			IEnumerator leave = MetaMenuHelpers.ExitToMainMenuRoutine();
			while (ValidLoad(transport, generation) && leave.MoveNext())
			{
				yield return null;
			}
		}
	}

	private static bool ValidLoad(ITransport transport, int generation)
	{
		if (generation == loadGeneration && Session.Transport == transport)
		{
			return Session.Active;
		}
		return false;
	}

	private static IEnumerator Drain(IEnumerator routine, ITransport transport, int generation)
	{
		while (ValidLoad(transport, generation) && routine.MoveNext())
		{
			yield return null;
		}
	}

	private static Transfer Serialize(MsgType type, CampaignSave save, int levelIndex, int transition)
	{
		try
		{
			byte[] array;
			using (MemoryStream memoryStream = new MemoryStream())
			{
				new BinaryFormatter().Serialize(memoryStream, save);
				array = memoryStream.ToArray();
			}
			if (array.Length == 0 || array.Length > 33554432)
			{
				throw new IOException("CampaignSave 大小超限");
			}
			Transfer transfer = new Transfer
			{
				Type = type,
				Id = ++nextTransferId,
				LevelIndex = levelIndex,
				Epoch = CurrentBattleEpoch,
				CampaignId = campaignId,
				Revision = ++revision,
				Transition = transition,
				Data = array
			};
			StatusText = "正在发送战役修订 " + revision;
			Plugin.Log.LogInfo((object)("[战役] 排队 " + type.ToString() + " #" + transfer.Id + "，战役 " + campaignId + "，修订 " + revision + "，战斗 " + CurrentBattleEpoch + "，过渡 " + transition + "，字节=" + array.Length));
			return transfer;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError((object)("[战役] 序列化失败：" + ex));
			Session.Fail("战役序列化失败：" + ex.Message);
			return null;
		}
	}

	private static void SendChunks()
	{
		for (int i = 0; i < 4; i++)
		{
			if (outgoing == null)
			{
				outgoing = latestState;
				latestState = null;
				if (outgoing == null)
				{
					break;
				}
			}
			int num = Math.Min(8192, outgoing.Data.Length - outgoing.Offset);
			MessageWriter messageWriter = Protocol.Begin(outgoing.Type);
			messageWriter.Write(14);
			messageWriter.Write(outgoing.Id);
			messageWriter.Write(outgoing.Data.Length);
			messageWriter.Write(outgoing.Offset);
			messageWriter.Write(outgoing.LevelIndex);
			messageWriter.Write(outgoing.Epoch);
			messageWriter.Write(outgoing.CampaignId);
			messageWriter.Write(outgoing.Revision);
			messageWriter.Write(outgoing.Transition);
			messageWriter.Write(num);
			messageWriter.Write(outgoing.Data, outgoing.Offset, num);
			if (!Session.Send(messageWriter.ToArray()))
			{
				break;
			}
			outgoing.Offset += num;
			if (outgoing.Offset == outgoing.Data.Length)
			{
				outgoing = null;
			}
		}
	}

	private static void Receive(MsgType type, BinaryReader reader)
	{
		if (!Session.IsClient)
		{
			return;
		}
		int num = reader.ReadInt32();
		int num2 = reader.ReadInt32();
		int num3 = reader.ReadInt32();
		int num4 = reader.ReadInt32();
		int num5 = reader.ReadInt32();
		int num6 = reader.ReadInt32();
		int num7 = reader.ReadInt32();
		int num8 = reader.ReadInt32();
		int num9 = reader.ReadInt32();
		int num10 = reader.ReadInt32();
		if (num != 14 || num2 <= 0 || num7 <= 0 || num8 <= 0 || num9 < 0 || num6 < 0 || (type == MsgType.CampaignStart && (num6 <= 0 || num5 < 0)) || num3 <= 0 || num3 > 33554432 || num10 <= 0 || num10 > 8192 || num4 < 0 || num4 > num3 - num10)
		{
			throw new IOException("战役分片标头无效");
		}
		byte[] array = reader.ReadBytes(num10);
		if (array.Length != num10)
		{
			throw new IOException("战役分片不完整");
		}
		reader.RequireEnd();
		if (num2 <= lastCompletedTransfer || num7 < campaignId || (num7 == campaignId && num8 <= appliedRevision) || (ClosingBattle && ((type == MsgType.CampaignStart && num6 <= closingEpoch) || (type == MsgType.CampaignState && num9 != transitionId))))
		{
			return;
		}
		if (incoming == null || incoming.Id != num2)
		{
			if (incoming != null && num2 < incoming.Id)
			{
				return;
			}
			if (num4 != 0)
			{
				throw new IOException("缺少战役分片开头");
			}
			incoming = new Transfer
			{
				Type = type,
				Id = num2,
				LevelIndex = num5,
				Epoch = num6,
				CampaignId = num7,
				Revision = num8,
				Transition = num9,
				Data = new byte[num3]
			};
		}
		if (incoming.Type != type || incoming.Data.Length != num3 || incoming.LevelIndex != num5 || incoming.Epoch != num6 || incoming.CampaignId != num7 || incoming.Revision != num8 || incoming.Transition != num9 || incoming.Offset != num4)
		{
			throw new IOException("战役分片顺序无效");
		}
		Buffer.BlockCopy(array, 0, incoming.Data, num4, num10);
		incoming.Offset += num10;
		if (incoming.Offset == num3)
		{
			Transfer transfer = incoming;
			lastCompletedTransfer = num2;
			incoming = null;
			if (type == MsgType.CampaignStart)
			{
				pendingStart = transfer;
				pendingState = null;
			}
			else
			{
				pendingState = transfer;
			}
			StatusText = "战役修订已接收";
		}
	}

	private static void TryApply()
	{
		//IL_018e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0194: Expected O, but got Unknown
		if (loading || clientCleanupPending || (pendingStart == null && pendingState == null))
		{
			return;
		}
		Voxels.TowerDefense.Stack instance = Singleton<Voxels.TowerDefense.Stack>.instance;
		if (!global::UnityObjectOps.Truthy((Object)(object)instance) || instance.stateLoading.active)
		{
			return;
		}
		if (awaitingLevelEntry)
		{
			if (instance.stateLevel.active)
			{
				awaitingLevelEntry = false;
			}
		}
		else
		{
			if (instance.stateLevel.active || Profile.meta == null || Profile.userSave == null)
			{
				return;
			}
			if (!IsRemoteCampaign && Profile.campaign != null)
			{
				if (!warnedActiveCampaign)
				{
					warnedActiveCampaign = true;
					StatusText = "请先退出当前单人战役";
					Plugin.Log.LogWarning((object)"[战役] 客户端需先退出当前单人战役");
				}
				return;
			}
			Transfer transfer = pendingStart ?? pendingState;
			if (!CampaignFence.AcceptRevision(transfer.CampaignId, transfer.Revision, transfer.Epoch, transfer.Transition, campaignId, appliedRevision, CurrentBattleEpoch, transitionId, ClosingBattle, transfer.Type == MsgType.CampaignStart))
			{
				if (transfer == pendingStart)
				{
					pendingStart = null;
				}
				else
				{
					pendingState = null;
				}
				return;
			}
			if (transfer.Type == MsgType.CampaignStart)
			{
				pendingStart = null;
			}
			else
			{
				pendingState = null;
			}
			CampaignSave val = null;
			try
			{
				DecodingSnapshot = true;
				using (MemoryStream serializationStream = new MemoryStream(transfer.Data, writable: false))
				{
					val = (CampaignSave)new BinaryFormatter
					{
						Binder = new CampaignTypeBinder(typeof(CampaignSave))
					}.Deserialize(serializationStream);
				}
				if (val.serializedVersion != 19 || val.levelStates == null || val.levelStates.Count < 2 || val.levelStates.Count > 4096 || val.heroes == null || val.heroes.Count > 256 || val.stats == null || val.inventory == null || val.deployOrder == null || val.oldDeployOrder == null || (transfer.Type == MsgType.CampaignStart && transfer.LevelIndex >= val.levelStates.Count))
				{
					throw new IOException("CampaignSave 内容无效");
				}
				val.loadTime = Time.unscaledTime;
			}
			catch (Exception ex)
			{
				if (val != null)
				{
					val.Unload();
				}
				Plugin.Log.LogError((object)("[战役] 解析失败：" + ex));
				Session.Fail("主机战役无法解析：" + ex.Message);
				return;
			}
			finally
			{
				DecodingSnapshot = false;
			}
			if (transfer.Type == MsgType.CampaignStart)
			{
				CampaignProgress.PrepareClientMapForChange();
			}
			loading = true;
			int generation = ++loadGeneration;
			bool loaded = false;
			ITransport transport = Session.Transport;
			if (transfer.Transition > 0 && cleanupFinished > 0f)
			{
				Plugin.Log.LogInfo((object)("[战役耗时] 客户端清场后等待进度 过渡=" + transfer.Transition + " 秒=" + (Time.realtimeSinceStartup - cleanupFinished).ToString("F2")));
			}
			Action action = delegate
			{
				if (ValidLoad(transport, generation) && Session.IsClient)
				{
					loading = false;
					if (loaded && (!ClosingBattle || transfer.Transition == transitionId))
					{
						campaignId = transfer.CampaignId;
						appliedRevision = transfer.Revision;
						CurrentBattleEpoch = transfer.Epoch;
						remoteMeta.Refresh(remoteCampaign, false);
						StatusText = "战役已同步，修订 " + appliedRevision;
						if (transfer.Type == MsgType.CampaignStart)
						{
							ClosingBattle = false;
							EnterClientLevel(transfer.LevelIndex);
						}
						else
						{
							Singleton<Voxels.TowerDefense.Stack>.instance.stateCampaign.SetActive(true);
							CampaignProgress.RefreshMap(Singleton<CampaignManager>.instance.campaign);
							if (transfer.Transition > 0)
							{
								SendReady(transfer.Transition, campaignId, CurrentBattleEpoch, appliedRevision, 0);
								ClosingBattle = false;
								Plugin.Log.LogInfo((object)("[战役耗时] 客户端返回地图完成 过渡=" + transfer.Transition + " 总秒=" + (Time.realtimeSinceStartup - transitionStarted).ToString("F2")));
							}
						}
					}
				}
			};
			IEnumerator enumerator = ApplyCampaign(val, transfer.CampaignId, transport, generation, delegate
			{
				loaded = true;
			});
			CampaignManager instance2 = Singleton<CampaignManager>.instance;
			if (transfer.Type == MsgType.CampaignState && transfer.CampaignId == campaignId && IsRemoteCampaign && global::UnityObjectOps.Truthy((Object)(object)instance2) && CampaignProgress.RuntimeCompatible(instance2.campaign, val))
			{
				silentApply = enumerator;
				silentComplete = action;
				TickSilentApply();
			}
			else
			{
				LoadingScreen.BeginLoadingPhase("正在同步联机战役...", action, new IEnumerator[1] { enumerator });
			}
		}
	}

	private static void TickSilentApply()
	{
		if (silentApply == null)
		{
			return;
		}
		try
		{
			if (!silentApply.MoveNext())
			{
				Action action = silentComplete;
				StopSilentApply();
				action?.Invoke();
			}
		}
		catch (Exception ex)
		{
			StopSilentApply();
			loading = false;
			Plugin.Log.LogError((object)("[战役] 原地更新失败：" + ex));
			Session.Fail("战役进度更新失败：" + ex.Message);
		}
	}

	private static void StopSilentApply()
	{
		IEnumerator enumerator = silentApply;
		silentApply = null;
		silentComplete = null;
		if (enumerator is IDisposable disposable)
		{
			disposable.Dispose();
		}
	}

	private static void EnterClientLevel(int index)
	{
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		CampaignManager instance = Singleton<CampaignManager>.instance;
		if (!global::UnityObjectOps.Truthy((Object)(object)instance) || !global::UnityObjectOps.Truthy((Object)(object)instance.campaign) || index >= instance.campaign.levels.Count)
		{
			Session.Fail("客户端找不到主机选定岛屿");
			return;
		}
		bool applyingRemote = Session.ApplyingRemote;
		Session.ApplyingRemote = true;
		try
		{
			LevelNode obj = instance.campaign.levels[index];
			IslandProxy islandProxy = obj.islandProxy;
			islandProxy.preparingToPlay = true;
			islandProxy.UpdateBuildPriority();
			if ((int)islandProxy.state == 0)
			{
				islandProxy.GenerateIsland();
			}
			awaitingLevelEntry = true;
			obj.Play();
		}
		catch (Exception ex)
		{
			awaitingLevelEntry = false;
			Session.Fail("客户端进入岛屿失败：" + ex.Message);
		}
		finally
		{
			Session.ApplyingRemote = applyingRemote;
		}
	}

	public static bool ClientLoadCallbackAllowed(LevelNode level)
	{
		if (Session.Active && ClosingBattle)
		{
			return false;
		}
		if (!Session.IsClient && !IsRemoteCampaign && !HasIsolatedUserSave)
		{
			return true;
		}
		if (Session.IsClient && IsRemoteCampaign && CurrentBattleEpoch > 0)
		{
			return level.index == Profile.campaign.lastPlayedLevelIdx;
		}
		return false;
	}

	private static IEnumerator ApplyCampaign(CampaignSave save, int identity, ITransport transport, int generation, Action onLoaded)
	{
		bool completed = false;
		bool adopted = false;
		float started = Time.realtimeSinceStartup;
		try
		{
			if (!ValidLoad(transport, generation) || !Session.IsClient)
			{
				yield break;
			}
			CampaignManager instance = Singleton<CampaignManager>.instance;
			string text = ((identity != campaignId || !IsRemoteCampaign) ? "首连或战役身份改变" : CampaignProgress.RuntimeIncompatibility(global::UnityObjectOps.Truthy((Object)(object)instance) ? instance.campaign : null, save));
			if (text == null)
			{
				using (CampaignIslandCache cache = new CampaignIslandCache(instance.islandGenerator))
				{
					cache.InvalidateChanged(instance.campaign, save);
					IEnumerator update = CampaignProgress.Apply(instance.campaign, save);
					using (update as IDisposable)
					{
						while (ValidLoad(transport, generation) && update.MoveNext())
						{
							yield return null;
						}
					}
				}
				if (!ValidLoad(transport, generation))
				{
					yield break;
				}
				Plugin.Log.LogInfo((object)("[战役] 已原地更新战役 " + identity + "，保留英雄/关卡/图集引用，秒=" + (Time.realtimeSinceStartup - started).ToString("F2")));
			}
			else
			{
				Plugin.Log.LogInfo((object)("[战役重建] 战役=" + identity + " 原因=" + text));
				Singleton<Voxels.TowerDefense.Stack>.instance.stateCampaign.SetActive(false);
				if (global::UnityObjectOps.Truthy((Object)(object)instance) && global::UnityObjectOps.Truthy((Object)(object)instance.campaign))
				{
					IEnumerator shutdown = MetaMenuHelpers.ExitToMainMenuRoutine();
					while (ValidLoad(transport, generation) && shutdown.MoveNext())
					{
						yield return null;
					}
				}
				if (!ValidLoad(transport, generation))
				{
					yield break;
				}
				IsolateLocalProfile();
				remoteCampaign = (Profile.campaign = save);
				adopted = true;
				remoteMeta = new CampaignSaveMeta(save);
				remoteMeta.SetSaveSlot(-1);
				remoteMeta.campaignNumber = 0;
				Profile.activeCampaignMeta = remoteMeta;
				IEnumerator update = CampaignManager.GenerateCampaign(save, false);
				while (ValidLoad(transport, generation) && update.MoveNext())
				{
					yield return null;
				}
				if (!ValidLoad(transport, generation))
				{
					yield break;
				}
				Singleton<CampaignManager>.instance.campaign.lastPlayedLevel = null;
				Plugin.Log.LogInfo((object)("[战役] 重建战役 " + identity + "，秒=" + (Time.realtimeSinceStartup - started).ToString("F2")));
			}
			onLoaded();
			completed = true;
		}
		finally
		{
			if (!adopted)
			{
				save.Unload();
			}
			if (!completed && generation == loadGeneration)
			{
				loading = false;
				if (Session.Transport == transport && Session.IsClient)
				{
					Session.Fail("客户端战役加载中断，请返回主菜单后重连");
				}
			}
		}
	}

	private static void IsolateLocalProfile()
	{
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		if (!HasIsolatedUserSave)
		{
			originalUserSave = Profile.userSave;
			originalCampaignMeta = Profile.activeCampaignMeta;
			using (MemoryStream memoryStream = new MemoryStream())
			{
				new BinaryFormatter().Serialize(memoryStream, originalUserSave);
				memoryStream.Position = 0L;
				isolatedUserSave = (UserSave)new BinaryFormatter().Deserialize(memoryStream);
			}
			Profile.userSave = isolatedUserSave;
			Plugin.Log.LogInfo((object)"[战役] 客户端使用独立内存战役/用户统计，禁止写原存档");
		}
	}

	public static void RestoreLocalProfileIfIdle()
	{
		if (!loading && !clientCleanupPending && Profile.campaign == null && isolatedUserSave != null)
		{
			if (Profile.userSave == isolatedUserSave)
			{
				Profile.userSave = originalUserSave;
			}
			if (Profile.activeCampaignMeta == remoteMeta)
			{
				Profile.activeCampaignMeta = originalCampaignMeta;
			}
			isolatedUserSave = (originalUserSave = null);
			remoteCampaign = null;
			remoteMeta = (originalCampaignMeta = null);
			Plugin.Log.LogInfo((object)"[战役] 临时战役释放，恢复本地用户统计");
		}
	}
}
