using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using RTM.Utilities;
using Together.Net;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;
using Voxels.TowerDefense.UI;

namespace Together;

internal static class AgentAuthority
{
	private struct Group
	{
		public int Wave;

		public int Index;
	}

	private struct Key : IEquatable<Key>
	{
		public byte Side;

		public int A;

		public int B;

		public int Ordinal;

		public bool Equals(Key other)
		{
			if (Side == other.Side && A == other.A && B == other.B)
			{
				return Ordinal == other.Ordinal;
			}
			return false;
		}

		public override bool Equals(object other)
		{
			if (other is Key)
			{
				return Equals((Key)other);
			}
			return false;
		}

		public override int GetHashCode()
		{
			return (((((Side * 397) ^ A) * 397) ^ B) * 397) ^ Ordinal;
		}

		public override string ToString()
		{
			return Side + ":" + A + ":" + B + ":" + Ordinal;
		}
	}

	private sealed class Actor
	{
		public Agent Agent;

		public Squad Squad;

		public Key Key;

		public string Prefab;

		public int Id;

		public ActorVisual Visual;

		public VisualEquipment Equipment;

		public bool Announced;

		public bool Removed;

		public byte LastFlags;

		public bool MembershipApplied;

		public byte MembershipFlags;

		public Squad MembershipSquad;

		public Faction MembershipFaction;

		public float NextDormantPose;

		public readonly ActorTimeline<Sample> History = new ActorTimeline<Sample>();
	}

	private sealed class Sample
	{
		public VisualFrame Visual;

		public Vector3 NavPosition;

		public float Health;

		public PathProgress Progress;

		public byte PathKind = byte.MaxValue;

		public int PathId;

		public int PathRevision;

		public float PathTime;

		public byte Flags;
	}

	private sealed class Birth
	{
		public int Id;

		public int Sequence;

		public Key Key;

		public string Prefab;

		public byte[] Layout;

		public float Time;

		public float ReceivedAt;

		public Sample Sample;
	}

	private const int MaxActors = 8192;

	private const byte Spawned = 1;

	private const byte Alive = 2;

	private const byte Grounded = 4;

	private const byte OnMain = 8;

	private const byte Active = 16;

	private static readonly FieldInfo stateActive = AccessTools.Field(typeof(AgentState), "_active");

	private static readonly FieldInfo animationAction = AccessTools.Field(typeof(Agent), "animationAction");

	private static readonly FieldInfo allActiveField = AccessTools.Field(typeof(Agent), "allActive");

	private static readonly Dictionary<Agent, Actor> actors = new Dictionary<Agent, Actor>();

	private static readonly Dictionary<int, Actor> networkActors = new Dictionary<int, Actor>();

	private static readonly Dictionary<Key, Actor> localActors = new Dictionary<Key, Actor>();

	private static readonly Dictionary<Squad, Group> groups = new Dictionary<Squad, Group>();

	private static readonly Dictionary<Squad, int> ordinals = new Dictionary<Squad, int>();

	private static readonly Dictionary<string, Agent> prefabs = new Dictionary<string, Agent>(StringComparer.Ordinal);

	private static readonly Dictionary<Key, Squad> remoteSquads = new Dictionary<Key, Squad>();

	private static readonly Dictionary<int, Birth> pendingBirths = new Dictionary<int, Birth>();

	private static readonly Dictionary<int, float> removals = new Dictionary<int, float>();

	private static readonly RetiredIds retired = new RetiredIds();

	private static readonly List<Actor> actorScratch = new List<Actor>();

	private static readonly List<int> idScratch = new List<int>();

	private static readonly NetworkClock clock = new NetworkClock();

	private static readonly Dictionary<int, LifeCensus.Entry> latestLife = new Dictionary<int, LifeCensus.Entry>();

	private static LifeCensus latestCensus;

	private static float nextCensus;

	private static float nextLifeLog;

	private static float nextMembershipCheck;

	private static float poseLogStart;

	private static int poseSamples;

	private static int skippedPoses;

	private static int posePackets;

	private static int poseRejected;

	private static long poseBytes;

	private static long legacyPoseBytes;

	private static Island island;

	private static int levelIndex = -1;

	private static int nextId;

	private static int nextGroup;

	private static int sequence;

	private static int clockSequence;

	private static int expectedActors;

	private static float originTime;

	private static float nextSnapshot;

	private static float nextClock;

	private static float baselineStarted;

	private static float missingSince;

	private static float nextLayoutCheck;

	private static bool initialized;

	private static bool baselineSent;

	private static bool peerReady;

	private static bool localReady;

	private static bool clientPlayback;

	private static bool failed;

	private static bool applyingResult;

	private static bool sendingFinal;

	private static bool resultSent;

	private static bool resultApplied;

	private static int resultSequence;

	private static int receivedResultSequence;

	private static BattleSummary resultSummary;

	private static EndOfLevel.Reason sentResultReason;

	private static readonly HashSet<EnemyLineup> preparedLineups = new HashSet<EnemyLineup>();

	private static readonly FieldInfo lineupRows = AccessTools.Field(typeof(EnemyLineup), "vikingInfoList");

	private static EndOfLevel.Reason pendingResult;

	private static float resultTime;

	private static List<Agent> AllActive => (List<Agent>)allActiveField.GetValue(null);

	public static string LifeStatus { get; private set; } = "生命状态待校验";

	public static int Epoch { get; private set; }

	public static bool ClientPlayback
	{
		get
		{
			if (clientPlayback)
			{
				return global::UnityObjectOps.Truthy((Object)(object)island);
			}
			return false;
		}
	}

	public static bool Verified => ClientPlayback;

	public static bool HasFailure => failed;

	public static float HostTime
	{
		get
		{
			if (!Session.IsHost)
			{
				return clock.Latest;
			}
			return Mathf.Max(0f, Time.time - originTime);
		}
	}

	public static float PlaybackTime
	{
		get
		{
			if (!ClientPlayback)
			{
				return HostTime;
			}
			return clock.Time;
		}
	}

