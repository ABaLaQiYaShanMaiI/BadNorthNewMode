using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace Together;

/// <summary>BadNorthTogether：普通岛屿外部波接管。
///
/// 背景：Together 原版只在「终岛（isEnd）」建立 RollingRaidController 清单通道，
/// 普通岛屿上由其它模组新建的 Wave 客机完全看不到。这里补上普通岛屿的通道：
///
///   1) 其它模组调用 `wave.BeginWave()` 时，如果联机战斗基线已建立（Epoch&gt;0、部署已完成），
///      先拦下本机开波 → `HostRaidManifest.TryAdopt` 分配连续逻辑编号、绑定船组、生成完整波描述并发送；
///   2) 等客机装配确认（ready）后，恢复本机开波，并单独发一条 WaveBegin 让客机把自己的那份波也开起来。
///
/// 设计约束（保持 Together 原有行为）：
///   * 原版波在 BeginIsland 时已登记编号 → 不接管；
///   * 未建立战斗基线（进岛后才开房）→ 不接管，退回"仅本机"；
///   * 客机 / 回放端 / 已判定失败 / 正在结算 → 不接管；
///   * 登记失败（登陆点未完成等）→ 不阻塞战斗，退回本机行为并记日志。
/// </summary>
[HarmonyPatch(typeof(Wave), "BeginWave")]
internal static class HostRaidExternal
{
	private sealed class Held
	{
		public Wave Wave;

		public int Id;
	}

	private static readonly List<Held> held = new List<Held>();

	private static bool starting;

	private static bool warnedUnsupported;

	public static void Reset()
	{
		held.Clear();
		starting = false;
		warnedUnsupported = false;
	}

	private static IEnumerator Empty()
	{
		yield break;
	}

	[HarmonyPrefix]
	private static bool Prefix(Wave __instance, ref IEnumerator __result)
	{
		if (starting || !Session.IsHost || !Session.Active || CampaignSync.ClosingBattle || AgentAuthority.HasFailure)
		{
			return true;
		}
		if (!Session.PeerSupportsExternalWaves)
		{
			if (!warnedUnsupported)
			{
				warnedUnsupported = true;
				Plugin.Log.LogInfo((object)"[波次] 对端未确认支持普通岛外部波，外部波改为仅主机可见");
			}
			return true;
		}
		if (!DeploySync.BattleStarted || AgentAuthority.Epoch <= 0 || AgentAuthority.ClientPlayback)
		{
			return true;
		}
		IslandGameplayManager instance = Singleton<IslandGameplayManager>.instance;
		if (!global::UnityObjectOps.Truthy((Object)(object)instance) || !global::UnityObjectOps.Truthy((Object)(object)instance.island) || !global::UnityObjectOps.Truthy((Object)(object)__instance) || !global::UnityObjectOps.Truthy((Object)(object)__instance.raid) || (Object)(object)__instance.raid != (Object)(object)instance.island.raid)
		{
			return true;
		}
		for (int i = 0; i < held.Count; i++)
		{
			if ((Object)(object)held[i].Wave == (Object)(object)__instance)
			{
				__result = Empty();
				return false;
			}
		}
		if (!HostRaidManifest.TryAdopt(__instance, out var id))
		{
			return true;
		}
		held.Add(new Held
		{
			Wave = __instance,
			Id = id
		});
		__result = Empty();
		Plugin.Log.LogInfo((object)("[波次] 已接管外部波 " + id + "，等待客机装配"));
		return false;
	}

	/// <summary>主机每帧：把"客机已装配"的外部波真正开起来（并通知客机开波）。</summary>
	public static void Tick()
	{
		if (held.Count == 0)
		{
			return;
		}
		for (int i = 0; i < held.Count; i++)
		{
			Held item = held[i];
			if (!global::UnityObjectOps.Truthy((Object)(object)item.Wave) || !global::UnityObjectOps.Truthy((Object)(object)item.Wave.raid))
			{
				held.RemoveAt(i--);
				continue;
			}
			if (!HostRaidManifest.IsWaveReady(item.Id))
			{
				continue;
			}
			held.RemoveAt(i--);
			starting = true;
			try
			{
				item.Wave.raid.StartCoroutine(item.Wave.BeginWave());
				WaveSync.SendExternalBegin(item.Wave, item.Id);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning((object)("[波次] 外部波开波失败：" + ex.Message));
			}
			finally
			{
				starting = false;
			}
		}
	}
}
