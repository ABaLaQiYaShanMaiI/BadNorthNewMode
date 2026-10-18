using System;
using System.Collections;
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

namespace Together;

internal static class HostRaidManifest
{
	private sealed class HostEntry
	{
		public Wave Wave;

		public Manifest Manifest;

		public float SentAt;

		public bool Sent;
	}

	internal sealed class Manifest
	{
		public int Id;

		public int Budget;

		public int Retry;

		public uint Signature;

		public float Start;

		public float SpreadGroup;

		public float SpreadShip;

		public string Approach;

		public string Arrival;

		public readonly List<GroupData> Groups = new List<GroupData>();
	}

	internal sealed class GroupData
	{
		public float Offset;

		public readonly List<LandingData> Landings = new List<LandingData>();
	}

	internal sealed class LandingData
	{
		public string Ship;

		public float Offset;

		public float Duration;

		public float FleeDuration;

		public float Distance;

		public int Triangle;

		public Vector3 Position;

		public Vector3 NavPosition;

		public Vector3 Direction;

		public Vector3 Start;

		public Vector3 End;

		public Color Color;

		public readonly List<LoadData> Loads = new List<LoadData>();

		public Longship Prefab;
	}

	internal sealed class LoadData
	{
		public string Reference;

		public string Agent;

		public int Count;

		public VikingReference Prefab;
	}

	private const byte Schema = 1;

	private const byte Definition = 0;

	private const byte Acknowledgement = 1;

	internal const int MaxGroups = 64;

	internal const int MaxLandings = 128;

	internal const int MaxLoads = 16;

	internal const int MaxAgents = 8192;

	private static bool initialized;

	private static bool patched;

	private static Island island;

	private static int initialCount;

	private static int expectedLogicalId;

	// BadNorthTogether：普通岛屿上由其它模组创建的外部波，编号从这里往后分配（与客户端 expectedLogicalId 对齐）
	private static int nextExternalId;

	private static Type controllerType;

	private static MethodInfo acceptWaves;

	private static IEnumerator builder;

	private static readonly Queue<Manifest> pending = new Queue<Manifest>();

	private static readonly Dictionary<int, HostEntry> hostEntries = new Dictionary<int, HostEntry>();

	private static readonly Dictionary<int, uint> received = new Dictionary<int, uint>();

	private static readonly HashSet<int> ready = new HashSet<int>();

	private static readonly FieldInfo squadField = AccessTools.Field(typeof(ShipGroup), "_squad");

	private static readonly string[] travelNames = new string[5] { "startPos", "endPos", "duration", "fleeDuration", "distance" };

