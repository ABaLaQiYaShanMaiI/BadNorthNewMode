using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Together.Net;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace Together;

internal static class WaveSync
{
	private sealed class PendingWave
	{
		public int Id;

		public uint Signature;

		public float ReceivedAt;
	}

	private static readonly FieldInfo waveIndexField = AccessTools.Field(typeof(Raid), "waveIndex");

	private static readonly Queue<PendingWave> pendingWaves = new Queue<PendingWave>();

	private static readonly Queue<int> pendingHostWaves = new Queue<int>();

	private static readonly HashSet<int> waitingHostWaves = new HashSet<int>();

	private static bool initialized;

	private static bool applyingRemote;

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			Session.On(MsgType.WaveBegin, ReceiveWave);
			Session.Stopped += BeginIsland;
		}
	}

	public static void BeginIsland()
	{
		pendingWaves.Clear();
		pendingHostWaves.Clear();
		waitingHostWaves.Clear();
		applyingRemote = false;
	}

	public static void Tick()
	{
		if (!Session.Active || CampaignSync.ClosingBattle || !DeploySync.BattleStarted || AgentAuthority.HasFailure)
		{
			return;
		}
		IslandGameplayManager instance = Singleton<IslandGameplayManager>.instance;
		if ((Object)(object)instance == (Object)null || (Object)(object)instance.island == (Object)null)
		{
			return;
		}
		Raid raid = instance.island.raid;
		if (Session.IsHost)
		{
			while (pendingHostWaves.Count > 0)
			{
				int num = pendingHostWaves.Peek();
				Wave val = RaidIdentity.FindWave(num);
				if (!global::UnityObjectOps.Truthy((Object)(object)val) || val.triggered)
				{
					pendingHostWaves.Dequeue();
					waitingHostWaves.Remove(num);
					continue;
				}
				if (!HostRaidManifest.CanPublishWave(val))
				{
					break;
				}
				int num2 = raid.waves.IndexOf(val);
				if (num2 < 0)
				{
					break;
				}
				pendingHostWaves.Dequeue();
				waitingHostWaves.Remove(num);
				waveIndexField.SetValue(raid, num2);
				raid.BeginNextWave();
			}
			return;
		}
		while (pendingWaves.Count > 0)
		{
			PendingWave pendingWave = pendingWaves.Peek();
			Wave val2 = RaidIdentity.FindWave(pendingWave.Id);
			int num3 = (global::UnityObjectOps.Truthy((Object)(object)val2) ? raid.waves.IndexOf(val2) : (-1));
			if (num3 < 0 || !HostRaidManifest.IsWaveReady(pendingWave.Id))
			{
				if (Time.unscaledTime - pendingWave.ReceivedAt > 45f)
				{
					AgentAuthority.Fail("等待逻辑波生成超时：" + pendingWave.Id);
				}
				break;
			}
			if (RaidIdentity.WaveSignature(val2) != pendingWave.Signature)
			{
				AgentAuthority.Fail("逻辑波编成/航线校验不同：" + pendingWave.Id);
				break;
			}
			pendingWaves.Dequeue();
			if (!val2.triggered)
			{
				waveIndexField.SetValue(raid, num3);
				applyingRemote = true;
				try
				{
					raid.BeginNextWave();
				}
				finally
				{
					applyingRemote = false;
				}
				Plugin.Log.LogInfo((object)("[波次] 客户端按主机命令开启逻辑波 " + pendingWave.Id + "（当前队列位置 " + num3 + "）"));
			}
		}
	}

	public static bool BeforeMaybeLaunch(Raid raid, ref bool result)
	{
		if (Session.Active && CampaignSync.ClosingBattle)
		{
			result = false;
			return false;
		}
		if (!Session.Active || !DeploySync.BattleStarted)
		{
			return true;
		}
		if (AgentAuthority.HasFailure)
		{
			result = false;
			return false;
		}
		if (Session.IsClient)
		{
			result = false;
			return false;
		}
		int num = (int)waveIndexField.GetValue(raid);
		if (num >= 0 && num < raid.waves.Count && !raid.waves[num].triggered && !HostRaidManifest.CanPublishWave(raid.waves[num]))
		{
			result = false;
			return false;
		}
		return true;
	}

	public static bool BeforeBeginWave(Raid raid)
	{
		if (Session.Active && CampaignSync.ClosingBattle)
		{
			return false;
		}
		if (!Session.Active || !DeploySync.BattleStarted)
		{
			return true;
		}
		if (AgentAuthority.HasFailure)
		{
			return false;
		}
		if (Session.IsClient)
		{
			return applyingRemote;
		}
		int num = (int)waveIndexField.GetValue(raid);
		if (num < 0 || num >= raid.waves.Count)
		{
			return true;
		}
		Wave wave = raid.waves[num];
		int waveId = RaidIdentity.GetWaveId(wave);
		if (waveId < 0)
		{
			AgentAuthority.Fail("待开波缺少稳定逻辑身份");
			return false;
		}
		if (!HostRaidManifest.CanPublishWave(wave))
		{
			if (waitingHostWaves.Add(waveId))
			{
				pendingHostWaves.Enqueue(waveId);
			}
			return false;
		}
		using (MessageWriter messageWriter = Protocol.Begin(MsgType.WaveBegin))
		{
			messageWriter.Write(14);
			messageWriter.Write(AgentAuthority.Epoch);
			messageWriter.Write(Profile.campaign.lastPlayedLevelIdx);
			messageWriter.Write(waveId);
			messageWriter.Write(RaidIdentity.WaveSignature(wave));
			if (!Session.Send(messageWriter.ToArray()))
			{
				AgentAuthority.Fail("开波命令发送失败");
				return false;
			}
		}
		Plugin.Log.LogInfo((object)("[波次] 主机开启逻辑波 " + waveId + "（当前队列位置 " + num + "）"));
		return true;
	}

	/// <summary>BadNorthTogether：外部波不走 Raid.BeginNextWave，需要单独发一条开波命令。
	/// 客户端收到后会把自己那份同编号的波 BeginNextWave() 起来。</summary>
	internal static void SendExternalBegin(Wave wave, int logicalId)
	{
		if (!Session.IsHost || !Session.Active || CampaignSync.ClosingBattle || !global::UnityObjectOps.Truthy((Object)(object)wave))
		{
			return;
		}
		using (MessageWriter messageWriter = Protocol.Begin(MsgType.WaveBegin))
		{
			messageWriter.Write(14);
			messageWriter.Write(AgentAuthority.Epoch);
			messageWriter.Write(Profile.campaign.lastPlayedLevelIdx);
			messageWriter.Write(logicalId);
			uint signature = HostRaidManifest.SignatureOf(logicalId);
			messageWriter.Write((signature != 0u) ? signature : RaidIdentity.WaveSignature(wave));
			if (!Session.Send(messageWriter.ToArray()))
			{
				AgentAuthority.Fail("开波命令发送失败");
				return;
			}
		}
		Plugin.Log.LogInfo((object)("[波次] 主机开启外部逻辑波 " + logicalId));
	}

	private static void ReceiveWave(BinaryReader reader)
	{
		if (reader.ReadInt32() != 14)
		{
			throw new IOException("波次消息版本不一致");
		}
		int num = reader.ReadInt32();
		int num2 = reader.ReadInt32();
		int num3 = reader.ReadInt32();
		uint signature = reader.ReadUInt32();
		if (!CampaignSync.ClosingBattle && !RaidIdentity.IsWaveRetired(num3) && Session.IsClient && Profile.campaign != null && num == AgentAuthority.Epoch && num2 == Profile.campaign.lastPlayedLevelIdx)
		{
			if (num3 < 0 || num3 > 65535 || pendingWaves.Count > 1024)
			{
				throw new IOException("逻辑波命令非法或队列过长");
			}
			pendingWaves.Enqueue(new PendingWave
			{
				Id = num3,
				Signature = signature,
				ReceivedAt = Time.unscaledTime
			});
		}
	}
}
