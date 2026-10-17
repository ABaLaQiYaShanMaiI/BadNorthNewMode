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

namespace Together;

internal static class CombatEvents
{
	private sealed class Event
	{
		public byte Kind;

		public float Time;

		public string Resource;

		public Vector3 Position;

		public Quaternion Rotation;

		public Vector3 Scale;

		public uint[] ParticleSeeds;
	}

	private sealed class AudioAnchor
	{
		public GameObject Object;

		public float RetireAt;
	}

	private struct ParticleState
	{
		public bool Automatic;

		public uint Seed;
	}

	[HarmonyPatch(typeof(Agent), "DealDamage")]
	private static class DamageScopePatch
	{
		private static void Prefix(Agent __instance, ref bool __state)
		{
			__state = Capturing && global::UnityObjectOps.Truthy((Object)(object)__instance) && global::UnityObjectOps.Truthy((Object)(object)__instance.faction) && (Object)(object)((IslandComponent)__instance.faction).island == (Object)(object)island;
			if (__state)
			{
				damageDepth++;
			}
		}

		private static Exception Finalizer(Exception __exception, bool __state)
		{
			if (__state)
			{
				damageDepth = Math.Max(0, damageDepth - 1);
			}
			return __exception;
		}
	}

	[HarmonyPatch(typeof(ReusableEffect), "PlayAt", new Type[]
	{
		typeof(Vector3),
		typeof(Quaternion),
		typeof(Vector3)
	})]
	private static class HitEffectPatch
	{
		private static void Postfix(ReusableEffect __instance, ReusableEffect __result, Vector3 pos, Quaternion rotation, Vector3 scale)
		{
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			//IL_0003: Unknown result type (might be due to invalid IL or missing references)
			//IL_0004: Unknown result type (might be due to invalid IL or missing references)
			try
			{
				CaptureEffect(__instance, __result, pos, rotation, scale);
			}
			catch (Exception ex)
			{
				SceneVisuals.Warn("combat-capture", "命中特效采样失败：" + ex.Message);
			}
		}
	}

