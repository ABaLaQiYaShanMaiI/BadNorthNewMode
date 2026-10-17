using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Together.Net;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;

namespace Together;

internal static class FireVisuals
{
	private sealed class ResidueSource
	{
		public FireVisualData Data;

		public float End;
	}

	private sealed class View
	{
		public GameObject Root;

		public Transform Parent;

		public FireVisualData Data;

		public ParticleSystem[] Particles;

		public float Time;
	}

	[HarmonyPatch]
	private static class BurnSetupPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			MethodInfo methodInfo = ((burnType == null) ? null : AccessTools.Method(burnType, "Setup", (Type[])null, (Type[])null));
			if (methodInfo != null)
			{
				yield return methodInfo;
			}
		}

		private static void Postfix(Component __instance)
		{
			TrackBurn(__instance);
		}
	}

	[HarmonyPatch]
	private static class BurnEndPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			MethodInfo methodInfo = ((burnType == null) ? null : AccessTools.Method(burnType, "OnDestroy", (Type[])null, (Type[])null));
			if (methodInfo != null)
			{
				yield return methodInfo;
			}
		}

		private static void Prefix(Component __instance)
		{
			StopBurn(__instance);
		}
	}

	[HarmonyPatch]
	private static class ResiduePatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			MethodInfo methodInfo = ((arrowType == null) ? null : AccessTools.Method(arrowType, "SpawnGhostFire", (Type[])null, (Type[])null));
			if (methodInfo != null)
			{
				yield return methodInfo;
			}
		}

		private static bool Prefix()
		{
			spawnedGhost = null;
			return !AgentAuthority.ClientPlayback;
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			foreach (CodeInstruction instruction in instructions)
			{
				yield return instruction;
				MethodInfo methodInfo = instruction.operand as MethodInfo;
				if (instruction.opcode == OpCodes.Call && methodInfo != null && methodInfo.DeclaringType == typeof(Object) && methodInfo.Name == "Instantiate" && methodInfo.IsGenericMethod && methodInfo.GetGenericArguments()[0] == typeof(GameObject))
				{
					yield return new CodeInstruction(OpCodes.Dup, (object)null);
					yield return new CodeInstruction(OpCodes.Call, (object)AccessTools.Method(typeof(FireVisuals), "CaptureGhost", (Type[])null, (Type[])null));
				}
			}
		}

		private static void Postfix(Component __instance, Vector3 pos, Quaternion rot)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			Residue(__instance, pos, rot);
		}
	}

	private static readonly Type modType = AccessTools.TypeByName("Voxels.TowerDefense.FireArrowMod");

	private static readonly Type arrowType = AccessTools.TypeByName("Voxels.TowerDefense.ArrowFireEffect");

	private static readonly Type burnType = AccessTools.TypeByName("Voxels.TowerDefense.BurningAgent");

	private static readonly FieldInfo prefabField = Field(modType, "FirePrefab");

	private static readonly FieldInfo arrowFire = Field(arrowType, "isFire");

	private static readonly FieldInfo arrowInstance = Field(arrowType, "fireInstance");

	private static readonly FieldInfo burnInstance = Field(burnType, "fire");

	private static readonly FieldInfo remaining = Field(burnType, "remainingTime");

	private static readonly Dictionary<int, bool> arrows = new Dictionary<int, bool>();

	private static readonly Dictionary<string, FireVisualData> captured = new Dictionary<string, FireVisualData>(StringComparer.Ordinal);

	private static readonly Dictionary<Component, int> burning = new Dictionary<Component, int>();

	private static readonly HashSet<Component> dirtyBurns = new HashSet<Component>();

	private static readonly Dictionary<string, View> views = new Dictionary<string, View>(StringComparer.Ordinal);

	private static readonly Queue<GameObject> pool = new Queue<GameObject>();

	private static readonly VisualEventQueue<FireVisualData> pending = new VisualEventQueue<FireVisualData>();

	private static GameObject staging;

	private static GameObject spawnedGhost;

	private static readonly Dictionary<GameObject, ResidueSource> residues = new Dictionary<GameObject, ResidueSource>();

	private static int sequence;

	private static int lastSequence;

	private static int nextResidue;

	private static bool initialized;

	private static GameObject Template
	{
		get
		{
			if (prefabField != null)
			{
				object value = prefabField.GetValue(null);
				return (GameObject)((value is GameObject) ? value : null);
			}
			return null;
		}
	}

	private static FieldInfo Field(Type type, string name)
	{
		if (type != null)
		{
			return AccessTools.Field(type, name);
		}
		return null;
	}

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			Session.On(MsgType.FireVisual, Receive);
			Session.Stopped += Reset;
			Session.OptionalStopped += Reset;
			Plugin.Log.LogInfo((object)("[火焰表现] Fire适配=" + (prefabField != null && arrowFire != null && burnInstance != null)));
		}
	}

	public static void Reset()
	{
		foreach (View value in views.Values)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)value.Root))
			{
				Object.Destroy((Object)(object)value.Root);
			}
		}
		if (global::UnityObjectOps.Truthy((Object)(object)staging))
		{
			Object.Destroy((Object)(object)staging);
		}
		staging = (spawnedGhost = null);
		residues.Clear();
		pool.Clear();
		views.Clear();
		arrows.Clear();
		burning.Clear();
		dirtyBurns.Clear();
		pending.Clear();
		captured.Clear();
		sequence = (lastSequence = (nextResidue = 0));
	}

	private static void Send(FireVisualData value)
	{
		value.Time = AgentAuthority.HostTime;
		value.Sequence = ++sequence;
		using MessageWriter messageWriter = Protocol.Begin(MsgType.FireVisual);
		messageWriter.Write(14);
		messageWriter.Write(AgentAuthority.Epoch);
		value.Write(messageWriter);
		if (!Session.SendOptional(messageWriter.ToArray()))
		{
			SceneVisuals.Warn("fire-send", "火焰表现事件发送失败，未中断战斗");
		}
	}

	private static FireParticleData[] DescribeParticles(GameObject source)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		ParticleSystem[] componentsInChildren = source.GetComponentsInChildren<ParticleSystem>(true);
		FireParticleData[] array = new FireParticleData[componentsInChildren.Length];
		for (int i = 0; i < array.Length; i++)
		{
			ParticleSystem.MainModule main = componentsInChildren[i].main;
			ParticleSystem.EmissionModule emission = componentsInChildren[i].emission;
			int num = i;
			FireParticleData fireParticleData = new FireParticleData
			{
				Seed = componentsInChildren[i].randomSeed
			};
			ParticleSystem.MinMaxCurve val = main.startLifetime;
			fireParticleData.Lifetime = val.constantMax;
			val = emission.rateOverTime;
			fireParticleData.Rate = val.constantMax;
			fireParticleData.Speed = main.simulationSpeed;
			fireParticleData.Emitting = emission.enabled;
			fireParticleData.Looping = main.loop;
			array[num] = fireParticleData;
		}
		return array;
	}

	private static FireVisualData Describe(byte kind, int id, GameObject source, Transform parent, float duration)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		Vector3 lossyScale = source.transform.lossyScale;
		Vector3 lossyScale2 = parent.lossyScale;
		return new FireVisualData
		{
			Kind = kind,
			OwnerId = id,
			Enabled = true,
			Duration = duration,
			Position = parent.InverseTransformPoint(source.transform.position),
			Rotation = Quaternion.Inverse(parent.rotation) * source.transform.rotation,
			Scale = new Vector3(lossyScale.x / lossyScale2.x, lossyScale.y / lossyScale2.y, lossyScale.z / lossyScale2.z),
			Particles = DescribeParticles(source)
		};
	}

	internal static void ObserveArrow(Component projectile, int id, Transform anchor)
	{
		if (!SceneVisuals.Capturing || arrowType == null || arrowFire == null || arrowInstance == null)
		{
			return;
		}
		try
		{
			Component component = projectile.GetComponent(arrowType);
			object obj;
			if (!global::UnityObjectOps.Truthy((Object)(object)component))
			{
				obj = null;
			}
			else
			{
				object value = arrowInstance.GetValue(component);
				obj = ((value is GameObject) ? value : null);
			}
			GameObject val = (GameObject)obj;
			bool flag = global::UnityObjectOps.Truthy((Object)(object)component) && (bool)arrowFire.GetValue(component) && global::UnityObjectOps.Truthy((Object)(object)val) && val.activeInHierarchy;
			arrows.TryGetValue(id, out var value2);
			if (flag)
			{
				FireVisualData fireVisualData = Describe(0, id, val, anchor, 120f);
				if (value2 && captured.TryGetValue(fireVisualData.Key, out var value3) && FireVisualData.SameParticles(value3.Particles, fireVisualData.Particles))
				{
					return;
				}
				captured[fireVisualData.Key] = fireVisualData;
				Send(fireVisualData);
			}
			else
			{
				Send(new FireVisualData
				{
					Kind = 0,
					OwnerId = id
				});
			}
			arrows[id] = flag;
		}
		catch (Exception ex)
		{
			SceneVisuals.Warn("fire-arrow", "火矢显示采样失败：" + ex.Message);
		}
	}

	internal static void EndArrow(int id)
	{
		bool value = default(bool);
		if ((SceneVisuals.Capturing && arrows.TryGetValue(id, out value)) & value)
		{
			try
			{
				Send(new FireVisualData
				{
					Kind = 0,
					OwnerId = id
				});
			}
			catch (Exception ex)
			{
				SceneVisuals.Warn("fire-stop", "火矢显示停止失败：" + ex.Message);
			}
		}
		arrows.Remove(id);
		captured.Remove((byte)0 + ":" + id);
	}

	private static void TrackBurn(Component source)
	{
		if (!SceneVisuals.Capturing || !global::UnityObjectOps.Truthy((Object)(object)source) || burnInstance == null || remaining == null)
		{
			return;
		}
		if (burning.Count >= 2048 && !burning.ContainsKey(source))
		{
			SceneVisuals.Warn("burn-capacity", "燃烧显示跟踪达到上限");
			return;
		}
		if (!burning.ContainsKey(source))
		{
			burning.Add(source, 0);
		}
		dirtyBurns.Add(source);
	}

	private static void StopBurn(Component source)
	{
		if (source == null || !burning.TryGetValue(source, out var value))
		{
			return;
		}
		try
		{
			if (SceneVisuals.Capturing && value > 0)
			{
				Send(new FireVisualData
				{
					Kind = 1,
					OwnerId = value
				});
			}
		}
		catch (Exception ex)
		{
			SceneVisuals.Warn("burn-stop", "人物燃烧结束事件失败：" + ex.Message);
		}
		burning.Remove(source);
		dirtyBurns.Remove(source);
		captured.Remove((byte)1 + ":" + value);
	}

	private static void ObserveBurns()
	{
		foreach (Component item in new List<Component>(burning.Keys))
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)item))
			{
				StopBurn(item);
				continue;
			}
			try
			{
				Agent component = item.GetComponent<Agent>();
				int num = AgentAuthority.FindId(component);
				object value = burnInstance.GetValue(item);
				GameObject val = (GameObject)((value is GameObject) ? value : null);
				float num2 = (float)remaining.GetValue(item);
				if (num == 0 || !global::UnityObjectOps.Truthy((Object)(object)val) || !val.activeInHierarchy)
				{
					continue;
				}
				if (num2 <= 0f)
				{
					StopBurn(item);
					continue;
				}
				FireVisualData fireVisualData = Describe(1, num, val, ((Component)component).transform, num2);
				if (dirtyBurns.Contains(item) || !captured.TryGetValue(fireVisualData.Key, out var value2) || !FireVisualData.SameParticles(value2.Particles, fireVisualData.Particles))
				{
					captured[fireVisualData.Key] = fireVisualData;
					Send(fireVisualData);
					burning[item] = num;
					dirtyBurns.Remove(item);
				}
			}
			catch (Exception ex)
			{
				SceneVisuals.Warn("burn-capture", "人物燃烧显示采样失败：" + ex.Message);
				dirtyBurns.Remove(item);
			}
		}
	}

	private static void CaptureGhost(GameObject root)
	{
		spawnedGhost = root;
	}

	private static void Residue(Component source, Vector3 position, Quaternion rotation)
	{
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		if (!SceneVisuals.Capturing || !global::UnityObjectOps.Truthy((Object)(object)spawnedGhost))
		{
			return;
		}
		try
		{
			if (residues.Count >= 256)
			{
				SceneVisuals.Warn("residue-budget", "主机残火跟踪达到显示预算");
				return;
			}
			float num = Convert.ToSingle(Field(arrowType, "groundPersistTime").GetValue(source));
			FireVisualData fireVisualData = Describe(2, ++nextResidue, spawnedGhost, SceneVisuals.Root(1, 0), num);
			fireVisualData.Position = position;
			fireVisualData.Rotation = rotation;
			residues.Add(spawnedGhost, new ResidueSource
			{
				Data = fireVisualData,
				End = AgentAuthority.HostTime + num
			});
			Send(fireVisualData);
		}
		catch (Exception ex)
		{
			SceneVisuals.Warn("fire-residue", "命中残火采样失败：" + ex.Message);
		}
	}

	private static void ObserveResidues()
	{
		foreach (GameObject item in new List<GameObject>(residues.Keys))
		{
			ResidueSource residueSource = residues[item];
			float num = residueSource.End - AgentAuthority.HostTime;
			if (!global::UnityObjectOps.Truthy((Object)(object)item) || num <= 0f)
			{
				residues.Remove(item);
				continue;
			}
			FireParticleData[] array = DescribeParticles(item);
			if (!FireVisualData.SameParticles(residueSource.Data.Particles, array))
			{
				residueSource.Data.Particles = array;
				residueSource.Data.Duration = num;
				Send(residueSource.Data);
			}
		}
	}

	private static void Receive(BinaryReader reader)
	{
		try
		{
			int num = reader.ReadInt32();
			int num2 = reader.ReadInt32();
			FireVisualData fireVisualData = FireVisualData.Read(reader);
			reader.RequireEnd();
			if (Session.OptionalEnabled && !CampaignSync.ClosingBattle && Session.IsClient && AgentAuthority.ClientPlayback && num == 14 && num2 == AgentAuthority.Epoch && fireVisualData.Sequence > lastSequence)
			{
				lastSequence = fireVisualData.Sequence;
				if (!pending.Add(fireVisualData, fireVisualData.Key, fireVisualData.Time, Time.unscaledTime))
				{
					SceneVisuals.Warn("fire-queue", "火焰装饰队列已满");
				}
			}
		}
		catch (Exception ex)
		{
			SceneVisuals.Warn("fire-read", "跳过无效火焰事件：" + ex.Message);
		}
	}

	internal static void RemoveOwner(byte kind, int id)
	{
		string key = kind + ":" + id;
		if (!views.TryGetValue(key, out var value))
		{
			return;
		}
		views.Remove(key);
		try
		{
			Recycle(value);
		}
		catch (Exception ex)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)value.Root))
			{
				Object.Destroy((Object)(object)value.Root);
			}
			SceneVisuals.Warn("fire-release", "火焰回收失败：" + ex.Message);
		}
	}

	private static void Recycle(View view)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)view.Root))
		{
			return;
		}
		ParticleSystem[] particles = view.Particles;
		foreach (ParticleSystem val in particles)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)val))
			{
				val.Stop(false, (ParticleSystemStopBehavior)0);
			}
		}
		view.Root.SetActive(false);
		if (global::UnityObjectOps.Truthy((Object)(object)staging) && pool.Count < 32)
		{
			view.Root.transform.SetParent(staging.transform, false);
			pool.Enqueue(view.Root);
		}
		else
		{
			Object.Destroy((Object)(object)view.Root);
		}
	}

	private static GameObject Take()
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected O, but got Unknown
		if (!global::UnityObjectOps.Truthy((Object)(object)Template))
		{
			return null;
		}
		if (!global::UnityObjectOps.Truthy((Object)(object)staging))
		{
			staging = new GameObject("Together_FirePool");
			staging.SetActive(false);
		}
		while (pool.Count > 0)
		{
			GameObject val = pool.Dequeue();
			if (global::UnityObjectOps.Truthy((Object)(object)val))
			{
				return val;
			}
		}
		GameObject val2 = Object.Instantiate<GameObject>(Template, staging.transform, false);
		try
		{
			val2.SetActive(false);
			((Object)val2).name = "Together_Fire";
			MonoBehaviour[] componentsInChildren = val2.GetComponentsInChildren<MonoBehaviour>(true);
			foreach (MonoBehaviour obj in componentsInChildren)
			{
				((Behaviour)obj).enabled = false;
				Object.DestroyImmediate((Object)(object)obj);
			}
			Component[] componentsInChildren2 = val2.GetComponentsInChildren<Component>(true);
			foreach (Component val3 in componentsInChildren2)
			{
				if (global::UnityObjectOps.Truthy((Object)(object)val3) && !FireVisualPolicy.KeepComponent(((object)val3).GetType()))
				{
					Object.DestroyImmediate((Object)(object)val3);
				}
			}
			val2.AddComponent<LocalVisualObject>();
			return val2;
		}
		catch
		{
			Object.Destroy((Object)(object)val2);
			throw;
		}
	}

	private static bool Apply(FireVisualData data)
	{
		//IL_0163: Unknown result type (might be due to invalid IL or missing references)
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0288: Unknown result type (might be due to invalid IL or missing references)
		//IL_0299: Unknown result type (might be due to invalid IL or missing references)
		//IL_0264: Unknown result type (might be due to invalid IL or missing references)
		//IL_0275: Unknown result type (might be due to invalid IL or missing references)
		//IL_018f: Unknown result type (might be due to invalid IL or missing references)
		//IL_02aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0328: Unknown result type (might be due to invalid IL or missing references)
		//IL_032d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0346: Unknown result type (might be due to invalid IL or missing references)
		if (!data.Enabled || data.Expired(AgentAuthority.PlaybackTime))
		{
			RemoveOwner(data.Kind, data.OwnerId);
			return true;
		}
		if (data.Kind == 1 && AgentAuthority.IsRetired(data.OwnerId))
		{
			return true;
		}
		Transform val;
		if (data.Kind == 0)
		{
			val = ProjectilePlayback.FindVisual(data.OwnerId);
		}
		else if (data.Kind == 1)
		{
			Agent val2 = AgentAuthority.FindAgent(data.OwnerId);
			val = (global::UnityObjectOps.Truthy((Object)(object)val2) ? ((Component)val2).transform : null);
		}
		else
		{
			val = SceneVisuals.Root(1, 0);
		}
		if (!global::UnityObjectOps.Truthy((Object)(object)val) || !global::UnityObjectOps.Truthy((Object)(object)Template))
		{
			return false;
		}
		if (views.TryGetValue(data.Key, out var value) && global::UnityObjectOps.Truthy((Object)(object)value.Root))
		{
			if (value.Particles.Length != data.Particles.Length)
			{
				RemoveOwner(data.Kind, data.OwnerId);
				SceneVisuals.Warn("fire-refresh-layout", "火焰刷新模板变化，跳过本次显示");
				return true;
			}
			Advance(value, data.Time);
			value.Data = data;
			ApplyParameters(value.Particles, data.Particles, restart: false);
			if (data.Kind == 2)
			{
				value.Root.transform.position = data.Position;
				value.Root.transform.rotation = data.Rotation;
			}
			else
			{
				value.Root.transform.localPosition = data.Position;
				value.Root.transform.localRotation = data.Rotation;
			}
			value.Root.transform.localScale = data.Scale;
			return true;
		}
		RemoveOwner(data.Kind, data.OwnerId);
		if (views.Count >= 256)
		{
			SceneVisuals.Warn("fire-views", "火焰显示数量达到上限");
			return true;
		}
		GameObject val3 = Take();
		if (!global::UnityObjectOps.Truthy((Object)(object)val3))
		{
			return false;
		}
		ParticleSystem[] componentsInChildren = val3.GetComponentsInChildren<ParticleSystem>(true);
		if (componentsInChildren.Length != data.Particles.Length)
		{
			Object.Destroy((Object)(object)val3);
			SceneVisuals.Warn("fire-layout", "两端火焰粒子模板不同");
			return true;
		}
		views.Add(data.Key, new View
		{
			Root = val3,
			Parent = val,
			Data = data,
			Particles = componentsInChildren,
			Time = data.Time
		});
		val3.transform.SetParent(val, false);
		if (data.Kind == 2)
		{
			val3.transform.position = data.Position;
			val3.transform.rotation = data.Rotation;
		}
		else
		{
			val3.transform.localPosition = data.Position;
			val3.transform.localRotation = data.Rotation;
		}
		val3.transform.localScale = data.Scale;
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			ParticleSystem obj = componentsInChildren[i];
			FireParticleData fireParticleData = data.Particles[i];
			obj.Stop(false, (ParticleSystemStopBehavior)0);
			obj.useAutoRandomSeed = false;
			obj.randomSeed = fireParticleData.Seed;
			ParticleSystem.MainModule main = obj.main;
			main.startLifetime = (ParticleSystem.MinMaxCurve)(fireParticleData.Lifetime);
			main.simulationSpeed = 1f;
			main.loop = fireParticleData.Looping;
			main.playOnAwake = false;
			ParticleSystem.EmissionModule emission = obj.emission;
			emission.enabled = fireParticleData.Emitting;
			emission.rateOverTime = (ParticleSystem.MinMaxCurve)(fireParticleData.Rate);
		}
		val3.SetActive(true);
		ParticleSystem[] array = componentsInChildren;
		foreach (ParticleSystem obj2 in array)
		{
			obj2.Play(false);
			obj2.Pause(false);
		}
		return true;
	}

	private static void ApplyParameters(ParticleSystem[] particles, FireParticleData[] data, bool restart)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		for (int i = 0; i < particles.Length; i++)
		{
			ParticleSystem.MainModule main = particles[i].main;
			main.startLifetime = (ParticleSystem.MinMaxCurve)(data[i].Lifetime);
			main.simulationSpeed = 1f;
			main.loop = data[i].Looping;
			ParticleSystem.EmissionModule emission = particles[i].emission;
			emission.enabled = data[i].Emitting;
			emission.rateOverTime = (ParticleSystem.MinMaxCurve)(data[i].Rate);
		}
	}

	private static void Advance(View view, float time)
	{
		float num = Mathf.Max(0f, time - view.Time);
		for (int i = 0; i < view.Particles.Length; i++)
		{
			view.Particles[i].Simulate(num * view.Data.Particles[i].Speed, false, false, false);
			view.Particles[i].Pause(false);
		}
		view.Time = Mathf.Max(view.Time, time);
	}

	public static void LateTick()
	{
		if (SceneVisuals.Capturing)
		{
			ObserveBurns();
			ObserveResidues();
		}
		else
		{
			if (!AgentAuthority.ClientPlayback)
			{
				return;
			}
			if (!AgentAuthority.HasFailure)
			{
				pending.Process(AgentAuthority.PlaybackTime, Time.unscaledTime, delegate(FireVisualData data)
				{
					try
					{
						return Apply(data);
					}
					catch (Exception ex)
					{
						RemoveOwner(data.Kind, data.OwnerId);
						SceneVisuals.Warn("fire-apply", "火焰播放失败：" + ex.Message);
						return true;
					}
				}, delegate
				{
					SceneVisuals.Warn("fire-wait", "火焰等待本机模板或附着对象超时");
				});
			}
			foreach (string item in new List<string>(views.Keys))
			{
				View view = views[item];
				float playbackTime = AgentAuthority.PlaybackTime;
				if (!global::UnityObjectOps.Truthy((Object)(object)view.Root) || !global::UnityObjectOps.Truthy((Object)(object)view.Parent) || view.Data.Expired(playbackTime))
				{
					Recycle(view);
					views.Remove(item);
				}
				else if (!AgentAuthority.HasFailure && Mathf.Max(0f, playbackTime - view.Time) > 0f)
				{
					Advance(view, playbackTime);
				}
			}
		}
	}
}