	private static readonly FieldInfo[] travelFields = new FieldInfo[5];

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			Session.On(MsgType.RaidManifest, Receive);
			Session.Stopped += Reset;
			TryPatch();
		}
	}

	public static void BeginIsland(Island value)
	{
		Reset();
		island = value;
		initialCount = ((global::UnityObjectOps.Truthy((Object)(object)island) && global::UnityObjectOps.Truthy((Object)(object)island.raid) && island.raid.waves != null) ? island.raid.waves.Count : 0);
		expectedLogicalId = initialCount;
		nextExternalId = initialCount;   // BadNorthTogether：普通岛屿外部波从 initialCount 起连续编号
		TryPatch();
	}

	public static void Reset()
	{
		if (builder is IDisposable)
		{
			((IDisposable)builder).Dispose();
		}
		builder = null;
		island = null;
		initialCount = (expectedLogicalId = 0);
		nextExternalId = 0;
		pending.Clear();
		hostEntries.Clear();
		received.Clear();
		ready.Clear();
		HostRaidExternal.Reset();   // BadNorthTogether：清空待开外部波
	}

	public static bool IsWaveReady(int logicalId)
	{
		if (logicalId < 0 || !RaidIdentity.IsCommitted(logicalId))
		{
			return false;
		}
		if (logicalId >= initialCount)
		{
			return ready.Contains(logicalId);
		}
		return true;
	}

	public static bool CanPublishWave(Wave wave)
	{
		if (global::UnityObjectOps.Truthy((Object)(object)wave))
		{
			return IsWaveReady(RaidIdentity.GetWaveId(wave));
		}
		return false;
	}

	internal static void ReleaseWave(int id)
	{
		hostEntries.Remove(id);
		received.Remove(id);
		ready.Remove(id);
	}

	/// <summary>取已捕获清单的签名（外部波开波时用它，避免主机侧船速档位在捕获后改写航线导致签名漂移）。</summary>
	internal static uint SignatureOf(int logicalId)
	{
		HostEntry hostEntry;
		if (hostEntries.TryGetValue(logicalId, out hostEntry) && hostEntry.Manifest != null)
		{
			return hostEntry.Manifest.Signature;
		}
		return 0u;
	}

	// ===== BadNorthTogether：普通岛屿外部波（其它模组在岛内新建的 Wave）=====
	/// <summary>把一个"岛内新建、尚未登记"的波纳入主机清单同步：分配连续逻辑编号 →
	/// 绑定船组/人物 → 生成完整波描述并放入发送队列。失败返回 false（该波退回本机行为）。</summary>
	internal static bool TryAdopt(Wave wave, out int id)
	{
		id = -1;
		if (!Session.IsHost || !Session.PeerSupportsExternalWaves || !global::UnityObjectOps.Truthy((Object)(object)island) || !global::UnityObjectOps.Truthy((Object)(object)wave) || CampaignSync.ClosingBattle)
		{
			return false;
		}
		if (!global::UnityObjectOps.Truthy((Object)(object)island.raid) || island.raid.waves == null)
		{
			return false;
		}
		if (RaidIdentity.IsRegistered(wave) || wave.shipGroups == null || wave.shipGroups.Count == 0)
		{
			return false;
		}
		int candidate = ((nextExternalId > initialCount) ? nextExternalId : initialCount);
		while (candidate <= 65535 && (hostEntries.ContainsKey(candidate) || RaidIdentity.IsWaveRetired(candidate)))
		{
			candidate++;
		}
		if (candidate > 65535)
		{
			Plugin.Log.LogWarning((object)"[主机波清单] 外部波编号已耗尽，改为仅本机");
			return false;
		}
		try
		{
			// 先捕获描述：未完成的登陆点会在这里抛错，此时还没有占用逻辑编号
			Manifest manifest = CaptureExternal(wave, candidate);
			// 外部波由模组自己创建，可能刻意不放进 raid.waves；
			// RegisterExternal 会把它标成“外部波”，让 IsCommitted/Prune 正确放行。
			RaidIdentity.RegisterExternal(wave, candidate);
			foreach (ShipGroup shipGroup in wave.shipGroups)
			{
				AgentAuthority.RegisterGroup(shipGroup, shipGroup.squad);
			}
			hostEntries.Add(candidate, new HostEntry
			{
				Wave = wave,
				Manifest = manifest
			});
			nextExternalId = candidate + 1;
			id = candidate;
			Plugin.Log.LogInfo((object)("[主机波清单] 外部波登记 " + candidate + "（船组 " + wave.shipGroups.Count + "）"));
			return true;
		}
		catch (Exception ex)
		{
			RaidIdentity.UnregisterWave(wave);
			ReleaseWave(candidate);
			Plugin.Log.LogWarning((object)("[主机波清单] 外部波登记失败，改为仅本机：" + ex.Message));
			return false;
		}
	}

	/// <summary>Capture 的宽松版：允许船还没 Spawn（外部波在 BeginWave 之前捕获），其余字段与 Capture 一致。</summary>
	private static Manifest CaptureExternal(Wave wave, int id)
	{
		Manifest manifest = new Manifest
		{
			Id = id,
			Budget = Mathf.Clamp(wave.bounty, 1, 32767),
			Retry = 0,
			Signature = RaidIdentity.WaveSignature(wave),
			Start = SafeFloat(wave.waveStartTime, -1f, 10000000f),
			SpreadGroup = SafeFloat(wave.timeSpreadGroup, 0f, 10000f),
			SpreadShip = SafeFloat(wave.timeSpreadShip, 0f, 10000f),
			Approach = ((wave.approachAudioId == null) ? "" : ((LookupReference)wave.approachAudioId).name),
			Arrival = ((wave.arriveAudioId == null) ? "" : ((LookupReference)wave.arriveAudioId).name)
		};
		foreach (ShipGroup shipGroup in wave.shipGroups)
		{
			GroupData groupData = new GroupData
			{
				Offset = shipGroup.timeOffset
			};
			manifest.Groups.Add(groupData);
			foreach (Landing landing in shipGroup.landings)
			{
				if (!landing.placed || landing.navPos.isNull || !global::UnityObjectOps.Truthy((Object)(object)landing.shipPrefab))
				{
					throw new IOException("外部波还有未完成的登陆点");
				}
				int triangle = Array.IndexOf(island.navMesh.tris, landing.navPos.tri);
				if (triangle < 0)
				{
					throw new IOException("外部波登陆点不在岛屿导航网格上");
				}
				LandingData landingData = new LandingData
				{
					Ship = ((Object)landing.shipPrefab).name,
					Offset = landing.timeOffset,
					Triangle = triangle,
					Position = landing.pos,
					NavPosition = landing.navPos.pos,
					Direction = landing.dir,
					Start = landing.shipTravel.startPos,
					End = landing.shipTravel.endPos,
					Duration = landing.shipTravel.duration,
					FleeDuration = landing.shipTravel.fleeDuration,
					Distance = landing.shipTravel.distance,
					Color = landing.color
				};
				foreach (ShipLoad shipLoad in landing.shipLoads)
				{
					landingData.Loads.Add(new LoadData
					{
						Reference = ((Object)shipLoad.vikingRef).name,
						Agent = ((Object)shipLoad.vikingRef.agent).name,
						Count = shipLoad.count
					});
				}
				groupData.Landings.Add(landingData);
			}
		}
		return manifest;
	}

	private static void TryPatch()
	{
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fe: Expected O, but got Unknown
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_011f: Expected O, but got Unknown
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_0143: Expected O, but got Unknown
		if (patched)
		{
			return;
		}
		// BadNorthTogether：原版船航线反射字段**先**初始化 —— 没有 EndlessFinale 时客机装配外部波也要用
		for (int i = 0; i < travelNames.Length; i++)
		{
			travelFields[i] = AccessTools.Field(typeof(Landing.ShipTravel), travelNames[i]);
			if (travelFields[i] == null)
			{
				throw new IOException("原版船航线结构不兼容：" + travelNames[i]);
			}
		}
		controllerType = AccessTools.TypeByName("EndlessFinale.RollingRaidController");
		if (controllerType == null)
		{
			// 没有无尽模式控制器：普通岛屿照常收发外部波，只是不接管无尽工厂
			patched = true;
			Plugin.Log.LogInfo((object)"[主机波清单] 未检测到 EndlessFinale：普通岛屿外部波同步已启用");
			return;
		}
		Type type = AccessTools.TypeByName("EndlessFinale.NativeRaidFactory");
		MethodInfo methodInfo = AccessTools.Method(controllerType, "EnsureQueue", (Type[])null, (Type[])null);
		MethodInfo methodInfo2 = ((type == null) ? null : AccessTools.Method(type, "GenerateAndAdopt", (Type[])null, (Type[])null));
		acceptWaves = AccessTools.Method(controllerType, "AcceptFactoryWaves", (Type[])null, (Type[])null);
		if (methodInfo == null || methodInfo2 == null || acceptWaves == null || squadField == null)
		{
			// 无尽工厂结构不兼容时也不要把普通岛同步废掉：只关闭无尽工厂接管
			controllerType = null;
			acceptWaves = null;
			patched = true;
			Plugin.Log.LogWarning((object)"[主机波清单] 无尽工厂接口不兼容：仅启用普通岛屿外部波同步");
			return;
		}
		Harmony val = new Harmony("badnorth.together.host-raid-manifest");
		val.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(HostRaidManifest), "BeforeEnsureQueue", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		val.Patch((MethodBase)methodInfo2, new HarmonyMethod(typeof(HostRaidManifest), "BeforeFactory", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		val.Patch((MethodBase)acceptWaves, (HarmonyMethod)null, new HarmonyMethod(typeof(HostRaidManifest), "AfterAccept", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		patched = true;
		Plugin.Log.LogInfo((object)"[主机波清单] 已接管无尽工厂：客户端等待完整波描述，不再独立生成");
	}

	private static bool BeforeEnsureQueue()
	{
		if ((!Session.Active || !CampaignSync.ClosingBattle) && !Session.IsClient)
		{
			return !AgentAuthority.ClientPlayback;
		}
		return false;
	}

	private static bool BeforeFactory(ref IEnumerator __result)
	{
		if ((!Session.Active || !CampaignSync.ClosingBattle) && !Session.IsClient && !AgentAuthority.ClientPlayback)
		{
			return true;
		}
		__result = Empty();
		return false;
	}

	private static IEnumerator Empty()
	{
		yield break;
	}

	private static void AfterAccept(List<Wave> waves, int firstLogicalWaveIndex, int targetBudget, int retryIndex)
	{
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Expected O, but got Unknown
		if (!Session.IsHost || !global::UnityObjectOps.Truthy((Object)(object)island) || CampaignSync.ClosingBattle)
		{
			return;
		}
		try
		{
			for (int i = 0; i < waves.Count; i++)
			{
				int num = firstLogicalWaveIndex + i;
				RaidIdentity.RegisterWave(waves[i], num);
				foreach (ShipGroup shipGroup in waves[i].shipGroups)
				{
					AgentAuthority.RegisterGroup(shipGroup, (Squad)squadField.GetValue(shipGroup));
				}
				Plugin.Log.LogInfo((object)("[主机波清单] 正式登记逻辑波 " + num + "，候选人物已绑定稳定身份"));
				if (hostEntries.ContainsKey(num))
				{
					throw new IOException("主机重复提交逻辑波 " + num);
				}
				Manifest manifest = Capture(waves[i], num, targetBudget, retryIndex);
				hostEntries.Add(num, new HostEntry
				{
					Wave = waves[i],
					Manifest = manifest
				});
			}
			// BadNorthTogether：无尽工厂波与外部波共用同一条连续编号
			if (nextExternalId < firstLogicalWaveIndex + waves.Count)
			{
				nextExternalId = firstLogicalWaveIndex + waves.Count;
			}
		}
		catch (Exception ex)
		{
			AgentAuthority.Fail("主机波清单捕获失败：" + ex);
		}
	}

	public static void Tick()
	{
		if (!Session.Active || !global::UnityObjectOps.Truthy((Object)(object)island) || CampaignSync.ClosingBattle || AgentAuthority.HasFailure)
		{
			return;
		}
		try
		{
			if (Session.IsHost)
			{
				HostRaidExternal.Tick();   // BadNorthTogether：外部波待开队列
				List<int> list = new List<int>(hostEntries.Keys);
				list.Sort();
				{
					foreach (int item in list)
					{
						HostEntry hostEntry = hostEntries[item];
						if (ready.Contains(item))
						{
							continue;
						}
						if (!hostEntry.Sent)
						{
							if (AgentAuthority.Epoch <= 0 || !RaidIdentity.IsCommitted(item))
							{
								continue;
							}
							using (MessageWriter messageWriter = Header(0))
							{
								WriteManifest(messageWriter, hostEntry.Manifest);
								if (!Session.Send(messageWriter.ToArray()))
								{
									throw new IOException("主机波清单发送失败");
								}
							}
							hostEntry.Sent = true;
							hostEntry.SentAt = Time.unscaledTime;
							Plugin.Log.LogInfo((object)("[主机波清单] 已发送逻辑波 " + item + "，等待客户端装配确认"));
						}
						else if (Time.unscaledTime - hostEntry.SentAt > 60f)
						{
							throw new IOException("客户端装配波超时：逻辑波 " + item);
						}
					}
					return;
				}
			}
			if (AgentAuthority.Epoch > 0)
			{
				if (builder == null && pending.Count > 0)
				{
					builder = Build(pending.Dequeue());
				}
				if (builder != null && !builder.MoveNext())
				{
					((IDisposable)builder).Dispose();
					builder = null;
				}
			}
		}
		catch (Exception ex)
		{
			if (builder is IDisposable)
			{
				((IDisposable)builder).Dispose();
			}
			builder = null;
			AgentAuthority.Fail("主机波清单同步失败：" + ex);
		}
	}

	private static MessageWriter Header(byte action)
	{
		MessageWriter messageWriter = Protocol.Begin(MsgType.RaidManifest);
		messageWriter.Write(14);
		messageWriter.Write((byte)1);
		messageWriter.Write(AgentAuthority.Epoch);
		messageWriter.Write(Profile.campaign.lastPlayedLevelIdx);
		messageWriter.Write(action);
		return messageWriter;
	}

	private static void Receive(BinaryReader reader)
	{
		if (reader.ReadInt32() != 14 || reader.ReadByte() != 1)
		{
			throw new IOException("主机波清单协议版本不一致");
		}
		int num = reader.ReadInt32();
		int num2 = reader.ReadInt32();
		byte b = reader.ReadByte();
		if (CampaignSync.ClosingBattle || !global::UnityObjectOps.Truthy((Object)(object)island) || num != AgentAuthority.Epoch || Profile.campaign == null || num2 != Profile.campaign.lastPlayedLevelIdx)
		{
			return;
		}
		switch (b)
		{
		case 0:
		{
			Manifest manifest = ReadManifest(reader);
			reader.RequireEnd();
			if (RaidIdentity.IsWaveRetired(manifest.Id))
			{
				break;
			}
			if (!Session.IsClient)
			{
				throw new IOException("客户端不得发布主机波清单");
			}
			if (received.TryGetValue(manifest.Id, out var value2))
			{
				if (value2 != manifest.Signature)
				{
					throw new IOException("重复波清单内容冲突");
				}
				if (ready.Contains(manifest.Id))
				{
					SendAcknowledgement(manifest);
				}
				break;
			}
			if (manifest.Id != expectedLogicalId || pending.Count >= 8)
			{
				throw new IOException("波清单序号不连续或队列超限：期望 " + expectedLogicalId + "，收到 " + manifest.Id);
			}
			ResolveResources(manifest);
			received.Add(manifest.Id, manifest.Signature);
			expectedLogicalId++;
			pending.Enqueue(manifest);
			break;
		}
		case 1:
		{
			int num3 = reader.ReadInt32();
			uint num4 = reader.ReadUInt32();
			reader.RequireEnd();
			if (!Session.IsHost)
			{
				throw new IOException("主机不得发送客户端装配确认");
			}
			if (!RaidIdentity.IsWaveRetired(num3))
			{
				if (!hostEntries.TryGetValue(num3, out var value) || !value.Sent || value.Manifest.Signature != num4)
				{
					throw new IOException("波清单装配确认不匹配：" + num3);
				}
				if (ready.Add(num3))
				{
					Plugin.Log.LogInfo((object)("[主机波清单] 客户端已装配逻辑波 " + num3 + "；允许发布人物与开波"));
				}
			}
			break;
		}
		default:
			throw new IOException("未知波清单操作");
		}
	}

	private static Manifest Capture(Wave wave, int id, int budget, int retry)
	{
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_016c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Unknown result type (might be due to invalid IL or missing references)
		//IL_018b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0190: Unknown result type (might be due to invalid IL or missing references)
		//IL_019d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e5: Unknown result type (might be due to invalid IL or missing references)
		Manifest manifest = new Manifest
		{
			Id = id,
			Budget = budget,
			Retry = retry,
			Signature = RaidIdentity.WaveSignature(wave),
			Start = wave.waveStartTime,
			SpreadGroup = wave.timeSpreadGroup,
			SpreadShip = wave.timeSpreadShip,
			Approach = ((wave.approachAudioId == null) ? "" : ((LookupReference)wave.approachAudioId).name),
			Arrival = ((wave.arriveAudioId == null) ? "" : ((LookupReference)wave.arriveAudioId).name)
		};
		foreach (ShipGroup shipGroup in wave.shipGroups)
		{
			GroupData groupData = new GroupData
			{
				Offset = shipGroup.timeOffset
			};
			manifest.Groups.Add(groupData);
			foreach (Landing landing in shipGroup.landings)
			{
				if (!landing.placed || landing.navPos.isNull || !global::UnityObjectOps.Truthy((Object)(object)landing.spawnedShip))
				{
					throw new IOException("主机提交未完成的登陆点");
				}
				LandingData landingData = new LandingData
				{
					Ship = ((Object)landing.shipPrefab).name,
					Offset = landing.timeOffset,
					Triangle = Array.IndexOf(island.navMesh.tris, landing.navPos.tri),
					Position = landing.pos,
					NavPosition = landing.navPos.pos,
					Direction = landing.dir,
					Start = landing.shipTravel.startPos,
					End = landing.shipTravel.endPos,
					Duration = landing.shipTravel.duration,
					FleeDuration = landing.shipTravel.fleeDuration,
					Distance = landing.shipTravel.distance,
					Color = landing.color
				};
				foreach (ShipLoad shipLoad in landing.shipLoads)
				{
					landingData.Loads.Add(new LoadData
					{
						Reference = ((Object)shipLoad.vikingRef).name,
						Agent = ((Object)shipLoad.vikingRef.agent).name,
						Count = shipLoad.count
					});
				}
				groupData.Landings.Add(landingData);
			}
		}
		using MessageWriter messageWriter = new MessageWriter();
		WriteManifest(messageWriter, manifest);
		using BinaryReader reader = new BinaryReader(new MemoryStream(messageWriter.ToArray()));
		ReadManifest(reader);
		reader.RequireEnd();
		return manifest;
	}

	private static void ResolveResources(Manifest manifest)
	{
		// BadNorthTogether：普通岛屿没有 RollingRaidController，只校验导航网格
		if (!global::UnityObjectOps.Truthy((Object)(object)island.navMesh))
		{
			throw new IOException("客户端缺少岛屿导航网格");
		}
		foreach (GroupData group in manifest.Groups)
		{
			foreach (LandingData landing in group.Landings)
			{
				if (landing.Triangle >= island.navMesh.tris.Length)
				{
					throw new IOException("登陆点导航三角形不存在");
				}
				foreach (Longship possibleShip in island.levelNode.possibleShips)
				{
					if (global::UnityObjectOps.Truthy((Object)(object)possibleShip) && ((Object)possibleShip).name == landing.Ship)
					{
						if (global::UnityObjectOps.Truthy((Object)(object)landing.Prefab) && (Object)(object)landing.Prefab != (Object)(object)possibleShip)
						{
							throw new IOException("船资源名称不唯一：" + landing.Ship);
						}
						landing.Prefab = possibleShip;
					}
				}
				if (!global::UnityObjectOps.Truthy((Object)(object)landing.Prefab))
				{
					// BadNorthTogether（借鉴 BadNorthMP 的 VisualOnlyPrefab）：levelNode 里没有时按名字兜底
					landing.Prefab = FindAsset<Longship>(landing.Ship);
					if (global::UnityObjectOps.Truthy((Object)(object)landing.Prefab))
					{
						Plugin.Log.LogWarning((object)("[主机波清单] 船资源不在 levelNode 列表，已按名字兜底：" + landing.Ship));
					}
				}
				if (!global::UnityObjectOps.Truthy((Object)(object)landing.Prefab))
				{
					throw new IOException("找不到主机指定船资源：" + landing.Ship + "（两端船资源不一致）");
				}
				foreach (LoadData load in landing.Loads)
				{
					foreach (VikingReference enemy in island.levelNode.enemies)
					{
						if (global::UnityObjectOps.Truthy((Object)(object)enemy) && ((Object)enemy).name == load.Reference && global::UnityObjectOps.Truthy((Object)(object)enemy.agent) && ((Object)enemy.agent).name == load.Agent)
						{
							if (global::UnityObjectOps.Truthy((Object)(object)load.Prefab) && (Object)(object)load.Prefab != (Object)(object)enemy)
							{
								throw new IOException("敌人资源名称不唯一：" + load.Reference);
							}
							load.Prefab = enemy;
						}
					}
					if (!global::UnityObjectOps.Truthy((Object)(object)load.Prefab))
					{
						// BadNorthTogether（借鉴 BadNorthMP 的 VisualOnlyPrefab）：levelNode 里没有时按名字兜底
						load.Prefab = FindEnemy(load.Reference, load.Agent);
						if (global::UnityObjectOps.Truthy((Object)(object)load.Prefab))
						{
							Plugin.Log.LogWarning((object)("[主机波清单] 敌人资源不在 levelNode 列表，已按名字兜底：" + load.Reference + "/" + load.Agent));
						}
					}
					if (!global::UnityObjectOps.Truthy((Object)(object)load.Prefab))
					{
						throw new IOException("找不到主机指定敌人资源：" + load.Reference + "/" + load.Agent + "（两端插件/资源不一致）");
					}
				}
			}
		}
	}

	// BadNorthTogether（借鉴 BadNorthMP 的 VisualOnlyPrefab）：levelNode 列表里找不到时，按名字在运行时资源里兜底，
	// 专门对付"两端插件集不完全一致 / 外生兵种不在 levelNode 列表里"的情况。
	private static T FindAsset<T>(string name) where T : Object
	{
		T[] all = Resources.FindObjectsOfTypeAll<T>();
		for (int i = 0; i < all.Length; i++)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)all[i]) && ((Object)all[i]).name == name)
			{
				return all[i];
			}
		}
		return null;
	}

	private static VikingReference FindEnemy(string reference, string agent)
	{
		VikingReference[] all = Resources.FindObjectsOfTypeAll<VikingReference>();
		for (int i = 0; i < all.Length; i++)
		{
			VikingReference vikingReference = all[i];
			if (global::UnityObjectOps.Truthy((Object)(object)vikingReference) && ((Object)vikingReference).name == reference && global::UnityObjectOps.Truthy((Object)(object)vikingReference.agent) && ((Object)vikingReference.agent).name == agent)
			{
				return vikingReference;
			}
		}
		return null;
	}

	private static IEnumerator Build(Manifest data)
	{
		Wave wave = null;
		bool committed = false;
		try
		{
			wave = new GameObject("TogetherHostWave_" + data.Id).AddComponent<Wave>();
			((Component)wave).transform.SetParent(island.raid.landingContainer, false);
			wave.raid = island.raid;
			wave.waveStartTime = data.Start;
			wave.timeSpreadGroup = data.SpreadGroup;
			wave.timeSpreadShip = data.SpreadShip;
			wave.approachAudioId = (FabricEventReference)(data.Approach);
			wave.arriveAudioId = (FabricEventReference)(data.Arrival);
			RaidIdentity.RegisterWave(wave, data.Id);
			foreach (GroupData group in data.Groups)
			{
				ShipGroup val = new GameObject("Group").AddComponent<ShipGroup>();
				((Component)val).transform.SetParent(((Component)wave).transform, false);
				val.wave = wave;
				wave.shipGroups.Add(val);
				val.timeOffset = group.Offset;
				foreach (LandingData landing in group.Landings)
				{
					Landing val2 = new GameObject("Landing").AddComponent<Landing>();
					val.AddLanding(val2);
					val2.Init(island);
					val2.timeOffset = landing.Offset;
					val2.shipPrefab = landing.Prefab;
					val2.dir = landing.Direction;
					val2.color = landing.Color;
					val2.navPos = new NavPos(island.navMesh.tris[landing.Triangle], landing.NavPosition);
					if (!val2.navPos.isNull)
					{
						Vector3 val3 = val2.navPos.pos - landing.NavPosition;
						if (!(val3.sqrMagnitude > 0.0001f))
						{
							((Component)val2).transform.position = landing.Position;
							((Component)val2).transform.rotation = Quaternion.LookRotation(landing.Direction);
							object obj = (object)default(Landing.ShipTravel);
							object[] array = new object[5] { landing.Start, landing.End, landing.Duration, landing.FleeDuration, landing.Distance };
							for (int i = 0; i < array.Length; i++)
							{
								travelFields[i].SetValue(obj, array[i]);
							}
							val2.shipTravel = (Landing.ShipTravel)obj;
							val2.placed = true;
							foreach (LoadData load in landing.Loads)
							{
								ShipLoad val4 = new GameObject("Load").AddComponent<ShipLoad>();
								val4.vikingRef = load.Prefab;
								val4.count = load.Count;
								val2.AddShipLoad(val4);
							}
							continue;
						}
					}
					throw new IOException("主机登陆点不能映射到客户端导航网格：波 " + data.Id);
				}
			}
			wave.RefreshLandings();
			if (RaidIdentity.WaveSignature(wave) != data.Signature)
			{
				throw new IOException("波清单装配后的编成或航线校验失败");
			}
			foreach (ShipGroup shipGroup in wave.shipGroups)
			{
				foreach (Landing landing2 in shipGroup.landings)
				{
					Longship val5 = landing2.Spawn();
					if (!global::UnityObjectOps.Truthy((Object)(object)val5) || val5.agents.Count != landing2.agentCount)
					{
						throw new IOException("波清单创建船载人物数量不符");
					}
					// BadNorthTogether（借鉴 BadNorthMP 的 CustomWaves）：客机侧的船必须被显式激活并常驻动画，
					// 否则会出现"人到了、船没到场/船不动"。
					Animator shipAnimator = val5.GetComponent<Animator>();
					if (global::UnityObjectOps.Truthy((Object)(object)shipAnimator))
					{
						shipAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
						shipAnimator.enabled = true;
					}
					val5.gameObject.SetActive(true);
					val5.gameObject.AddComponent<ExternalShipWatch>();
					Plugin.Log.LogInfo((object)("[主机波清单] 装配船 " + ((Object)val5).name + "：载员 " + val5.agents.Count + "，动画=" + (global::UnityObjectOps.Truthy((Object)(object)shipAnimator) ? "ok" : "无")));
					SquadEvacuationLocation[] componentsInChildren = ((Component)val5).GetComponentsInChildren<SquadEvacuationLocation>(true);
					for (int j = 0; j < componentsInChildren.Length; j++)
					{
						componentsInChildren[j].Setup();
					}
					yield return null;
				}
			}
			island.raid.waves.Add(wave);
			// BadNorthTogether：普通岛屿没有 RollingRaidController，跳过 EndlessFinale 记账
			if (controllerType != null && acceptWaves != null)
			{
				Component controller = ((Component)island.raid).GetComponent(controllerType);
				if (global::UnityObjectOps.Truthy((Object)(object)controller))
				{
					acceptWaves.Invoke(controller, new object[4]
					{
						new List<Wave> { wave },
						data.Id,
						data.Budget,
						data.Retry
					});
				}
			}
			committed = true;
			ready.Add(data.Id);
			SendAcknowledgement(data);
			Plugin.Log.LogInfo((object)("[主机波清单] 已装配逻辑波 " + data.Id + "，预算 " + wave.bounty + "，船组 " + wave.shipGroups.Count));
		}
		finally
		{
			if (!committed && global::UnityObjectOps.Truthy((Object)(object)wave))
			{
				if (global::UnityObjectOps.Truthy((Object)(object)island) && global::UnityObjectOps.Truthy((Object)(object)island.raid))
				{
					island.raid.waves.Remove(wave);
				}
				foreach (ShipGroup shipGroup2 in wave.shipGroups)
				{
					object value = squadField.GetValue(shipGroup2);
					Squad val6 = (Squad)((value is Squad) ? value : null);
					if (global::UnityObjectOps.Truthy((Object)(object)val6))
					{
						Object.Destroy((Object)(object)((Component)val6).gameObject);
					}
				}
				Object.Destroy((Object)(object)((Component)wave).gameObject);
			}
		}
	}

	private static void SendAcknowledgement(Manifest data)
	{
		using MessageWriter messageWriter = Header(1);
		messageWriter.Write(data.Id);
		messageWriter.Write(data.Signature);
		if (!Session.Send(messageWriter.ToArray()))
		{
			throw new IOException("波清单确认发送失败");
		}
	}

	internal static void WriteManifest(BinaryWriter writer, Manifest data)
	{
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0119: Unknown result type (might be due to invalid IL or missing references)
		//IL_0125: Unknown result type (might be due to invalid IL or missing references)
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		writer.Write(data.Id);
		writer.Write(data.Budget);
		writer.Write(data.Retry);
		writer.Write(data.Signature);
		writer.Write(data.Start);
		writer.Write(data.SpreadGroup);
		writer.Write(data.SpreadShip);
		VisualWire.WriteString(writer, data.Approach, 512);
		VisualWire.WriteString(writer, data.Arrival, 512);
		writer.Write(data.Groups.Count);
		foreach (GroupData group in data.Groups)
		{
			writer.Write(group.Offset);
			writer.Write(group.Landings.Count);
			foreach (LandingData landing in group.Landings)
			{
				VisualWire.WriteString(writer, landing.Ship, 512);
				writer.Write(landing.Offset);
				writer.Write(landing.Triangle);
				WriteVector(writer, landing.Position);
				WriteVector(writer, landing.NavPosition);
				WriteVector(writer, landing.Direction);
				WriteVector(writer, landing.Start);
				WriteVector(writer, landing.End);
				writer.Write(landing.Duration);
				writer.Write(landing.FleeDuration);
				writer.Write(landing.Distance);
				writer.Write(landing.Color.r);
				writer.Write(landing.Color.g);
				writer.Write(landing.Color.b);
				writer.Write(landing.Color.a);
				writer.Write(landing.Loads.Count);
				foreach (LoadData load in landing.Loads)
				{
					VisualWire.WriteString(writer, load.Reference, 512);
					VisualWire.WriteString(writer, load.Agent, 512);
					writer.Write(load.Count);
				}
			}
		}
	}

	internal static Manifest ReadManifest(BinaryReader reader)
	{
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0172: Unknown result type (might be due to invalid IL or missing references)
		//IL_0177: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0183: Unknown result type (might be due to invalid IL or missing references)
		//IL_018a: Unknown result type (might be due to invalid IL or missing references)
		//IL_018f: Unknown result type (might be due to invalid IL or missing references)
		//IL_021a: Unknown result type (might be due to invalid IL or missing references)
		//IL_021f: Unknown result type (might be due to invalid IL or missing references)
		//IL_026a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0271: Unknown result type (might be due to invalid IL or missing references)
		//IL_0276: Unknown result type (might be due to invalid IL or missing references)
		//IL_027b: Unknown result type (might be due to invalid IL or missing references)
		Manifest manifest = new Manifest
		{
			Id = ReadInt(reader, 0, 65535),
			Budget = ReadInt(reader, 1, 32767),
			Retry = ReadInt(reader, 0, 100000),
			Signature = reader.ReadUInt32(),
			Start = ReadFloat(reader, -1f, 10000000f),
			SpreadGroup = ReadFloat(reader, 0f, 10000f),
			SpreadShip = ReadFloat(reader, 0f, 10000f),
			Approach = VisualWire.ReadString(reader, 512),
			Arrival = VisualWire.ReadString(reader, 512)
		};
		int num = ReadInt(reader, 1, 64);
		int num2 = 0;
		int num3 = 0;
		for (int i = 0; i < num; i++)
		{
			GroupData groupData = new GroupData
			{
				Offset = ReadFloat(reader, -10000f, 10000f)
			};
			manifest.Groups.Add(groupData);
			int num4 = ReadInt(reader, 1, 128);
			num2 += num4;
			if (num2 > 128)
			{
				throw new IOException("波清单船只总数超限");
			}
			int num5 = 0;
			while (num5 < num4)
			{
				LandingData landingData = new LandingData
				{
					Ship = VisualWire.ReadString(reader, 512),
					Offset = ReadFloat(reader, -10000f, 10000f),
					Triangle = ReadInt(reader, 0, 1000000),
					Position = ReadVector(reader),
					NavPosition = ReadVector(reader),
					Direction = ReadVector(reader),
					Start = ReadVector(reader),
					End = ReadVector(reader),
					Duration = ReadFloat(reader, 0.001f, 100000f),
					FleeDuration = ReadFloat(reader, 0.001f, 100000f),
					Distance = ReadFloat(reader, 0.001f, 10000f)
				};
				landingData.Color = new Color(ReadFloat(reader, 0f, 1f), ReadFloat(reader, 0f, 1f), ReadFloat(reader, 0f, 1f), ReadFloat(reader, 0f, 1f));
				if (landingData.Ship.Length != 0 && !(Math.Abs(landingData.Direction.sqrMagnitude - 1f) > 0.01f) && !(Math.Abs(landingData.Direction.y) > 0.001f))
				{
					Vector3 val = landingData.End - landingData.Start;
					if (!(Math.Abs(val.magnitude - landingData.Distance) > 0.02f))
					{
						int num6 = ReadInt(reader, 1, 16);
						for (int j = 0; j < num6; j++)
						{
							LoadData loadData = new LoadData
							{
								Reference = VisualWire.ReadString(reader, 512),
								Agent = VisualWire.ReadString(reader, 512),
								Count = ReadInt(reader, 1, 8192)
							};
							num3 += loadData.Count;
							if (num3 > 8192 || loadData.Reference.Length == 0 || loadData.Agent.Length == 0)
							{
								throw new IOException("波清单敌人载荷非法");
							}
							landingData.Loads.Add(loadData);
						}
						groupData.Landings.Add(landingData);
						num5++;
						continue;
					}
				}
				throw new IOException("波清单船资源、方向或航程非法");
			}
		}
		return manifest;
	}

	private static float SafeFloat(float value, float min, float max)
	{
		if (float.IsNaN(value) || float.IsInfinity(value))
		{
			return 0f;
		}
		return Mathf.Clamp(value, min, max);
	}

	private static int ReadInt(BinaryReader reader, int min, int max)
	{
		int num = reader.ReadInt32();
		if (num < min || num > max)
		{
			throw new IOException("波清单整数越界：" + num);
		}
		return num;
	}

	private static float ReadFloat(BinaryReader reader, float min, float max)
	{
		float num = reader.ReadSingle();
		if (float.IsNaN(num) || float.IsInfinity(num) || num < min || num > max)
		{
			throw new IOException("波清单数值非法");
		}
		return num;
	}

	private static void WriteVector(BinaryWriter writer, Vector3 value)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		writer.Write(value.x);
		writer.Write(value.y);
		writer.Write(value.z);
	}

	private static Vector3 ReadVector(BinaryReader reader)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		return new Vector3(ReadFloat(reader, -10000f, 10000f), ReadFloat(reader, -10000f, 10000f), ReadFloat(reader, -10000f, 10000f));
	}
}
