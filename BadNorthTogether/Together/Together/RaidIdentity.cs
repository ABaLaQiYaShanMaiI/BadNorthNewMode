using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace Together;

internal static class RaidIdentity
{
	internal const int MaxWaveId = 65535;

	internal const int MaxGroupId = 63;

	internal const int MaxLandingId = 255;

	private static readonly RetiredIds retiredWaves = new RetiredIds(1025);

	private static Island island;

	private static readonly Dictionary<Wave, int> waveIds = new Dictionary<Wave, int>();

	private static readonly Dictionary<int, Wave> waves = new Dictionary<int, Wave>();

	private static readonly Dictionary<Longship, int> shipIds = new Dictionary<Longship, int>();

	// BadNorthTogether：外部波（其它模组在岛内新建的波）不在 raid.waves 里，
	// 不能被 Prune 当成"已触发但不在波列表"的孤儿退休（否则人物永远发布不出去）。
	private static readonly HashSet<int> externalWaves = new HashSet<int>();

	private static Type controllerType;

	private static FieldInfo runtimeWavesField;

	public static void BeginIsland(Island value)
	{
		Reset();
		island = value;
		if (global::UnityObjectOps.Truthy((Object)(object)island) && global::UnityObjectOps.Truthy((Object)(object)island.raid) && island.raid.waves != null)
		{
			for (int i = 0; i < island.raid.waves.Count; i++)
			{
				RegisterWave(island.raid.waves[i], i);
			}
		}
	}

	public static void Reset()
	{
		island = null;
		waveIds.Clear();
		waves.Clear();
		shipIds.Clear();
		retiredWaves.Clear();
		externalWaves.Clear();
	}

	/// <summary>BadNorthTogether：登记"岛内新建的外部波"。与 RegisterWave 的差别只有一点：
	/// 记进 externalWaves，让 Prune 不要因为"它不在 raid.waves 里"就把它退休。</summary>
	public static void RegisterExternal(Wave wave, int logicalId)
	{
		RegisterWave(wave, logicalId);
		externalWaves.Add(logicalId);
	}