	public static string StatusText { get; private set; } = "等待进入岛屿";

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			Session.On(MsgType.BattleBaseline, ReceiveBaseline);
			Session.On(MsgType.BattleReady, ReceiveReady);
			Session.On(MsgType.ActorSpawn, ReceiveBirth);
			Session.On(MsgType.ActorPose, ReceivePose);
			Session.On(MsgType.ActorPoses, delegate(BinaryReader reader)
			{
				PoseBatch.Read(reader, ReceivePose);
			});
			Session.On(MsgType.ActorDespawn, ReceiveRemoval);
			Session.On(MsgType.VisualResources, ReceiveResources);
			Session.On(MsgType.BattleClock, ReceiveClock);
			Session.On(MsgType.BattleResult, ReceiveResult);
			Session.On(MsgType.ActorCensus, ReceiveCensus);
			Session.Stopped += OnDisconnected;
		}
	}

	public static void BeginIsland(Island value)
	{
		Reset();
		if (Session.Active)
		{
			island = value;
			levelIndex = value.levelNode.index;
			originTime = Time.time;
			Epoch = CampaignSync.CurrentBattleEpoch;
			if (Epoch <= 0)
			{
				Fail("进入岛屿前未分配战斗编号");
				return;
			}
			Session.ResetOptional();
			DeploySync.BeginIsland(levelIndex);
			WaveSync.BeginIsland();
			WorldAuthority.BeginIsland(value);
			AbilityCommands.BeginIsland(value);
			CombatEvents.BeginIsland(value);
			SurfaceMarks.Reset();
			SceneDecorations.Reset();
			FireVisuals.Reset();
			SceneVisuals.BeginIsland(value);
			StatusText = "部署完成后校验地图与人物表现";
			Plugin.Log.LogInfo((object)("[战斗] 建立岛屿 " + levelIndex + "，战斗实例 " + Epoch));
		}
	}

	public static void Reset()
	{
		//IL_01e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0207: Unknown result type (might be due to invalid IL or missing references)
		foreach (Actor value in actors.Values)
		{
			if (clientPlayback && global::UnityObjectOps.Truthy((Object)(object)value.Agent))
			{
				((Component)value.Agent).gameObject.SetActive(false);
			}
			if (value.Visual != null)
			{
				value.Visual.Dispose();
			}
			if (value.Equipment != null)
			{
				value.Equipment.Dispose();
			}
			value.History.Reset();
		}
		actors.Clear();
		networkActors.Clear();
		localActors.Clear();
		groups.Clear();
		ordinals.Clear();
		prefabs.Clear();
		remoteSquads.Clear();
		pendingBirths.Clear();
		removals.Clear();
		retired.Clear();
		actorScratch.Clear();
		idScratch.Clear();
		clock.Reset();
		VisualResources.Reset();
		ClientSquadFacts.Reset();
		SurfaceMarks.Reset();
		SceneDecorations.Reset();
		FireVisuals.Reset();
		SceneVisuals.Reset();
		poseLogStart = Time.unscaledTime;
		poseSamples = (skippedPoses = (posePackets = (poseRejected = 0)));
		poseBytes = (legacyPoseBytes = 0L);
		latestLife.Clear();
		latestCensus = null;
		nextCensus = (nextLifeLog = (nextMembershipCheck = 0f));
		LifeStatus = "生命状态待校验";
		island = null;
		levelIndex = -1;
		Epoch = (nextId = (nextGroup = (sequence = (clockSequence = (expectedActors = 0)))));
		baselineSent = (peerReady = (localReady = (clientPlayback = (failed = (applyingResult = false)))));
		sendingFinal = (resultSent = false);
		pendingResult = (EndOfLevel.Reason)0;
		resultApplied = false;
		resultSequence = (receivedResultSequence = 0);
		resultSummary = null;
		sentResultReason = (EndOfLevel.Reason)0;
		preparedLineups.Clear();
		originTime = (nextSnapshot = (nextClock = (baselineStarted = (missingSince = (nextLayoutCheck = (resultTime = 0f))))));
		StatusText = "等待进入岛屿";
	}

	private static void OnDisconnected()
	{
		if (ClientPlayback)
		{
			failed = true;
			StatusText = "连接已断开，战斗已冻结；请退出到主菜单";
			PauseForFailure();
		}
		else
		{
			Reset();
		}
	}

	public static void Fail(string reason)
	{
		if (failed)
		{
			return;
		}
		if (Session.IsHost && baselineSent)
		{
			try
			{
				SendCensus(frozen: true);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError((object)("[生命] 失败现场清单发送异常：" + ex.Message));
			}
		}
		failed = true;
		StatusText = "同步停止：" + reason;
		Plugin.Log.LogError((object)("[战斗] " + StatusText));
		if (Session.Active && Epoch > 0)
		{
			using MessageWriter messageWriter = Begin(MsgType.BattleReady);
			messageWriter.Write(value: false);
			VisualWire.WriteString(messageWriter, reason, 2048);
			Session.Send(messageWriter.ToArray());
		}
		PauseForFailure();
	}

	private static void PauseForFailure()
	{
		IslandGameplayManager instance = Singleton<IslandGameplayManager>.instance;
		if (global::UnityObjectOps.Truthy((Object)(object)instance) && global::UnityObjectOps.Truthy((Object)(object)instance.levelPauser) && !instance.levelPauser.isPaused)
		{
			instance.levelPauser.SetPause(true, false);
		}
	}

	public static void RegisterGroup(ShipGroup group, Squad squad)
	{
		if (!Session.Active || !global::UnityObjectOps.Truthy((Object)(object)island) || !global::UnityObjectOps.Truthy((Object)(object)group) || !global::UnityObjectOps.Truthy((Object)(object)squad) || !RaidIdentity.TryGetGroup(group, out var waveId, out var groupIndex))
		{
			return;
		}
		Group value = new Group
		{
			Wave = waveId,
			Index = groupIndex
		};
		groups[squad] = value;
		foreach (Actor value2 in actors.Values)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)value2.Agent) && (Object)(object)value2.Agent.squad == (Object)(object)squad && value2.Key.Side == 1 && (value2.Key.A != value.Wave || value2.Key.B != value.Index))
			{
				localActors.Remove(value2.Key);
				value2.Key.A = value.Wave;
				value2.Key.B = value.Index;
				localActors[value2.Key] = value2;
			}
		}
	}

	public static void RegisterAgent(Squad squad, Agent agent, Agent prefab)
	{
		if ((!Session.Active && !ClientPlayback) || !global::UnityObjectOps.Truthy((Object)(object)island) || CampaignSync.ClosingBattle || !global::UnityObjectOps.Truthy((Object)(object)agent) || actors.ContainsKey(agent))
		{
			return;
		}
		if (actors.Count >= 8192 || (Session.IsHost && nextId >= 10000000))
		{
			Fail("当前人物数量或累计编号达到协议上限");
			return;
		}
		ordinals.TryGetValue(squad, out var value);
		ordinals[squad] = value + 1;
		EnglishSquad val = (EnglishSquad)(object)((squad is EnglishSquad) ? squad : null);
		Key key;
		if (global::UnityObjectOps.Truthy((Object)(object)val))
		{
			key = new Key
			{
				Side = 0,
				A = val.hero.id,
				B = 0,
				Ordinal = value
			};
		}
		else
		{
			if (!groups.TryGetValue(squad, out var value2))
			{
				value2 = new Group
				{
					Wave = -1,
					Index = nextGroup++
				};
				groups[squad] = value2;
			}
			key = new Key
			{
				Side = 1,
				A = value2.Wave,
				B = value2.Index,
				Ordinal = value
			};
		}
		string text = NormalizeName(global::UnityObjectOps.Truthy((Object)(object)prefab) ? ((Object)prefab).name : ((Object)agent).name);
		if (global::UnityObjectOps.Truthy((Object)(object)prefab))
		{
			prefabs[text] = prefab;
		}
		Actor actor = new Actor
		{
			Agent = agent,
			Squad = squad,
			Key = key,
			Prefab = text
		};
		if (Session.IsHost)
		{
			actor.Id = ++nextId;
			networkActors.Add(actor.Id, actor);
		}
		actors.Add(agent, actor);
		localActors[key] = actor;
	}

	public static bool PrepareBattle()
	{
		if (!Session.IsHost || !global::UnityObjectOps.Truthy((Object)(object)island) || failed || CampaignSync.ClosingBattle)
		{
			return false;
		}
		if (peerReady)
		{
			return true;
		}
		if (baselineSent)
		{
			return false;
		}
		expectedActors = actors.Count;
		if (expectedActors == 0 || expectedActors > 8192)
		{
			Fail("部署人物数量非法：" + expectedActors);
			return false;
		}
		using (MessageWriter messageWriter = Begin(MsgType.BattleBaseline))
		{
			messageWriter.Write(levelIndex);
			messageWriter.Write(MapHash());
			messageWriter.Write(expectedActors);
			messageWriter.Write(HostTime);
			if (!Session.Send(messageWriter.ToArray()))
			{
				Fail("战斗基线发送失败");
				return false;
			}
		}
		baselineSent = true;
		baselineStarted = Time.unscaledTime;
		StatusText = "正在发送 " + expectedActors + " 名人物的表现布局";
		return false;
	}

	public static void Tick()
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)island) || CampaignSync.ClosingBattle)
		{
			return;
		}
		PruneSquads();
		if (failed)
		{
			PauseForFailure();
		}
		else
		{
			if (!Session.Active)
			{
				return;
			}
			if (Session.IsHost)
			{
				if (baselineSent && !peerReady && Time.unscaledTime - baselineStarted > 45f)
				{
					Fail("等待客户端校验超时，请检查资源或人物差异");
				}
			}
			else
			{
				if (!ClientPlayback)
				{
					return;
				}
				clock.Advance(Time.unscaledDeltaTime);
				TryBindBirths();
				if (failed)
				{
					return;
				}
				if (!localReady && networkActors.Count >= expectedActors && pendingBirths.Count == 0 && VisualResources.UnresolvedCount == 0)
				{
					using (MessageWriter messageWriter = Begin(MsgType.BattleReady))
					{
						messageWriter.Write(value: true);
						VisualWire.WriteString(messageWriter, "", 2048);
						if (!Session.Send(messageWriter.ToArray()))
						{
							Fail("人物校验确认发送失败");
							return;
						}
					}
					localReady = true;
					StatusText = "表现就绪：" + networkActors.Count + " 名，等待主机开战";
					Plugin.Log.LogInfo((object)("[战斗] 客户端地图/人物布局已确认 " + networkActors.Count));
				}
				if (VisualResources.UnresolvedCount > 0)
				{
					if (missingSince == 0f)
					{
						missingSince = Time.unscaledTime;
					}
					if (Time.unscaledTime - missingSince > 10f)
					{
						Fail("人物精灵资源无法匹配，请确保双方游戏和外观模组一致");
					}
				}
				else
				{
					missingSince = 0f;
				}
				if (!localReady && Time.unscaledTime - baselineStarted > 45f)
				{
					Fail("客户端人物基线校验超时");
				}
			}
		}
	}

	public static void LateTick()
	{
		//IL_05c2: Unknown result type (might be due to invalid IL or missing references)
		if (!global::UnityObjectOps.Truthy((Object)(object)island) || CampaignSync.ClosingBattle)
		{
			return;
		}
		if (failed)
		{
			if (ClientPlayback)
			{
				ApplyClientFacts(factsOnly: true);
			}
		}
		else
		{
			if (Session.IsHost && baselineSent)
			{
				actorScratch.Clear();
				actorScratch.AddRange(actors.Values);
				foreach (Actor item in actorScratch)
				{
					if (!global::UnityObjectOps.Truthy((Object)(object)item.Agent) && !item.Announced)
					{
						retired.Add(item.Id);
						ReleaseActor(item);
					}
				}
				int num = 0;
				foreach (Actor item2 in actorScratch)
				{
					if (global::UnityObjectOps.Truthy((Object)(object)item2.Agent) && !item2.Removed && !item2.Announced && CanPublishActor(item2) && num++ < 8)
					{
						SendBirth(item2);
					}
				}
				if (!DeploySync.BattleStarted)
				{
					return;
				}
				if (Time.unscaledTime >= nextCensus)
				{
					nextCensus = Time.unscaledTime + 0.25f;
					SendCensus(Time.timeScale == 0f);
				}
				if (Time.unscaledTime >= nextSnapshot)
				{
					nextSnapshot = Time.unscaledTime + 0.05f;
					using (PoseBatch batch = ((Session.Transport is SteamTransport) ? new PoseBatch(delegate(byte[] data)
					{
						SendPosePacket(data, reliable: false);
					}) : null))
					{
						foreach (Actor item3 in actorScratch)
						{
							if (global::UnityObjectOps.Truthy((Object)(object)item3.Agent) && item3.Announced && !item3.Removed)
							{
								if (!NeedsPose(CaptureFlags(item3.Agent), item3.LastFlags, Time.unscaledTime, item3.NextDormantPose))
								{
									skippedPoses++;
								}
								else
								{
									SendPose(item3, reliable: false, batch);
								}
							}
						}
					}
					LogPoseTraffic();
				}
				foreach (Actor item4 in actorScratch)
				{
					if (!global::UnityObjectOps.Truthy((Object)(object)item4.Agent) && item4.Announced && !item4.Removed)
					{
						SendRemoval(item4);
					}
				}
				if (Time.unscaledTime >= nextClock)
				{
					nextClock = Time.unscaledTime + 0.05f;
					using MessageWriter messageWriter = Begin(MsgType.BattleClock);
					messageWriter.Write(++clockSequence);
					messageWriter.Write(HostTime);
					messageWriter.Write(Time.timeScale);
					Session.Send(messageWriter.ToArray(), reliable: false);
				}
				if (!(Time.unscaledTime >= nextLayoutCheck))
				{
					return;
				}
				nextLayoutCheck = Time.unscaledTime + 2f;
				{
					foreach (Actor item5 in actorScratch)
					{
						if (global::UnityObjectOps.Truthy((Object)(object)item5.Agent) && !item5.Removed && item5.Visual != null && (!item5.Visual.IsLayoutCurrent() || !item5.Equipment.IsCurrent()))
						{
							item5.Visual.Dispose();
							item5.Visual = null;
							SendBirth(item5);
						}
					}
					return;
				}
			}
			if (!ClientPlayback || !localReady)
			{
				return;
			}
			ApplyClientFacts(factsOnly: false);
			actorScratch.Clear();
			actorScratch.AddRange(actors.Values);
			foreach (Actor item6 in actorScratch)
			{
				if (!global::UnityObjectOps.Truthy((Object)(object)item6.Agent))
				{
					ReleaseActor(item6);
				}
				else
				{
					if (item6.Removed)
					{
						continue;
					}
					EnsureVisual(item6);
					item6.Visual.EnterPlayback();
					if (item6.Id == 0)
					{
						if (((Component)item6.Agent).gameObject.activeSelf)
						{
							((Component)item6.Agent).gameObject.SetActive(false);
						}
					}
					else
					{
						if (!item6.History.TryVisual(PlaybackTime, out var previous, out var next, out var blend))
						{
							continue;
						}
						blend = PoseBlend(previous.Flags, next.Flags, blend);
						Sample sample = null;
						if (item6.History.TryState(PlaybackTime, out var previous2, out var next2, out var blend2))
						{
							blend2 = PoseBlend(previous2.Flags, next2.Flags, blend2);
							sample = ((blend2 >= 0.9999f) ? next2 : previous2);
						}
						if (sample != null && (sample.Flags & 0x20) != 0)
						{
							if (((Component)item6.Agent).gameObject.activeSelf)
							{
								((Component)item6.Agent).gameObject.SetActive(false);
							}
							continue;
						}
						item6.Visual.Apply(previous.Visual, next.Visual, blend);
						if (sample != null)
						{
							Sample sample2 = ((blend >= 0.9999f) ? next : previous);
							bool flag = (sample.Flags & 0x10) != 0 && ((sample.Flags & 2) != 0 || (sample2.Flags & 2) == 0);
							if (((Component)item6.Agent).gameObject.activeSelf != flag)
							{
								((Component)item6.Agent).gameObject.SetActive(flag);
							}
						}
					}
				}
			}
			ApplyRemovals();
			if ((int)pendingResult != 0 && PlaybackTime >= resultTime)
			{
				ApplyResult();
			}
			if (localReady && DeploySync.BattleStarted)
			{
				StatusText = "主机权威表现：" + networkActors.Count + " 名；缓冲播放中";
			}
		}
	}

	private static void EnsureEquipment(Actor actor)
	{
		if (actor.Equipment == null)
		{
			actor.Equipment = new VisualEquipment(actor.Agent);
		}
	}

	private static void EnsureVisual(Actor actor)
	{
		EnsureEquipment(actor);
		if (actor.Visual == null)
		{
			actor.Visual = new ActorVisual(actor.Agent);
		}
	}

	private static Sample Capture(Actor actor)
	{
		EnsureVisual(actor);
		Sample sample = CaptureFacts(actor.Agent);
		sample.Visual = actor.Visual.Capture();
		return sample;
	}

	private static byte CaptureFlags(Agent agent)
	{
		byte b = 0;
		if (agent.spawned.active)
		{
			b |= 1;
		}
		if (agent.aliveState.active)
		{
			b |= 2;
		}
		if (agent.groundedState.active)
		{
			b |= 4;
		}
		if (agent.navPos.valid && agent.navPos.onMain)
		{
			b |= 8;
		}
		if (((Component)agent).gameObject.activeSelf)
		{
			b |= 0x10;
		}
		if (!((Component)agent).gameObject.activeInHierarchy)
		{
			b |= 0x20;
		}
		return b;
	}

	private static Sample CaptureFacts(Agent agent)
	{
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		byte flags = CaptureFlags(agent);
		ClientSquadFacts.CaptureTarget(agent, out var kind, out var id);
		Sample obj = new Sample
		{
			Flags = flags,
			Health = agent.health,
			Progress = ClientSquadFacts.CaptureProgress(agent, kind),
			PathKind = kind,
			PathId = id
		};
		Squad squad = agent.squad;
		obj.PathRevision = ClientSquadFacts.TargetRevision((EnglishSquad)(object)((squad is EnglishSquad) ? squad : null));
		obj.NavPosition = (agent.navPos.valid ? agent.navPos.wPos : ((Component)agent).transform.position);
		return obj;
	}

	private static void SendCensus(bool frozen)
	{
		LifeCensus lifeCensus = new LifeCensus
		{
			Sequence = ++sequence,
			Time = HostTime,
			LastId = nextId,
			Frozen = frozen
		};
		int num = 0;
		int num2 = 0;
		foreach (Actor value in actors.Values)
		{
			if (!value.Announced || value.Removed || !global::UnityObjectOps.Truthy((Object)(object)value.Agent) || value.Agent.spawned == null)
			{
				continue;
			}
			Sample sample = CaptureFacts(value.Agent);
			LifeCensus.Entry entry = new LifeCensus.Entry
			{
				Id = value.Id,
				Flags = sample.Flags,
				Health = sample.Health,
				X = sample.NavPosition.x,
				Y = sample.NavPosition.y,
				Z = sample.NavPosition.z,
				Progress = sample.Progress,
				PathKind = sample.PathKind,
				PathId = sample.PathId,
				PathRevision = sample.PathRevision
			};
			lifeCensus.Entries.Add(entry);
			if (entry.Living)
			{
				if (value.Key.Side == 0)
				{
					num++;
				}
				else
				{
					num2++;
				}
			}
		}
		using (MessageWriter messageWriter = Begin(MsgType.ActorCensus))
		{
			lifeCensus.Write(messageWriter);
			if (!Session.Send(messageWriter.ToArray()))
			{
				throw new IOException("独立生命清单发送失败");
			}
		}
		LifeStatus = "主机存活：我方 " + num + " / 敌方 " + num2;
		if (frozen || Time.unscaledTime >= nextLifeLog)
		{
			nextLifeLog = Time.unscaledTime + 5f;
			Plugin.Log.LogInfo((object)("[生命] 主机清单 " + lifeCensus.Sequence + " 时间=" + lifeCensus.Time.ToString("F3") + " 我方=" + num + " 敌方=" + num2 + " 冻结=" + frozen));
		}
	}

	private static Sample CensusSample(LifeCensus.Entry entry, float time)
	{
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		return new Sample
		{
			Flags = entry.Flags,
			Health = entry.Health,
			Progress = entry.Progress,
			PathKind = entry.PathKind,
			PathId = entry.PathId,
			PathRevision = entry.PathRevision,
			PathTime = time,
			NavPosition = new Vector3(entry.X, entry.Y, entry.Z)
		};
	}

	private static void ReceiveCensus(BinaryReader reader)
	{
		int num = ReadHeader(reader);
		LifeCensus lifeCensus = LifeCensus.Read(reader);
		if (reader.BaseStream.Position != reader.BaseStream.Length)
		{
			throw new IOException("生命清单含多余数据");
		}
		if (CampaignSync.ClosingBattle || !Session.IsClient || !ClientPlayback || num != Epoch || (latestCensus != null && lifeCensus.Sequence <= latestCensus.Sequence))
		{
			return;
		}
		latestCensus = lifeCensus;
		latestLife.Clear();
		foreach (LifeCensus.Entry entry in lifeCensus.Entries)
		{
			if (!retired.Contains(entry.Id))
			{
				latestLife.Add(entry.Id, entry);
				if (networkActors.TryGetValue(entry.Id, out var value) && !value.Removed)
				{
					value.History.AddState(lifeCensus.Sequence, lifeCensus.Time, CensusSample(entry, lifeCensus.Time));
				}
			}
		}
		HashSet<int> hashSet = new HashSet<int>(networkActors.Keys);
		hashSet.UnionWith(pendingBirths.Keys);
		foreach (int item in lifeCensus.Missing(hashSet))
		{
			if (!removals.TryGetValue(item, out var value2) || lifeCensus.Time < value2)
			{
				removals[item] = lifeCensus.Time;
			}
		}
		if (lifeCensus.Frozen)
		{
			clock.FreezeAt(lifeCensus.Time);
		}
	}

	private static void ApplyClientFacts(bool factsOnly)
	{
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_011e: Unknown result type (might be due to invalid IL or missing references)
		ApplyRemovals();
		ClientSquadFacts.BeginFrame();
		bool flag = Time.unscaledTime >= nextMembershipCheck;
		if (flag)
		{
			nextMembershipCheck = Time.unscaledTime + 0.25f;
		}
		int num = 0;
		int num2 = 0;
		actorScratch.Clear();
		actorScratch.AddRange(actors.Values);
		foreach (Actor item in actorScratch)
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)item.Agent) || item.Removed || item.Id == 0 || !item.History.TryState(PlaybackTime, out var previous, out var next, out var blend))
			{
				continue;
			}
			blend = PoseBlend(previous.Flags, next.Flags, blend);
			Sample sample = ((blend >= 0.9999f) ? next : previous);
			bool sameTarget = previous.PathRevision == next.PathRevision && previous.PathKind == next.PathKind && previous.PathId == next.PathId;
			ApplyState(item, sample, Vector3.Lerp(previous.NavPosition, next.NavPosition, blend), PathProgress.Blend(previous.Progress, next.Progress, blend, sameTarget), flag);
			if ((sample.Flags & 3) == 3)
			{
				if (item.Key.Side == 0)
				{
					num++;
				}
				else
				{
					num2++;
				}
			}
			if (factsOnly && ((sample.Flags & 2) == 0 || (sample.Flags & 0x10) == 0 || (sample.Flags & 0x20) != 0) && ((Component)item.Agent).gameObject.activeSelf)
			{
				((Component)item.Agent).gameObject.SetActive(false);
			}
		}
		foreach (Squad allSquad in island.english.allSquads)
		{
			EnglishSquad val = (EnglishSquad)(object)((allSquad is EnglishSquad) ? allSquad : null);
			if (global::UnityObjectOps.Truthy((Object)(object)val))
			{
				ClientSquadFacts.ApplyTarget(val, island);
				ClientSquadFacts.ClearIfDead(val);
				AbilityCommands.ApplySquadUiFacts(val);
			}
		}
		LifeStatus = "客户端存活：我方 " + num + " / 敌方 " + num2 + "；播放落后 " + (clock.Latest - clock.Time).ToString("F2") + " 秒";
		if (latestCensus != null && Time.unscaledTime >= nextLifeLog)
		{
			nextLifeLog = Time.unscaledTime + 5f;
			Plugin.Log.LogInfo((object)("[生命] 客户端清单 " + latestCensus.Sequence + " 清单时间=" + latestCensus.Time.ToString("F3") + " 播放时间=" + PlaybackTime.ToString("F3") + " 我方=" + num + " 敌方=" + num2));
		}
	}

	private static bool CanPublishActor(Actor actor)
	{
		if (actor.Key.Side == 1)
		{
			if (RaidIdentity.IsCommitted(actor.Key.A))
			{
				return HostRaidManifest.IsWaveReady(actor.Key.A);
			}
			return false;
		}
		return true;
	}

	private static void SendBirth(Actor actor)
	{
		if (!CanPublishActor(actor))
		{
			return;
		}
		Sample sample = Capture(actor);
		FlushResources();
		using MessageWriter messageWriter = Begin(MsgType.ActorSpawn);
		messageWriter.Write(actor.Id);
		WriteKey(messageWriter, actor.Key);
		VisualWire.WriteString(messageWriter, actor.Prefab, 512);
		messageWriter.Write(++sequence);
		messageWriter.Write(HostTime);
		using (MemoryStream memoryStream = new MemoryStream())
		{
			using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
			actor.Equipment.Write(binaryWriter);
			actor.Visual.WriteLayout(binaryWriter);
			binaryWriter.Flush();
			byte[] array = memoryStream.ToArray();
			messageWriter.Write(array.Length);
			messageWriter.Write(array);
		}
		WriteActorSample(messageWriter, actor, sample);
		if (!Session.Send(messageWriter.ToArray()))
		{
			Fail("人物出生信息发送失败");
			return;
		}
		actor.Announced = true;
		actor.LastFlags = sample.Flags;
	}

	internal static bool NeedsPose(byte flags, byte previousFlags, float now, float nextDormant)
	{
		if ((flags & 0x20) != 0 && flags == previousFlags)
		{
			return now >= nextDormant;
		}
		return true;
	}

	internal static float PoseBlend(byte before, byte after, float blend)
	{
		if (((before | after) & 0x20) == 0 || !(blend < 0.9999f))
		{
			return blend;
		}
		return 0f;
	}

	private static bool SendPosePacket(byte[] data, bool reliable)
	{
		bool num = Session.Send(data, reliable);
		if (num)
		{
			posePackets++;
			poseBytes += data.Length + 33;
			return num;
		}
		poseRejected++;
		return num;
	}

	private static void LogPoseTraffic()
	{
		float num = Time.unscaledTime - poseLogStart;
		if (!(num < 5f))
		{
			Plugin.Log.LogInfo((object)("[同步流量] epoch=" + Epoch + " 人物=" + actors.Count + " 姿态=" + poseSamples + " 跳过休眠=" + skippedPoses + " 接受包=" + posePackets + " 拒绝=" + poseRejected + " 姿态KB/s=" + ((float)poseBytes / num / 1000f).ToString("F1") + " 同样本旧编码KB/s=" + ((float)legacyPoseBytes / num / 1000f).ToString("F1") + "（含会话头，非线路带宽/送达统计）"));
			poseLogStart = Time.unscaledTime;
			poseSamples = (skippedPoses = (posePackets = (poseRejected = 0)));
			poseBytes = (legacyPoseBytes = 0L);
		}
	}

	private static void SendPose(Actor actor, bool reliable, PoseBatch batch = null)
	{
		Sample sample;
		try
		{
			sample = Capture(actor);
		}
		catch (IOException)
		{
			if (actor.Visual == null || actor.Visual.IsLayoutCurrent())
			{
				throw;
			}
			batch?.Flush();
			actor.Visual.Dispose();
			actor.Visual = null;
			SendBirth(actor);
			return;
		}
		if (batch != null && VisualResources.HasPendingDefinitions)
		{
			batch.Flush();
		}
		FlushResources();
		reliable |= actor.LastFlags != sample.Flags;
		actor.LastFlags = sample.Flags;
		actor.NextDormantPose = Time.unscaledTime + 1f;
		poseSamples++;
		legacyPoseBytes += 110 + 27 * sample.Visual.Nodes.Length;
		using MessageWriter messageWriter = Begin(MsgType.ActorPose);
		messageWriter.Write(actor.Id);
		messageWriter.Write(++sequence);
		messageWriter.Write(HostTime);
		WriteActorSample(messageWriter, actor, sample);
		byte[] array = messageWriter.ToArray();
		if (batch != null && !reliable)
		{
			batch.Add(array);
			return;
		}
		batch?.Flush();
		if (!SendPosePacket(array, reliable) & reliable)
		{
			Fail("人物生命周期状态发送失败");
		}
	}

	internal static void FlushResources(bool cosmetic = false)
	{
		if (!VisualResources.HasPendingDefinitions || (cosmetic && !Session.OptionalEnabled && !VisualResources.HasPendingRequiredDefinitions))
		{
			return;
		}
		bool hasPendingRequiredDefinitions = VisualResources.HasPendingRequiredDefinitions;
		using MessageWriter messageWriter = Begin(MsgType.VisualResources);
		messageWriter.Write(cosmetic && !hasPendingRequiredDefinitions);
		VisualResources.WritePendingDefinitions(messageWriter);
		if (!((cosmetic && !hasPendingRequiredDefinitions) ? Session.SendOptional(messageWriter.ToArray()) : Session.Send(messageWriter.ToArray())))
		{
			if (cosmetic && !hasPendingRequiredDefinitions)
			{
				SceneVisuals.Warn("resources", "装饰资源定义发送失败");
			}
			else
			{
				Fail("人物资源定义发送失败");
			}
		}
	}

	private static void ReceiveBaseline(BinaryReader reader)
	{
		int num = ReadHeader(reader);
		int num2 = reader.ReadInt32();
		uint num3 = reader.ReadUInt32();
		int num4 = reader.ReadInt32();
		float hostTime = ReadTime(reader);
		if (CampaignSync.ClosingBattle || !Session.IsClient || !global::UnityObjectOps.Truthy((Object)(object)island) || num2 != levelIndex || num != Epoch)
		{
			return;
		}
		if (Epoch != 0 && num != Epoch)
		{
			Fail("收到不属于当前战斗的基线");
			return;
		}
		if (num4 <= 0 || num4 > 8192 || num <= 0)
		{
			throw new IOException("战斗基线非法");
		}
		Epoch = num;
		clientPlayback = true;
		expectedActors = num4;
		baselineStarted = Time.unscaledTime;
		if (num3 != MapHash())
		{
			Fail("两端岛屿导航/建筑不同，已阻止开始战斗");
			return;
		}
		clock.Observe(0, hostTime, Time.timeScale);
		StatusText = "正在匹配主机人物表现布局";
	}

	private static void ReceiveReady(BinaryReader reader)
	{
		int num = ReadHeader(reader);
		bool flag = reader.ReadBoolean();
		string text = VisualWire.ReadString(reader, 2048);
		if (!CampaignSync.ClosingBattle && num == Epoch && global::UnityObjectOps.Truthy((Object)(object)island))
		{
			if (!flag)
			{
				Fail("对端报告：" + text);
			}
			else if (Session.IsHost)
			{
				peerReady = true;
				StatusText = "双端地图/人物校验通过";
				Plugin.Log.LogInfo((object)"[战斗] 人物基线确认，允许第一波");
			}
		}
	}

	private static void ReceiveBirth(BinaryReader reader)
	{
		int num = ReadHeader(reader);
		Birth birth = new Birth
		{
			Id = reader.ReadInt32(),
			Key = ReadKey(reader),
			Prefab = VisualWire.ReadString(reader, 512),
			Sequence = reader.ReadInt32(),
			Time = ReadTime(reader),
			ReceivedAt = Time.unscaledTime
		};
		int num2 = reader.ReadInt32();
		if (birth.Id <= 0 || birth.Id > 10000000 || birth.Sequence < 0 || num2 <= 0 || num2 > 131072)
		{
			throw new IOException("人物出生描述非法");
		}
		birth.Layout = reader.ReadBytes(num2);
		if (birth.Layout.Length != num2)
		{
			throw new EndOfStreamException();
		}
		birth.Sample = ReadActorSample(reader, MsgType.ActorSpawn, num, birth.Id, birth.Sequence);
		birth.Sample.PathTime = birth.Time;
		if (!CampaignSync.ClosingBattle && Session.IsClient && ClientPlayback && num == Epoch && !retired.Contains(birth.Id))
		{
			if (pendingBirths.Count >= 8192 && !pendingBirths.ContainsKey(birth.Id))
			{
				throw new IOException("待创建人物超过上限");
			}
			pendingBirths[birth.Id] = birth;
		}
	}

	private static void TryBindBirths()
	{
		idScratch.Clear();
		foreach (KeyValuePair<int, Birth> pendingBirth in pendingBirths)
		{
			if (failed)
			{
				break;
			}
			Birth value = pendingBirth.Value;
			if (!networkActors.TryGetValue(value.Id, out var value2))
			{
				if (!localActors.TryGetValue(value.Key, out value2) || !global::UnityObjectOps.Truthy((Object)(object)value2.Agent) || value2.Id != 0 || value2.Prefab != value.Prefab)
				{
					value2 = CreateRemoteActor(value);
				}
				if (value2 == null)
				{
					if (Time.unscaledTime - value.ReceivedAt > 10f)
					{
						string prefab = value.Prefab;
						Key key = value.Key;
						Fail("找不到人物模板：" + prefab + "，" + key.ToString());
					}
					continue;
				}
			}
			try
			{
				EnsureEquipment(value2);
				using (MemoryStream input = new MemoryStream(value.Layout, writable: false))
				{
					using BinaryReader reader = new BinaryReader(input);
					value2.Equipment.Read(reader);
					EnsureVisual(value2);
					value2.Visual.ReadLayout(reader, value.Sample.Visual.LayoutHash, value.Sample.Visual.Nodes.Length);
				}
				value2.Equipment.Commit();
				value2.Id = value.Id;
				value2.Key = value.Key;
				networkActors[value.Id] = value2;
				value2.MembershipApplied = false;
				value2.History.Bind(value.Sample.Visual.LayoutHash, value.Sequence, value.Time, value.Sample);
				if (latestCensus != null && latestLife.TryGetValue(value.Id, out var value3))
				{
					value2.History.AddState(latestCensus.Sequence, latestCensus.Time, CensusSample(value3, latestCensus.Time));
				}
				idScratch.Add(pendingBirth.Key);
			}
			catch (Exception ex)
			{
				if (value2.Equipment != null)
				{
					value2.Equipment.Rollback();
				}
				if (!(ex is IOException))
				{
					throw;
				}
				if (Time.unscaledTime - value.ReceivedAt > 3f)
				{
					string[] obj = new string[10]
					{
						"人物 ",
						value.Id.ToString(),
						"（",
						value.Prefab,
						"，",
						null,
						null,
						null,
						null,
						null
					};
					Key key = value.Key;
					obj[5] = key.ToString();
					obj[6] = "，";
					obj[7] = ((value2.Id == 0) ? "首次绑定" : "布局更新");
					obj[8] = "）显示布局不同：";
					obj[9] = ex.Message;
					Fail(string.Concat(obj));
				}
			}
		}
		foreach (int item in idScratch)
		{
			pendingBirths.Remove(item);
		}
	}

	private static Actor CreateRemoteActor(Birth birth)
	{
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_017c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0392: Unknown result type (might be due to invalid IL or missing references)
		//IL_039d: Unknown result type (might be due to invalid IL or missing references)
		Squad value = null;
		Agent val = null;
		if (birth.Key.Side == 0)
		{
			foreach (Squad allSquad in island.english.allSquads)
			{
				EnglishSquad val2 = (EnglishSquad)(object)((allSquad is EnglishSquad) ? allSquad : null);
				if (global::UnityObjectOps.Truthy((Object)(object)val2) && val2.hero.id == birth.Key.A)
				{
					value = (Squad)(object)val2;
					val = ((birth.Key.Ordinal == 0) ? val2.squadTemplate.standardPrefab : val2.minionPrefab);
					break;
				}
			}
			if (!global::UnityObjectOps.Truthy((Object)(object)value))
			{
				return null;
			}
		}
		else
		{
			Key key = birth.Key;
			key.Ordinal = 0;
			if (!remoteSquads.TryGetValue(key, out value) || !global::UnityObjectOps.Truthy((Object)(object)value))
			{
				foreach (KeyValuePair<Squad, Group> group in groups)
				{
					if (global::UnityObjectOps.Truthy((Object)(object)group.Key) && group.Value.Wave == birth.Key.A && group.Value.Index == birth.Key.B)
					{
						value = group.Key;
						break;
					}
				}
				if (!global::UnityObjectOps.Truthy((Object)(object)value))
				{
					value = ScriptableObjectSingleton<PrefabManager>.instance.vikingSquad.SpawnGetFromPrefab(new NavPos(island.navMesh, birth.Sample.NavPosition, true, 1f), island.vikings);
				}
				remoteSquads[key] = value;
			}
		}
		if (global::UnityObjectOps.Truthy((Object)(object)val) && NormalizeName(((Object)val).name) != birth.Prefab)
		{
			val = null;
		}
		if (!global::UnityObjectOps.Truthy((Object)(object)val) && birth.Key.Side == 0)
		{
			foreach (Squad allSquad2 in island.english.allSquads)
			{
				EnglishSquad val3 = (EnglishSquad)(object)((allSquad2 is EnglishSquad) ? allSquad2 : null);
				Agent val4 = null;
				if (global::UnityObjectOps.Truthy((Object)(object)val3) && val3.hero != null && val3.hero.classUpgrade != null && global::UnityObjectOps.Truthy((Object)(object)val3.hero.classUpgrade.definition))
				{
					FieldInfo fieldInfo = AccessTools.Field(((object)val3.hero.classUpgrade.definition).GetType(), "minionAgentPrefab");
					if (fieldInfo != null)
					{
						object value2 = fieldInfo.GetValue(val3.hero.classUpgrade.definition);
						val4 = (Agent)((value2 is Agent) ? value2 : null);
					}
				}
				if (global::UnityObjectOps.Truthy((Object)(object)val4) && NormalizeName(((Object)val4).name) == birth.Prefab)
				{
					val = val4;
					break;
				}
			}
		}
		if (prefabs.TryGetValue(birth.Prefab, out var value3) && global::UnityObjectOps.Truthy((Object)(object)value3))
		{
			val = value3;
		}
		if (!global::UnityObjectOps.Truthy((Object)(object)val))
		{
			VikingReference[] array = Resources.FindObjectsOfTypeAll<VikingReference>();
			foreach (VikingReference val5 in array)
			{
				if (global::UnityObjectOps.Truthy((Object)(object)val5) && global::UnityObjectOps.Truthy((Object)(object)val5.agent) && NormalizeName(((Object)val5.agent).name) == birth.Prefab)
				{
					val = val5.agent;
					prefabs[birth.Prefab] = val;
					break;
				}
			}
		}
		if (!global::UnityObjectOps.Truthy((Object)(object)val) || NormalizeName(((Object)val).name) != birth.Prefab)
		{
			return null;
		}
		Agent key2 = value.CreateAgent(val, new NavPos(island.navMesh, birth.Sample.NavPosition, true, 1f));
		if (!actors.TryGetValue(key2, out var value4))
		{
			return null;
		}
		localActors.Remove(value4.Key);
		value4.Key = birth.Key;
		value4.Prefab = birth.Prefab;
		localActors[birth.Key] = value4;
		if (ordinals[value] <= birth.Key.Ordinal)
		{
			ordinals[value] = birth.Key.Ordinal + 1;
		}
		Plugin.Log.LogInfo((object)("[战斗] 按主机创建补员/增援 " + birth.Id + " " + birth.Prefab));
		return value4;
	}

	private static void ReceivePose(BinaryReader reader)
	{
		int num = ReadHeader(reader);
		int num2 = reader.ReadInt32();
		int seq = reader.ReadInt32();
		float num3 = ReadTime(reader);
		Sample sample = ReadActorSample(reader, MsgType.ActorPose, num, num2, seq);
		sample.PathTime = num3;
		if (!CampaignSync.ClosingBattle && Session.IsClient && ClientPlayback && num == Epoch && networkActors.TryGetValue(num2, out var value) && !value.Removed)
		{
			value.History.Add(sample.Visual.LayoutHash, seq, num3, sample);
		}
	}

	private static void ReceiveResources(BinaryReader reader)
	{
		int num = ReadHeader(reader);
		bool flag = reader.ReadBoolean();
		if (CampaignSync.ClosingBattle || !Session.IsClient || num != Epoch || !ClientPlayback)
		{
			return;
		}
		if (!flag)
		{
			VisualResources.ReadDefinitions(reader);
			reader.RequireEnd();
			return;
		}
		try
		{
			VisualResources.ReadDefinitions(reader);
			reader.RequireEnd();
		}
		catch (Exception ex)
		{
			SceneVisuals.Warn("optional-definitions", "装饰资源定义降级：" + ex.Message);
		}
	}

	private static void ReceiveClock(BinaryReader reader)
	{
		int num = ReadHeader(reader);
		int num2 = reader.ReadInt32();
		float hostTime = ReadTime(reader);
		float hostRate = reader.ReadSingle();
		if (!CampaignSync.ClosingBattle && Session.IsClient && num == Epoch && ClientPlayback)
		{
			clock.Observe(num2, hostTime, hostRate);
		}
	}

	private static void ApplyState(Actor actor, Sample sample, Vector3 navPosition, PathProgress progress, bool reconcileMembership)
	{
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		Agent agent = actor.Agent;
		bool flag = (sample.Flags & 1) != 0;
		bool flag2 = (sample.Flags & 2) != 0;
		bool flag3 = (sample.Flags & 4) != 0;
		SetState(agent.spawned, flag);
		SetState(agent.aliveState, flag2);
		SetState(agent.deadState, !flag2);
		SetState(agent.groundedState, flag3);
		SetState(agent.aliveAndGrounded, flag & flag2 & flag3);
		agent.health = sample.Health;
		agent.wPos = navPosition;
		agent.velocity = Vector3.zero;
		agent.force = Vector3.zero;
		Squad squad = agent.squad;
		ClientSquadFacts.RecordTarget((EnglishSquad)(object)((squad is EnglishSquad) ? squad : null), sample.PathKind, sample.PathId, sample.PathTime, sample.PathRevision, progress);
		if ((sample.Flags & 8) != 0)
		{
			if (!agent.navPos.valid || !agent.navPos.onMain)
			{
				agent.navPos = new NavPos(island.navMesh, navPosition, true, 1f);
			}
			else
			{
				agent.navPos.pos = agent.navPos.transform.InverseTransformPoint(navPosition);
			}
			if ((Object)(object)((Component)agent).transform.parent != (Object)(object)island.runContainer)
			{
				((Component)agent).transform.SetParent(island.runContainer, true);
			}
		}
		ApplyMembership(actor, sample.Flags, reconcileMembership);
		actor.LastFlags = sample.Flags;
	}

	private static void ApplyMembership(Actor actor, byte flags, bool reconcile)
	{
		Agent agent = actor.Agent;
		byte b = (byte)(flags & 3);
		if (!reconcile && actor.MembershipApplied && b == actor.MembershipFlags && (Object)(object)actor.MembershipSquad == (Object)(object)agent.squad && (Object)(object)actor.MembershipFaction == (Object)(object)agent.faction)
		{
			return;
		}
		Squad membershipSquad = actor.MembershipSquad;
		Faction membershipFaction = actor.MembershipFaction;
		if (global::UnityObjectOps.Truthy((Object)(object)membershipSquad) && (Object)(object)membershipSquad != (Object)(object)agent.squad)
		{
			membershipSquad.livingAgents.Remove(agent);
			SetMembership(Squad.allLiving, membershipSquad, membershipSquad.alive);
			SetMembership(membershipSquad.faction.livingSquads, membershipSquad, membershipSquad.alive);
		}
		if (global::UnityObjectOps.Truthy((Object)(object)membershipFaction) && (Object)(object)membershipFaction != (Object)(object)agent.faction)
		{
			membershipFaction.agents.Remove(agent);
			if (global::UnityObjectOps.Truthy((Object)(object)membershipSquad))
			{
				SetMembership(membershipFaction.livingSquads, membershipSquad, membershipSquad.alive && (Object)(object)membershipSquad.faction == (Object)(object)membershipFaction);
			}
		}
		bool flag = (b & 1) != 0;
		bool flag2 = (b & 2) != 0;
		SetMembership(AllActive, agent, flag);
		SetMembership(agent.faction.agents, agent, flag);
		SetMembership(agent.squad.livingAgents, agent, flag & flag2);
		SetMembership(Squad.allLiving, agent.squad, agent.squad.alive);
		SetMembership(agent.faction.livingSquads, agent.squad, agent.squad.alive);
		actor.MembershipFlags = b;
		actor.MembershipSquad = agent.squad;
		actor.MembershipFaction = agent.faction;
		actor.MembershipApplied = true;
	}

	private static void SetState(AgentState state, bool value)
	{
		if (state != null && state.active != value)
		{
			stateActive.SetValue(state, value);
		}
	}

	private static void SetMembership<T>(List<T> list, T value, bool present)
	{
		if (present)
		{
			if (!list.Contains(value))
			{
				list.Add(value);
			}
		}
		else
		{
			list.Remove(value);
		}
	}

	public static bool BeforeFinalDeath(Agent agent)
	{
		if (ClientPlayback)
		{
			return false;
		}
		if (!CampaignSync.ClosingBattle && Session.IsHost && baselineSent && !sendingFinal && actors.TryGetValue(agent, out var value) && value.Announced)
		{
			sendingFinal = true;
			try
			{
				SendPose(value, reliable: true);
				SendRemoval(value);
			}
			catch (Exception ex)
			{
				Fail("死亡表现采样失败：" + ex.Message);
			}
			finally
			{
				sendingFinal = false;
			}
		}
		return true;
	}

	private static void SendRemoval(Actor actor)
	{
		if (actor.Removed)
		{
			return;
		}
		using (MessageWriter messageWriter = Begin(MsgType.ActorDespawn))
		{
			messageWriter.Write(actor.Id);
			messageWriter.Write(HostTime);
			if (!Session.Send(messageWriter.ToArray()))
			{
				Fail("人物退场发送失败");
				return;
			}
		}
		actor.Removed = true;
		retired.Add(actor.Id);
		ReleaseActor(actor);
	}

	private static void ReceiveRemoval(BinaryReader reader)
	{
		int num = ReadHeader(reader);
		int num2 = reader.ReadInt32();
		float value = ReadTime(reader);
		if (!CampaignSync.ClosingBattle && Session.IsClient && num == Epoch && ClientPlayback && !retired.Contains(num2) && num2 > 0 && num2 <= 10000000 && (networkActors.ContainsKey(num2) || pendingBirths.ContainsKey(num2)))
		{
			removals[num2] = value;
		}
	}

	private static void ApplyRemovals()
	{
		idScratch.Clear();
		foreach (KeyValuePair<int, float> removal in removals)
		{
			if (removal.Value > PlaybackTime)
			{
				continue;
			}
			if (networkActors.TryGetValue(removal.Key, out var value))
			{
				value.Removed = true;
				if (global::UnityObjectOps.Truthy((Object)(object)value.Agent))
				{
					Agent agent = value.Agent;
					SceneDecorations.BeforeActorRemoval(removal.Key, agent);
					FireVisuals.RemoveOwner(1, removal.Key);
					SetState(agent.spawned, value: false);
					SetState(agent.aliveState, value: false);
					SetState(agent.aliveAndGrounded, value: false);
					agent.squad.livingAgents.Remove(agent);
					agent.faction.agents.Remove(agent);
					AllActive.Remove(agent);
					SetMembership(Squad.allLiving, agent.squad, agent.squad.alive);
					SetMembership(agent.faction.livingSquads, agent.squad, agent.squad.alive);
					((Component)agent).gameObject.SetActive(false);
					Squad squad = agent.squad;
					ClientSquadFacts.ClearIfDead((EnglishSquad)(object)((squad is EnglishSquad) ? squad : null));
					Object.Destroy((Object)(object)((Component)agent).gameObject);
				}
				ReleaseActor(value);
			}
			retired.Add(removal.Key);
			pendingBirths.Remove(removal.Key);
			latestLife.Remove(removal.Key);
			idScratch.Add(removal.Key);
		}
		foreach (int item in idScratch)
		{
			removals.Remove(item);
		}
	}

	private static void ReleaseActor(Actor actor)
	{
		Agent agent = actor.Agent;
		Squad squad = actor.Squad;
		if (actor.Visual != null)
		{
			actor.Visual.Dispose();
		}
		if (actor.Equipment != null)
		{
			actor.Equipment.Dispose();
		}
		actor.History.Reset();
		actor.Visual = null;
		actor.Equipment = null;
		if (agent != null)
		{
			actors.Remove(agent);
		}
		localActors.Remove(actor.Key);
		networkActors.Remove(actor.Id);
		latestLife.Remove(actor.Id);
		if (latestCensus != null)
		{
			latestCensus.Entries.RemoveAll((LifeCensus.Entry entry) => entry.Id == actor.Id);
		}
		pendingBirths.Remove(actor.Id);
		actor.Agent = null;
		actor.Squad = null;
		if (!global::UnityObjectOps.Truthy((Object)(object)squad) || squad is EnglishSquad)
		{
			return;
		}
		bool flag = false;
		foreach (Actor value in actors.Values)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)value.Agent) && (Object)(object)value.Agent.squad == (Object)(object)squad)
			{
				flag = true;
				break;
			}
		}
		if (flag)
		{
			return;
		}
		groups.Remove(squad);
		List<Key> list = new List<Key>();
		foreach (KeyValuePair<Key, Squad> remoteSquad in remoteSquads)
		{
			if ((Object)(object)remoteSquad.Value == (Object)(object)squad)
			{
				list.Add(remoteSquad.Key);
			}
		}
		foreach (Key item in list)
		{
			remoteSquads.Remove(item);
		}
	}

	private static void PruneSquads()
	{
		foreach (Squad item in new List<Squad>(ordinals.Keys))
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)item))
			{
				ordinals.Remove(item);
				groups.Remove(item);
			}
		}
		foreach (Key item2 in new List<Key>(remoteSquads.Keys))
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)remoteSquads[item2]))
			{
				remoteSquads.Remove(item2);
			}
		}
	}

	public static bool BeforeResult()
	{
		if (ClientPlayback)
		{
			return applyingResult;
		}
		return true;
	}

	internal static Agent FindAgent(int id)
	{
		if (!networkActors.TryGetValue(id, out var value) || value.Removed)
		{
			return null;
		}
		return value.Agent;
	}

	internal static int FindId(Agent agent)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)agent) || !actors.TryGetValue(agent, out var value) || !value.Announced)
		{
			return 0;
		}
		return value.Id;
	}

	internal static bool IsRetired(int id)
	{
		return retired.Contains(id);
	}

	internal static bool TryDescribeSurface(Transform target, out int id, out string[] names, out ushort[] ordinals)
	{
		id = 0;
		names = null;
		ordinals = null;
		Agent componentInParent = ((Component)target).GetComponentInParent<Agent>();
		if (!global::UnityObjectOps.Truthy((Object)(object)componentInParent) || !actors.TryGetValue(componentInParent, out var value) || !value.Announced || value.Removed || value.Visual == null)
		{
			return false;
		}
		id = value.Id;
		return value.Visual.TryDescribeSurface(target, out names, out ordinals);
	}

	internal static Transform ResolveSurface(int id, string[] names, ushort[] ordinals)
	{
		if (!networkActors.TryGetValue(id, out var value) || value.Removed || value.Visual == null)
		{
			return null;
		}
		return value.Visual.ResolveSurface(names, ordinals);
	}

	public static void AfterResult(EndOfLevel.Reason reason)
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		if (!CampaignSync.ClosingBattle && Session.IsHost && DeploySync.BattleStarted && !resultSent)
		{
			SendCensus(frozen: true);
			resultSent = true;
			resultTime = HostTime;
			sentResultReason = reason;
			SendResultSummary();
		}
	}

	private static BattleSummary CaptureResultSummary()
	{
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Expected I4, but got Unknown
		BattleSummary battleSummary = new BattleSummary();
		EnemyLineupIntro instance = EnemyLineupIntro.instance;
		if (!global::UnityObjectOps.Truthy((Object)(object)instance))
		{
			throw new IOException("原生结算统计尚未初始化");
		}
		foreach (EnemyLineupIntro.VikingInfo vikingInfo in instance.vikingInfoList)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)vikingInfo.vikingReference))
			{
				battleSummary.Entries.Add(new BattleSummary.Entry
				{
					Reference = ((Object)vikingInfo.vikingReference).name,
					Type = (int)vikingInfo.vikingReference.type,
					Kills = vikingInfo.kills,
					OnIsland = vikingInfo.onIsland
				});
			}
		}
		return battleSummary;
	}

	private static void SendResultSummary()
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		BattleSummary battleSummary = CaptureResultSummary();
		using (MessageWriter messageWriter = Begin(MsgType.BattleResult))
		{
			messageWriter.Write(resultTime);
			messageWriter.Write((byte)sentResultReason);
			messageWriter.Write(++resultSequence);
			battleSummary.Write(messageWriter);
			if (!Session.Send(messageWriter.ToArray()))
			{
				throw new IOException("结算统计发送失败");
			}
		}
		int num = 0;
		foreach (BattleSummary.Entry entry in battleSummary.Entries)
		{
			num += entry.Kills;
		}
		Plugin.Log.LogInfo((object)("[结算] 主机 epoch=" + Epoch + " 修订=" + resultSequence + " 击杀=" + num + " 类别=" + battleSummary.Entries.Count));
	}

	private static void ReceiveResult(BinaryReader reader)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Invalid comparison between Unknown and I4
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Invalid comparison between Unknown and I4
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		int num = ReadHeader(reader);
		float num2 = ReadTime(reader);
		EndOfLevel.Reason val = (EndOfLevel.Reason)reader.ReadByte();
		int num3 = reader.ReadInt32();
		BattleSummary battleSummary = BattleSummary.Read(reader);
		reader.RequireEnd();
		if ((int)val < 1 || (int)val > 3 || num3 <= 0)
		{
			throw new IOException("关卡结果非法");
		}
		if (!CampaignSync.ClosingBattle && Session.IsClient && num == Epoch && ClientPlayback && num3 > receivedResultSequence)
		{
			if (receivedResultSequence > 0 && (sentResultReason != val || resultTime != num2))
			{
				throw new IOException("同场结算原因或时间发生变化");
			}
			receivedResultSequence = num3;
			sentResultReason = val;
			resultSummary = battleSummary;
			resultTime = num2;
			if (resultApplied)
			{
				ApplyResultSummary();
			}
			else
			{
				pendingResult = val;
			}
			clock.ConfirmTime(num2);
		}
	}

	private static void ApplyResultSummary()
	{
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Expected I4, but got Unknown
		//IL_0183: Unknown result type (might be due to invalid IL or missing references)
		//IL_018a: Expected I4, but got Unknown
		if (resultSummary == null || !global::UnityObjectOps.Truthy((Object)(object)EnemyLineupIntro.instance))
		{
			return;
		}
		Dictionary<string, EnemyLineupIntro.VikingInfo> dictionary = new Dictionary<string, EnemyLineupIntro.VikingInfo>(StringComparer.Ordinal);
		foreach (EnemyLineupIntro.VikingInfo vikingInfo in EnemyLineupIntro.instance.vikingInfoList)
		{
			vikingInfo.kills = 0;
			if (global::UnityObjectOps.Truthy((Object)(object)vikingInfo.vikingReference))
			{
				dictionary.Add((int)vikingInfo.vikingReference.type + ":" + ((Object)vikingInfo.vikingReference).name, vikingInfo);
			}
		}
		int num = 0;
		foreach (BattleSummary.Entry entry in resultSummary.Entries)
		{
			if (!dictionary.TryGetValue(entry.Key, out var value))
			{
				throw new IOException("结算敌兵资源未匹配：" + entry.Key);
			}
			value.kills = entry.Kills;
			value.onIsland = entry.OnIsland;
			num += entry.Kills;
		}
		foreach (EnemyLineup preparedLineup in preparedLineups)
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)preparedLineup))
			{
				continue;
			}
			foreach (AgentInfo item in (List<AgentInfo>)lineupRows.GetValue(preparedLineup))
			{
				if (global::UnityObjectOps.Truthy((Object)(object)item) && global::UnityObjectOps.Truthy((Object)(object)item.vikingReference) && dictionary.TryGetValue((int)item.vikingReference.type + ":" + ((Object)item.vikingReference).name, out var value2))
				{
					item.text.text = IntStringCache.GetClean(value2.kills);
				}
			}
		}
		Plugin.Log.LogInfo((object)("[结算] 客户端 epoch=" + Epoch + " 修订=" + receivedResultSequence + " 击杀=" + num));
	}

	internal static void OnLineupPrepared(EnemyLineup lineup)
	{
		if (ClientPlayback)
		{
			preparedLineups.Add(lineup);
		}
		else if (Session.IsHost && resultSent && !CampaignSync.ClosingBattle)
		{
			SendResultSummary();
		}
	}

	private static void ApplyResult()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Invalid comparison between Unknown and I4
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Invalid comparison between Unknown and I4
		EndOfLevel.Reason val = pendingResult;
		pendingResult = (EndOfLevel.Reason)0;
		applyingResult = true;
		try
		{
			ApplyResultSummary();
			resultApplied = true;
			EndOfLevel endOfLevel = Singleton<IslandGameplayManager>.instance.endOfLevel;
			if ((int)val == 1)
			{
				endOfLevel.AllVikingsKilled();
			}
			else if ((int)val == 2)
			{
				endOfLevel.AllEnglishKilled();
			}
			else
			{
				endOfLevel.Evacuated();
			}
		}
		finally
		{
			applyingResult = false;
		}
	}

	public static bool BeforeSquadUpdate(Squad squad)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		if (!ClientPlayback)
		{
			return true;
		}
		if (squad.agents.Count > 0)
		{
			squad.bounds = new Bounds(squad.agents[0].wPos, Vector3.one * 0.1f);
			foreach (Agent agent in squad.agents)
			{
				if (global::UnityObjectOps.Truthy((Object)(object)agent))
				{
					squad.bounds.Encapsulate(agent.wPos);
				}
			}
		}
		EnglishSquad val = (EnglishSquad)(object)((squad is EnglishSquad) ? squad : null);
		if (global::UnityObjectOps.Truthy((Object)(object)val))
		{
			((AgentState)val.selectedState).Update();
			((AgentState)val.hoverState).Update();
			val.selectedState.anim.state.Update();
			val.hoverState.anim.state.Update();
		}
		return false;
	}

	public static bool BlockClientDamage(Agent agent)
	{
		if (ClientPlayback && global::UnityObjectOps.Truthy((Object)(object)agent) && global::UnityObjectOps.Truthy((Object)(object)agent.faction))
		{
			return (Object)(object)((IslandComponent)agent.faction).island == (Object)(object)island;
		}
		return false;
	}

	public static void ClearAnimationAction(Agent agent)
	{
		if (animationAction != null)
		{
			animationAction.SetValue(agent, null);
		}
	}

	private static uint MapHash()
	{
		//IL_0152: Unknown result type (might be due to invalid IL or missing references)
		//IL_0175: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0207: Unknown result type (might be due to invalid IL or missing references)
		uint hash = 2166136261u;
		Hash(ref hash, island.navMesh.verts.Length);
		Hash(ref hash, island.navMesh.tris.Length);
		Vert[] verts = island.navMesh.verts;
		foreach (Vert val in verts)
		{
			Hash(ref hash, Mathf.RoundToInt(val.pos.x * 10000f));
			Hash(ref hash, Mathf.RoundToInt(val.pos.y * 10000f));
			Hash(ref hash, Mathf.RoundToInt(val.pos.z * 10000f));
		}
		Tri[] tris = island.navMesh.tris;
		foreach (Tri val2 in tris)
		{
			for (int j = 0; j < 3; j++)
			{
				Hash(ref hash, val2.verts[j].index);
			}
		}
		Hash(ref hash, island.navSpotter.navSpots.Count);
		foreach (NavSpot navSpot in island.navSpotter.navSpots)
		{
			Hash(ref hash, navSpot.index);
			Hash(ref hash, Mathf.RoundToInt(navSpot.navPos.wPos.x * 1000f));
			Hash(ref hash, Mathf.RoundToInt(navSpot.navPos.wPos.z * 1000f));
		}
		Hash(ref hash, island.village.houses.Length);
		House[] houses = island.village.houses;
		foreach (House val3 in houses)
		{
			Hash(ref hash, Mathf.RoundToInt(((Component)val3).transform.position.x * 1000f));
			Hash(ref hash, Mathf.RoundToInt(((Component)val3).transform.position.z * 1000f));
		}
		return hash;
	}

	private static void Hash(ref uint hash, int value)
	{
		hash = (hash ^ (uint)value) * 16777619;
	}

	private static MessageWriter Begin(MsgType type)
	{
		MessageWriter messageWriter = Protocol.Begin(type);
		messageWriter.Write(14);
		messageWriter.Write(Epoch);
		return messageWriter;
	}

	private static int ReadHeader(BinaryReader reader)
	{
		if (reader.ReadInt32() != 14)
		{
			throw new IOException("战斗协议版本不一致");
		}
		return reader.ReadInt32();
	}

	private static float ReadTime(BinaryReader reader)
	{
		float num = reader.ReadSingle();
		if (!Finite(num) || num < 0f)
		{
			throw new IOException("主机时间非法");
		}
		return num;
	}

	private static bool Finite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}

	private static string NormalizeName(string name)
	{
		return (name ?? "").Replace("(Clone)", "").Trim();
	}

	private static void WriteKey(BinaryWriter writer, Key key)
	{
		writer.Write(key.Side);
		writer.Write(key.A);
		writer.Write(key.B);
		writer.Write(key.Ordinal);
	}

	private static Key ReadKey(BinaryReader reader)
	{
		Key result = new Key
		{
			Side = reader.ReadByte(),
			A = reader.ReadInt32(),
			B = reader.ReadInt32(),
			Ordinal = reader.ReadInt32()
		};
		if (result.Side > 1 || result.Ordinal < 0 || result.Ordinal > 10000000)
		{
			throw new IOException("人物身份非法");
		}
		return result;
	}

	private static void WriteSample(BinaryWriter writer, Sample sample)
	{
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		LifeCensus.ValidateFacts(sample.Flags, sample.Health, sample.PathKind, sample.PathId, sample.PathRevision);
		writer.Write(sample.Flags);
		writer.Write(sample.Health);
		PathProgress pathProgress = sample.Progress.Canonical();
		byte b = (byte)((pathProgress.Known ? 1u : 0u) | (uint)((sample.PathKind != byte.MaxValue) ? 2 : 0) | (uint)((sample.PathRevision != 0) ? 4 : 0));
		writer.Write(b);
		if ((b & 1) != 0)
		{
			pathProgress.Write(writer);
		}
		if ((b & 2) != 0)
		{
			writer.Write(sample.PathKind);
			writer.Write(sample.PathId);
		}
		if ((b & 4) != 0)
		{
			writer.Write(sample.PathRevision);
		}
		VisualWire.WriteVector(writer, sample.NavPosition);
		sample.Visual.Write(writer);
	}

	private static void WriteActorSample(BinaryWriter writer, Actor actor, Sample sample)
	{
		try
		{
			WriteSample(writer, sample);
		}
		catch (IOException ex)
		{
			string[] obj = new string[8]
			{
				"人物 ",
				actor.Id.ToString(),
				"（",
				actor.Prefab,
				"，",
				null,
				null,
				null
			};
			Key key = actor.Key;
			obj[5] = key.ToString();
			obj[6] = "）：";
			obj[7] = ex.Message;
			throw new IOException(string.Concat(obj), ex);
		}
	}

	private static Sample ReadSample(BinaryReader reader)
	{
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		Sample sample = new Sample
		{
			Flags = reader.ReadByte(),
			Health = reader.ReadSingle()
		};
		byte num = reader.ReadByte();
		if ((num & -8) != 0)
		{
			throw new IOException("人物路径字段掩码非法");
		}
		if ((num & 1) != 0)
		{
			sample.Progress = PathProgress.Read(reader);
		}
		if ((num & 2) != 0)
		{
			sample.PathKind = reader.ReadByte();
			sample.PathId = reader.ReadInt32();
		}
		if ((num & 4) != 0)
		{
			sample.PathRevision = reader.ReadInt32();
		}
		sample.NavPosition = VisualWire.ReadVector(reader);
		sample.Visual = VisualFrame.Read(reader);
		LifeCensus.ValidateFacts(sample.Flags, sample.Health, sample.PathKind, sample.PathId, sample.PathRevision);
		return sample;
	}

	private static Sample ReadActorSample(BinaryReader reader, MsgType type, int epoch, int id, int seq)
	{
		try
		{
			Sample result = ReadSample(reader);
			reader.RequireEnd();
			return result;
		}
		catch (IOException ex)
		{
			string[] obj = new string[12]
			{
				"人物消息 type=",
				type.ToString(),
				"(",
				null,
				null,
				null,
				null,
				null,
				null,
				null,
				null,
				null
			};
			byte b = (byte)type;
			obj[3] = b.ToString();
			obj[4] = ") epoch=";
			obj[5] = epoch.ToString();
			obj[6] = " id=";
			obj[7] = id.ToString();
			obj[8] = " sequence=";
			obj[9] = seq.ToString();
			obj[10] = "：";
			obj[11] = ex.Message;
			throw new IOException(string.Concat(obj), ex);
		}
	}
}