	[HarmonyPatch(typeof(ReusableEffect), "PlayAt", new Type[]
	{
		typeof(NavPos),
		typeof(Quaternion),
		typeof(Vector3)
	})]
	private static class GroundEffectPatch
	{
		private static void Postfix(ReusableEffect __instance, ReusableEffect __result)
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			if (!global::UnityObjectOps.Truthy((Object)(object)__result))
			{
				return;
			}
			try
			{
				CaptureEffect(__instance, __result, ((Component)__result).transform.position, ((Component)__result).transform.rotation, ((Component)__result).transform.lossyScale);
			}
			catch (Exception ex)
			{
				SceneVisuals.Warn("ground-effect", "地面特效采样失败：" + ex.Message);
			}
		}
	}

	[HarmonyPatch(typeof(AudioEventBuffer), "RequestEvent", new Type[]
	{
		typeof(FabricEventReference),
		typeof(GameObject)
	})]
	private static class AudioNamePatch
	{
		private static void Prefix(AudioEventBuffer __instance, FabricEventReference eventRef)
		{
			if (Capturing && damageDepth > 0 && eventRef != null && ((LookupReference)eventRef).id != 0 && IsCombatBuffer(__instance))
			{
				combatAudioNames[((LookupReference)eventRef).id] = ((LookupReference)eventRef).name;
			}
		}
	}

	[HarmonyPatch(typeof(AudioEventBuffer), "Update")]
	private static class AudioScopePatch
	{
		private static void Prefix(AudioEventBuffer __instance, ref bool __state)
		{
			__state = Capturing && IsCombatBuffer(__instance);
			if (__state)
			{
				combatAudioDepth++;
			}
		}

		private static Exception Finalizer(Exception __exception, bool __state)
		{
			if (__state)
			{
				combatAudioDepth = Math.Max(0, combatAudioDepth - 1);
			}
			return __exception;
		}
	}

	[HarmonyPatch(typeof(FabricWrapper), "PostEvent", new Type[]
	{
		typeof(int),
		typeof(GameObject)
	})]
	private static class AudioPlayedPatch
	{
		private static void Postfix(int eventId, GameObject parentGameObject, bool __result)
		{
			if (__result)
			{
				CaptureAudio(eventId, parentGameObject);
			}
		}
	}

	private const byte Schema = 1;

	private const int MaximumQueuedEvents = 2048;

	private static bool initialized;

	private static Island island;

	private static int damageDepth;

	private static int combatAudioDepth;

	private static int sequence;

	private static int lastSequence;

	private static readonly Dictionary<int, string> combatAudioNames = new Dictionary<int, string>();

	private static readonly Dictionary<string, ReusableEffect> effects = new Dictionary<string, ReusableEffect>();

	private static readonly HashSet<string> missingEffects = new HashSet<string>();

	private static readonly List<Event> pending = new List<Event>();

	private static readonly List<AudioAnchor> audioAnchors = new List<AudioAnchor>();

	private static readonly Dictionary<ParticleSystem, ParticleState> originalParticleStates = new Dictionary<ParticleSystem, ParticleState>();

	private static bool Capturing
	{
		get
		{
			if (Session.IsHost && Session.OptionalEnabled && !CampaignSync.ClosingBattle && global::UnityObjectOps.Truthy((Object)(object)island) && DeploySync.BattleStarted)
			{
				return !AgentAuthority.HasFailure;
			}
			return false;
		}
	}

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			Session.On(MsgType.CombatEvent, Receive);
			Session.Stopped += Reset;
			Session.OptionalStopped += StopOptional;
		}
	}

	public static void BeginIsland(Island value)
	{
		Reset();
		island = value;
	}

	public static void Reset()
	{
		StopOptional();
		combatAudioNames.Clear();
		island = null;
		damageDepth = (combatAudioDepth = (sequence = (lastSequence = 0)));
	}

	private static void StopOptional()
	{
		foreach (KeyValuePair<ParticleSystem, ParticleState> originalParticleState in originalParticleStates)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)originalParticleState.Key))
			{
				originalParticleState.Key.Stop(true, (ParticleSystemStopBehavior)0);
				originalParticleState.Key.randomSeed = originalParticleState.Value.Seed;
				originalParticleState.Key.useAutoRandomSeed = originalParticleState.Value.Automatic;
			}
		}
		originalParticleStates.Clear();
		foreach (AudioAnchor audioAnchor in audioAnchors)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)audioAnchor.Object))
			{
				Object.Destroy((Object)(object)audioAnchor.Object);
			}
		}
		audioAnchors.Clear();
		pending.Clear();
		effects.Clear();
		missingEffects.Clear();
	}

	public static void Tick()
	{
	}

	private static bool IsCombatBuffer(AudioEventBuffer buffer)
	{
		IslandGameplayManager instance = Singleton<IslandGameplayManager>.instance;
		if (global::UnityObjectOps.Truthy((Object)(object)instance))
		{
			return (Object)(object)buffer == (Object)(object)instance.combatAudio;
		}
		return false;
	}

	private static string EffectKey(ReusableEffect effect)
	{
		PrefabManager instance = ScriptableObjectSingleton<PrefabManager>.instance;
		if (global::UnityObjectOps.Truthy((Object)(object)instance))
		{
			FieldInfo[] fields = typeof(PrefabManager).GetFields(BindingFlags.Instance | BindingFlags.Public);
			foreach (FieldInfo fieldInfo in fields)
			{
				if (typeof(ReusableEffect).IsAssignableFrom(fieldInfo.FieldType))
				{
					object value = fieldInfo.GetValue(instance);
					if ((Object)((value is ReusableEffect) ? value : null) == (Object)(object)effect)
					{
						return "prefab:" + fieldInfo.Name;
					}
				}
			}
		}
		return ((object)effect).GetType().FullName + "|" + ((Object)effect).name.Replace("(Clone)", "").Trim();
	}

	private static void Send(Event value)
	{
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		using MessageWriter messageWriter = Protocol.Begin(MsgType.CombatEvent);
		messageWriter.Write((byte)1);
		messageWriter.Write(AgentAuthority.Epoch);
		messageWriter.Write(AgentAuthority.HostTime);
		messageWriter.Write(++sequence);
		messageWriter.Write(value.Kind);
		VisualWire.WriteString(messageWriter, value.Resource, 512);
		WorldAuthority.WriteVector(messageWriter, value.Position);
		if (value.Kind == 0)
		{
			WorldAuthority.WriteRotation(messageWriter, value.Rotation);
			WorldAuthority.WriteVector(messageWriter, value.Scale);
			messageWriter.Write((byte)value.ParticleSeeds.Length);
			uint[] particleSeeds = value.ParticleSeeds;
			foreach (uint value2 in particleSeeds)
			{
				messageWriter.Write(value2);
			}
		}
		if (!Session.SendOptional(messageWriter.ToArray()))
		{
			SceneVisuals.Warn("combat-send", "命中特效发送失败，未中断战斗");
		}
	}

	private static void CaptureEffect(ReusableEffect definition, ReusableEffect instance, Vector3 position, Quaternion rotation, Vector3 scale)
	{
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		PrefabManager instance2 = ScriptableObjectSingleton<PrefabManager>.instance;
		bool flag = global::UnityObjectOps.Truthy((Object)(object)instance2) && ((Object)(object)definition == (Object)(object)instance2.bloodStain || (Object)(object)definition == (Object)(object)instance2.bloodSplat || (Object)(object)definition == (Object)(object)instance2.splash);
		if (!Capturing || (damageDepth == 0 && !flag) || !global::UnityObjectOps.Truthy((Object)(object)definition) || !global::UnityObjectOps.Truthy((Object)(object)instance))
		{
			return;
		}
		ParticleSystem[] componentsInChildren = ((Component)instance).GetComponentsInChildren<ParticleSystem>(true);
		if (componentsInChildren.Length > 64)
		{
			Plugin.Log.LogWarning((object)("[战斗表现] 命中特效粒子数量超出范围：" + ((Object)definition).name));
			return;
		}
		uint[] array = new uint[componentsInChildren.Length];
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			array[i] = componentsInChildren[i].randomSeed;
		}
		Send(new Event
		{
			Kind = 0,
			Resource = EffectKey(definition),
			Position = position,
			Rotation = rotation,
			Scale = scale,
			ParticleSeeds = array
		});
	}

	private static void CaptureAudio(int eventId, GameObject source)
	{
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		if (Capturing && combatAudioDepth != 0 && global::UnityObjectOps.Truthy((Object)(object)source) && combatAudioNames.TryGetValue(eventId, out var value))
		{
			Send(new Event
			{
				Kind = 1,
				Resource = value,
				Position = source.transform.position
			});
		}
	}

	private static void Receive(BinaryReader reader)
	{
		try
		{
			ReadEvent(reader);
		}
		catch (Exception ex)
		{
			SceneVisuals.Warn("combat-read", "跳过无效命中特效：" + ex.Message);
		}
	}

	private static void ReadEvent(BinaryReader reader)
	{
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		byte b = reader.ReadByte();
		int num = reader.ReadInt32();
		float num2 = reader.ReadSingle();
		if (!Session.OptionalEnabled || CampaignSync.ClosingBattle || !Session.IsClient || !global::UnityObjectOps.Truthy((Object)(object)island) || b != 1 || num != AgentAuthority.Epoch)
		{
			return;
		}
		int num3 = reader.ReadInt32();
		byte b2 = reader.ReadByte();
		string resource = VisualWire.ReadString(reader, 512);
		Vector3 position = ReadVector(reader);
		if (b2 > 1 || !Finite(num2) || num3 <= 0)
		{
			throw new IOException("命中表现消息头无效");
		}
		Event obj = new Event
		{
			Kind = b2,
			Time = num2,
			Resource = resource,
			Position = position
		};
		if (b2 == 0)
		{
			Quaternion val = WorldAuthority.ReadRotation(reader);
			if (!Finite(val.x) || !Finite(val.y) || !Finite(val.z) || !Finite(val.w))
			{
				throw new IOException("命中表现旋转无效");
			}
			obj.Rotation = val;
			obj.Scale = ReadVector(reader);
			int num4 = reader.ReadByte();
			if (num4 > 64)
			{
				throw new IOException("命中特效粒子数量异常");
			}
			obj.ParticleSeeds = new uint[num4];
			for (int i = 0; i < num4; i++)
			{
				obj.ParticleSeeds[i] = reader.ReadUInt32();
			}
		}
		if (num3 > lastSequence)
		{
			lastSequence = num3;
			if (pending.Count >= 2048)
			{
				SceneVisuals.Warn("combat-queue", "命中特效队列已满，跳过装饰事件");
			}
			else
			{
				pending.Add(obj);
			}
		}
	}

	private static bool Finite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}

	private static Vector3 ReadVector(BinaryReader reader)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		Vector3 val = WorldAuthority.ReadVector(reader);
		if (!Finite(val.x) || !Finite(val.y) || !Finite(val.z))
		{
			throw new IOException("命中表现坐标无效");
		}
		return val;
	}

	public static void LateTick()
	{
		if (!Session.OptionalEnabled || CampaignSync.ClosingBattle || !AgentAuthority.ClientPlayback || !global::UnityObjectOps.Truthy((Object)(object)island))
		{
			return;
		}
		float playbackTime = AgentAuthority.PlaybackTime;
		int num = 0;
		while (pending.Count > 0 && pending[0].Time <= playbackTime && num++ < 128)
		{
			Event obj = pending[0];
			pending.RemoveAt(0);
			if (obj.Kind == 0)
			{
				PlayEffect(obj);
			}
			else
			{
				PlayAudio(obj);
			}
		}
		for (int num2 = audioAnchors.Count - 1; num2 >= 0; num2--)
		{
			if (!(audioAnchors[num2].RetireAt > Time.unscaledTime))
			{
				if (global::UnityObjectOps.Truthy((Object)(object)audioAnchors[num2].Object))
				{
					Object.Destroy((Object)(object)audioAnchors[num2].Object);
				}
				audioAnchors.RemoveAt(num2);
			}
		}
	}

	private static ReusableEffect ResolveEffect(string key)
	{
		if (effects.TryGetValue(key, out var value) && global::UnityObjectOps.Truthy((Object)(object)value))
		{
			return value;
		}
		if (key.StartsWith("prefab:", StringComparison.Ordinal))
		{
			FieldInfo field = typeof(PrefabManager).GetField(key.Substring(7), BindingFlags.Instance | BindingFlags.Public);
			PrefabManager instance = ScriptableObjectSingleton<PrefabManager>.instance;
			if (field != null && global::UnityObjectOps.Truthy((Object)(object)instance) && typeof(ReusableEffect).IsAssignableFrom(field.FieldType))
			{
				object value2 = field.GetValue(instance);
				value = (ReusableEffect)((value2 is ReusableEffect) ? value2 : null);
			}
		}
		else
		{
			int num = key.IndexOf('|');
			if (num <= 0)
			{
				return null;
			}
			string text = key.Substring(0, num);
			string text2 = key.Substring(num + 1);
			Object[] array = Resources.FindObjectsOfTypeAll(typeof(ReusableEffect));
			foreach (Object obj in array)
			{
				ReusableEffect val = (ReusableEffect)(object)((obj is ReusableEffect) ? obj : null);
				if (global::UnityObjectOps.Truthy((Object)(object)val) && !(((object)val).GetType().FullName != text) && !(((Object)val).name.Replace("(Clone)", "").Trim() != text2))
				{
					value = val;
					if (((Object)val).name.IndexOf("(Clone)", StringComparison.Ordinal) < 0)
					{
						break;
					}
				}
			}
		}
		if (global::UnityObjectOps.Truthy((Object)(object)value))
		{
			effects[key] = value;
		}
		return value;
	}

	private static void PlayEffect(Event value)
	{
		//IL_0109: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		ReusableEffect val = ResolveEffect(value.Resource);
		if (!global::UnityObjectOps.Truthy((Object)(object)val))
		{
			if (missingEffects.Add(value.Resource))
			{
				Plugin.Log.LogWarning((object)("[战斗表现] 未找到本机命中特效：" + value.Resource));
			}
			return;
		}
		ParticleSystem[] componentsInChildren = ((Component)val.GetOrCreate((Transform)null)).GetComponentsInChildren<ParticleSystem>(true);
		if (componentsInChildren.Length != value.ParticleSeeds.Length)
		{
			if (missingEffects.Add(value.Resource + ":layout"))
			{
				Plugin.Log.LogWarning((object)("[战斗表现] 命中特效粒子结构与主机不一致：" + value.Resource));
			}
			return;
		}
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			if (!originalParticleStates.ContainsKey(componentsInChildren[i]))
			{
				originalParticleStates[componentsInChildren[i]] = new ParticleState
				{
					Automatic = componentsInChildren[i].useAutoRandomSeed,
					Seed = componentsInChildren[i].randomSeed
				};
			}
			componentsInChildren[i].Stop(true, (ParticleSystemStopBehavior)1);
			componentsInChildren[i].useAutoRandomSeed = false;
			componentsInChildren[i].randomSeed = value.ParticleSeeds[i];
		}
		val.PlayAt(value.Position, value.Rotation, value.Scale);
	}

	private static void PlayAudio(Event value)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		if (audioAnchors.Count < 256)
		{
			GameObject val = new GameObject("Together_CombatAudio");
			val.transform.SetParent(island.runContainer, false);
			val.transform.position = value.Position;
			audioAnchors.Add(new AudioAnchor
			{
				Object = val,
				RetireAt = Time.unscaledTime + 5f
			});
			FabricWrapper.PostEvent(value.Resource, val);
		}
	}
}