	public static void RegisterWave(Wave wave, int logicalId)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)wave) || logicalId < 0 || logicalId > 65535)
		{
			throw new IOException("逻辑波编号超出有效范围：" + logicalId);
		}
		if (retiredWaves.Contains(logicalId))
		{
			throw new IOException("已经退休的逻辑波不可重新登记：" + logicalId);
		}
		if (wave.shipGroups.Count > 64)
		{
			throw new IOException("波船组数量超过稳定身份预算");
		}
		foreach (ShipGroup shipGroup in wave.shipGroups)
		{
			if (shipGroup.landings.Count > 256)
			{
				throw new IOException("船组登陆点数量超过稳定身份预算");
			}
		}
		if (waveIds.TryGetValue(wave, out var value))
		{
			if (value != logicalId)
			{
				throw new IOException("同一波的逻辑编号被改变");
			}
			return;
		}
		if (waves.TryGetValue(logicalId, out var value2) && global::UnityObjectOps.Truthy((Object)(object)value2) && (Object)(object)value2 != (Object)(object)wave)
		{
			throw new IOException("逻辑波编号重复：" + logicalId);
		}
		waveIds.Add(wave, logicalId);
		waves[logicalId] = wave;
	}

	public static bool IsRegistered(Wave wave)
	{
		return global::UnityObjectOps.Truthy((Object)(object)wave) && waveIds.ContainsKey(wave);
	}

	internal static void UnregisterWave(Wave wave)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)wave))
		{
			return;
		}
		if (waveIds.TryGetValue(wave, out var value))
		{
			waveIds.Remove(wave);
			externalWaves.Remove(value);
			if (waves.TryGetValue(value, out var value2) && (Object)(object)value2 == (Object)(object)wave)
			{
				waves.Remove(value);
			}
		}
	}

	public static int GetWaveId(Wave wave)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)wave) || !global::UnityObjectOps.Truthy((Object)(object)island) || (Object)(object)wave.raid != (Object)(object)island.raid)
		{
			return -1;
		}
		if (waveIds.TryGetValue(wave, out var value))
		{
			return value;
		}
		Component val = FindController();
		if (global::UnityObjectOps.Truthy((Object)(object)val))
		{
			IDictionary dictionary = ((runtimeWavesField == null) ? null : (runtimeWavesField.GetValue(val) as IDictionary));
			if (dictionary == null || !dictionary.Contains(wave))
			{
				return -1;
			}
			object obj = dictionary[wave];
			value = (int)(AccessTools.Field(obj.GetType(), "LogicalIndex") ?? throw new IOException("无尽模式逻辑波结构不兼容")).GetValue(obj);
		}
		else
		{
			value = island.raid.waves.IndexOf(wave);
		}
		if (value < 0 || retiredWaves.Contains(value))
		{
			return -1;
		}
		RegisterWave(wave, value);
		return value;
	}

	private static Component FindController()
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)island) || !global::UnityObjectOps.Truthy((Object)(object)island.raid))
		{
			return null;
		}
		if (controllerType == null)
		{
			controllerType = AccessTools.TypeByName("EndlessFinale.RollingRaidController");
			if (controllerType == null)
			{
				return null;
			}
			runtimeWavesField = AccessTools.Field(controllerType, "runtimeWaves");
		}
		return ((Component)island.raid).GetComponent(controllerType);
	}

	public static bool TryGetGroup(ShipGroup group, out int waveId, out int groupIndex)
	{
		waveId = -1;
		groupIndex = -1;
		if (!global::UnityObjectOps.Truthy((Object)(object)group) || !global::UnityObjectOps.Truthy((Object)(object)group.wave))
		{
			return false;
		}
		waveId = GetWaveId(group.wave);
		groupIndex = group.wave.shipGroups.IndexOf(group);
		if (waveId >= 0)
		{
			return groupIndex >= 0;
		}
		return false;
	}

	public static Wave FindWave(int logicalId)
	{
		if (waves.TryGetValue(logicalId, out var value) && global::UnityObjectOps.Truthy((Object)(object)value))
		{
			return value;
		}
		if (!global::UnityObjectOps.Truthy((Object)(object)island) || !global::UnityObjectOps.Truthy((Object)(object)island.raid) || island.raid.waves == null)
		{
			return null;
		}
		foreach (Wave wave in island.raid.waves)
		{
			GetWaveId(wave);
		}
		if (!waves.TryGetValue(logicalId, out value) || !global::UnityObjectOps.Truthy((Object)(object)value))
		{
			return null;
		}
		return value;
	}

	public static bool IsCommitted(int logicalId)
	{
		Wave val = FindWave(logicalId);
		if (global::UnityObjectOps.Truthy((Object)(object)val) && global::UnityObjectOps.Truthy((Object)(object)island) && global::UnityObjectOps.Truthy((Object)(object)island.raid))
		{
			return externalWaves.Contains(logicalId) || island.raid.waves.Contains(val);
		}
		return false;
	}

	public static int GetShipId(Longship ship)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)ship) || !global::UnityObjectOps.Truthy((Object)(object)ship.landing))
		{
			return -1;
		}
		if (shipIds.TryGetValue(ship, out var value))
		{
			return value;
		}
		Landing landing = ship.landing;
		if (global::UnityObjectOps.Truthy((Object)(object)landing.shipGroup))
		{
			if (!TryGetGroup(landing.shipGroup, out var waveId, out var groupIndex))
			{
				return -1;
			}
			int num = landing.shipGroup.landings.IndexOf(landing);
			if (groupIndex < 0 || groupIndex > 63 || num < 0 || num > 255)
			{
				throw new IOException("船组或登陆点编号超出有效范围");
			}
			value = EncodeShip(waveId, groupIndex, num);
		}
		else
		{
			int num2 = -1;
			foreach (Agent agent in ship.agents)
			{
				if (global::UnityObjectOps.Truthy((Object)(object)agent))
				{
					Squad squad = agent.squad;
					EnglishSquad val = (EnglishSquad)(object)((squad is EnglishSquad) ? squad : null);
					if (!global::UnityObjectOps.Truthy((Object)(object)val) || val.hero == null)
					{
						return -1;
					}
					if (num2 >= 0 && num2 != val.hero.id)
					{
						throw new IOException("友军船包含多个英雄小队");
					}
					num2 = val.hero.id;
				}
			}
			if (num2 < 0)
			{
				return -1;
			}
			if (num2 >= 1073741824)
			{
				throw new IOException("友军船英雄编号超出有效范围");
			}
			value = 0x40000000 | num2;
		}
		shipIds[ship] = value;
		return value;
	}

	internal static int EncodeShip(int wave, int group, int landing)
	{
		if (wave < 0 || wave > 65535 || group < 0 || group > 63 || landing < 0 || landing > 255)
		{
			throw new IOException("稳定敌船身份超出范围：波=" + wave + " 组=" + group + " 登陆=" + landing);
		}
		return (wave << 14) | (group << 8) | landing;
	}

	internal static bool IsWaveRetired(int id)
	{
		return retiredWaves.Contains(id);
	}

	internal static bool IsShipRetired(int id)
	{
		if (id >= 0 && id < 1073741824)
		{
			return retiredWaves.Contains(id >> 14);
		}
		return false;
	}

	public static void Prune()
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)island) || !global::UnityObjectOps.Truthy((Object)(object)island.raid))
		{
			return;
		}
		List<int> list = new List<int>();
		foreach (KeyValuePair<int, Wave> wave in waves)
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)wave.Value) || (wave.Value.triggered && !island.raid.waves.Contains(wave.Value) && !externalWaves.Contains(wave.Key)))
			{
				list.Add(wave.Key);
			}
		}
		foreach (int item in list)
		{
			Wave val = waves[item];
			waves.Remove(item);
			if (val != null)
			{
				waveIds.Remove(val);
			}
			retiredWaves.Add(item);
			HostRaidManifest.ReleaseWave(item);
			WorldAuthority.ReleaseWave(item);
		}
		foreach (Longship item2 in new List<Longship>(shipIds.Keys))
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)item2) || IsShipRetired(shipIds[item2]))
			{
				shipIds.Remove(item2);
			}
		}
	}

	public static uint WaveSignature(Wave wave)
	{
		uint hash = 2166136261u;
		hash = Mix(hash, wave.shipGroups.Count);
		foreach (ShipGroup shipGroup in wave.shipGroups)
		{
			hash = Mix(hash, shipGroup.landings.Count);
			foreach (Landing landing in shipGroup.landings)
			{
				hash = Mix(hash, (int)LandingSignature(landing));
			}
		}
		return hash;
	}

	public static uint LandingSignature(Landing landing)
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		uint hash = 2166136261u;
		hash = MixString(hash, global::UnityObjectOps.Truthy((Object)(object)landing.shipPrefab) ? ((Object)landing.shipPrefab).name : "");
		hash = MixVector(hash, landing.shipTravel.startPos);
		hash = MixVector(hash, landing.shipTravel.endPos);
		hash = MixVector(hash, landing.dir);
		hash = Mix(hash, Mathf.RoundToInt(landing.shipTravel.duration * 1000f));
		hash = Mix(hash, landing.shipLoads.Count);
		foreach (ShipLoad shipLoad in landing.shipLoads)
		{
			hash = Mix(hash, shipLoad.count);
			hash = MixString(hash, (global::UnityObjectOps.Truthy((Object)(object)shipLoad.vikingRef) && global::UnityObjectOps.Truthy((Object)(object)shipLoad.vikingRef.agent)) ? ((Object)shipLoad.vikingRef.agent).name : "");
		}
		return hash;
	}

	private static uint MixVector(uint hash, Vector3 value)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		return Mix(Mix(Mix(hash, Mathf.RoundToInt(value.x * 1000f)), Mathf.RoundToInt(value.y * 1000f)), Mathf.RoundToInt(value.z * 1000f));
	}

	private static uint MixString(uint hash, string value)
	{
		hash = Mix(hash, value.Length);
		foreach (char value2 in value)
		{
			hash = Mix(hash, value2);
		}
		return hash;
	}

	private static uint Mix(uint hash, int value)
	{
		return (hash ^ (uint)value) * 16777619;
	}
}
