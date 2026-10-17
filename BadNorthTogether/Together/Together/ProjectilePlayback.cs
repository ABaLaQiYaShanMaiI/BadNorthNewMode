using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Together.Net;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.Ballistics;
using Voxels.TowerDefense.SpriteMagic;

namespace Together;

internal static class ProjectilePlayback
{
	private sealed class Source
	{
		public int Id;

		public Component Component;

		public Renderer[] Renderers;

		public ProjectileNodePose[] LastPoses;
	}

	private sealed class Playback
	{
		public GameObject Root;

		public Renderer[] Renderers;

		public BatchedSprite[] Batches;

		public Renderer[][] Drawers;

		public byte[] Kinds;

		public float[] Tangents;

		public float Start;

		public float End = float.PositiveInfinity;

		public readonly List<ProjectileNodePose[]> Poses = new List<ProjectileNodePose[]>();

		public readonly List<float> Times = new List<float>();
	}

	private sealed class Effect
	{
		public float Time;

		public string Definition;

		public Vector3 Position;

		public Vector3 Normal;

		public Vector3 Forward;
	}

	[HarmonyPatch(typeof(Shootable), "Shoot")]
	private static class ShootPatch
	{
		private static bool Prefix()
		{
			return !AgentAuthority.ClientPlayback;
		}

		private static void Postfix(Shootable __instance)
		{
			Started((Component)(object)__instance);
		}
	}

	[HarmonyPatch(typeof(Shootable), "FixedUpdate")]
	private static class ShootUpdatePatch
	{
		private static bool Prefix()
		{
			return !AgentAuthority.ClientPlayback;
		}
	}

	[HarmonyPatch(typeof(Torch), "Shoot")]
	private static class TorchShootPatch
	{
		private static bool Prefix()
		{
			return !AgentAuthority.ClientPlayback;
		}

		private static void Postfix(Torch __instance)
		{
			Started((Component)(object)__instance);
		}
	}

	[HarmonyPatch(typeof(Torch), "Update")]
	private static class TorchUpdatePatch
	{
		private static bool Prefix()
		{
			return !AgentAuthority.ClientPlayback;
		}
	}

	[HarmonyPatch(typeof(BombProjectile), "Setup")]
	private static class BombSetupPatch
	{
		private static void Postfix(BombProjectile __instance)
		{
			QueueStart((Component)(object)__instance);
		}
	}

	[HarmonyPatch(typeof(BombProjectile), "FixedUpdate")]
	private static class BombUpdatePatch
	{
		private static bool Prefix()
		{
			return !AgentAuthority.ClientPlayback;
		}
	}

	[HarmonyPatch(typeof(SelfPoolingPrefab), "ReturnToPool")]
	private static class SelfReturnPatch
	{
		private static void Prefix(SelfPoolingPrefab __instance)
		{
			Ended((Component)(object)__instance);
		}
	}

	[HarmonyPatch(typeof(SelfPoolingPrefab), "OnDisable")]
	private static class SelfDisablePatch
	{
		private static void Prefix(SelfPoolingPrefab __instance)
		{
			Ended((Component)(object)__instance);
		}
	}

	[HarmonyPatch(typeof(SelfPoolingPrefab), "Get")]
	private static class SelfGetPatch
	{
		private static void Postfix(SelfPoolingPrefab __result)
		{
			QueueStart((Component)(object)__result);
		}
	}

	[HarmonyPatch(typeof(PoolablePrefab), "ReturnToPool")]
	private static class ReturnPatch
	{
		private static void Prefix(PoolablePrefab __instance)
		{
			Ended((Component)(object)__instance);
		}
	}

	[HarmonyPatch(typeof(ExplosionHelpers), "CreateExplosionInternal")]
	private static class ExplosionPatch
	{
		private static bool Prefix(Vector3 position, Vector3 normal, Vector3 forward, ExplosionDef definition)
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			if (AgentAuthority.ClientPlayback)
			{
				return false;
			}
			Explosion(position, normal, forward, definition);
			return true;
		}
	}

	[HarmonyPatch]
	private static class MineSetupPatch
	{
		private static MethodBase TargetMethod()
		{
			return AccessTools.Method(mineType, "Setup", (Type[])null, (Type[])null);
		}

		private static void Postfix(Component __instance)
		{
			QueueStart(__instance);
		}
	}

	[HarmonyPatch]
	private static class MineUpdatePatch
	{
		private static MethodBase TargetMethod()
		{
			return AccessTools.Method(mineType, "Update", (Type[])null, (Type[])null);
		}

		private static bool Prefix()
		{
			return !AgentAuthority.ClientPlayback;
		}
	}

	private const byte Schema = 3;

	private const int MaxRenderers = 64;

	private static readonly FieldInfo batchedRends = AccessTools.Field(typeof(BatchedSprite), "rends");

	private static readonly FieldInfo batchedRenderer = AccessTools.Field(typeof(BatchedSprite).GetNestedType("Rend", BindingFlags.NonPublic), "mr");

	private static readonly FieldInfo tangentField = AccessTools.Field(typeof(BatchedSprite), "tangentW");

	private static bool initialized;

	private static Island island;

	private static int nextId;

	private static float nextSample;

	private static float trafficStart;

	private static long trafficBytes;

	private static long oldTrafficBytes;

	private static int trafficPackets;

	private static int trafficRejected;

	private static readonly Dictionary<Component, Source> sources = new Dictionary<Component, Source>();

	private static readonly HashSet<Component> pendingSources = new HashSet<Component>();

	private static readonly Dictionary<int, Playback> playbacks = new Dictionary<int, Playback>();

	private static readonly Dictionary<string, Component> templates = new Dictionary<string, Component>();

	private static readonly List<Effect> effects = new List<Effect>();

	private static readonly HashSet<ParticleSystem> effectParticles = new HashSet<ParticleSystem>();

	private static readonly Type mineType = AccessTools.TypeByName("Voxels.TowerDefense.ProximityMine");

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			Session.On(MsgType.WorldEvent, Receive);
			Session.On(MsgType.ProjectilePoses, delegate(BinaryReader reader)
			{
				PoseBatch.Read(reader, Receive, MsgType.WorldEvent);
			});
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
		foreach (Playback value in playbacks.Values)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)value.Root))
			{
				Object.Destroy((Object)(object)value.Root);
			}
		}
		sources.Clear();
		pendingSources.Clear();
		playbacks.Clear();
		templates.Clear();
		effects.Clear();
		island = null;
		nextId = 0;
		nextSample = 0f;
		trafficStart = Time.unscaledTime;
		trafficBytes = (oldTrafficBytes = 0L);
		trafficPackets = (trafficRejected = 0);
	}

	private static void StopOptional()
	{
		effects.Clear();
		foreach (ParticleSystem effectParticle in effectParticles)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)effectParticle))
			{
				effectParticle.Stop(false, (ParticleSystemStopBehavior)0);
			}
		}
		effectParticles.Clear();
	}

	public static void Tick()
	{
	}

	internal static Transform FindVisual(int id)
	{
		if (!playbacks.TryGetValue(id, out var value) || value.Renderers.Length == 0 || !global::UnityObjectOps.Truthy((Object)(object)value.Renderers[0]))
		{
			return null;
		}
		return ((Component)value.Renderers[0]).transform;
	}

	private static string CleanName(string name)
	{
		return name.Replace("(Clone)", "").Trim();
	}

	private static byte Kind(Component component)
	{
		if (component is TankArcherArrow)
		{
			return 1;
		}
		if (component is ThrowingAxe)
		{
			return 2;
		}
		if (component is Arrow)
		{
			return 0;
		}
		if (component is Torch)
		{
			return 3;
		}
		if (component is BombProjectile)
		{
			return 4;
		}
		if (mineType != null && mineType.IsInstanceOfType(component))
		{
			return 5;
		}
		return byte.MaxValue;
	}

	private static Type TypeFor(byte kind)
	{
		return kind switch
		{
			0 => typeof(Arrow), 
			1 => typeof(TankArcherArrow), 
			2 => typeof(ThrowingAxe), 
			3 => typeof(Torch), 
			4 => typeof(BombProjectile), 
			5 => mineType, 
			_ => null, 
		};
	}

	private static Renderer[] Renderers(Component component)
	{
		return ProjectileVisualPolicy.Select(component.GetComponentsInChildren<Renderer>(true), (Renderer renderer) => renderer is SpriteRenderer || renderer is MeshRenderer, (Renderer renderer) => renderer is MeshRenderer && global::UnityObjectOps.Truthy((Object)(object)((Component)renderer).transform.parent) && global::UnityObjectOps.Truthy((Object)(object)((Component)((Component)renderer).transform.parent).GetComponent<BatchedSprite>()) && global::UnityObjectOps.Truthy((Object)(object)((Component)renderer).GetComponent<MeshFilter>()) && !global::UnityObjectOps.Truthy((Object)(object)((Component)renderer).GetComponent<BatchedSprite>()));
	}

	private static void Started(Component component)
	{
		if (CampaignSync.ClosingBattle || !Session.IsHost || !global::UnityObjectOps.Truthy((Object)(object)island) || !DeploySync.BattleStarted || !global::UnityObjectOps.Truthy((Object)(object)component) || Kind(component) == byte.MaxValue)
		{
			return;
		}
		pendingSources.Remove(component);
		if (sources.ContainsKey(component))
		{
			return;
		}
		Source source = new Source
		{
			Id = ++nextId,
			Component = component,
			Renderers = Renderers(component)
		};
		if (source.Renderers.Length == 0 || source.Renderers.Length > 64)
		{
			AgentAuthority.Fail("飞行物没有可复制的渲染组件：" + ((Object)component).name);
			return;
		}
		sources[component] = source;
		ProjectileNodePose[] poses = Capture(source);
		AgentAuthority.FlushResources();
		using (MessageWriter messageWriter = Header(0))
		{
			messageWriter.Write(source.Id);
			messageWriter.Write(Kind(component));
			VisualWire.WriteString(messageWriter, CleanName(((Object)component).name), 512);
			WritePoses(messageWriter, poses);
			if (!Session.Send(messageWriter.ToArray()))
			{
				AgentAuthority.Fail("飞行物出生事件发送失败");
			}
		}
		FireVisuals.ObserveArrow(component, source.Id, ((Component)source.Renderers[0]).transform);
	}

	private static void Ended(Component component)
	{
		if (!Session.IsHost || component == null)
		{
			return;
		}
		pendingSources.Remove(component);
		if (!sources.TryGetValue(component, out var value))
		{
			return;
		}
		if (CampaignSync.ClosingBattle)
		{
			FireVisuals.EndArrow(value.Id);
			sources.Remove(component);
			return;
		}
		ProjectileNodePose[] poses = Capture(value);
		AgentAuthority.FlushResources();
		using (MessageWriter messageWriter = Header(2))
		{
			messageWriter.Write(value.Id);
			WritePoses(messageWriter, poses);
			Session.Send(messageWriter.ToArray());
		}
		FireVisuals.EndArrow(value.Id);
		sources.Remove(component);
	}

	private static void QueueStart(Component component)
	{
		if (Session.IsHost && !CampaignSync.ClosingBattle && global::UnityObjectOps.Truthy((Object)(object)island) && DeploySync.BattleStarted && global::UnityObjectOps.Truthy((Object)(object)component) && Kind(component) != byte.MaxValue)
		{
			pendingSources.Add(component);
		}
	}

	private static void SendPosePacket(byte[] data)
	{
		if (Session.Send(data, reliable: false))
		{
			trafficBytes += data.Length + 33;
			trafficPackets++;
		}
		else
		{
			trafficRejected++;
		}
	}

	private static MessageWriter Header(byte action)
	{
		MessageWriter messageWriter = Protocol.Begin(MsgType.WorldEvent);
		messageWriter.Write((byte)3);
		messageWriter.Write(AgentAuthority.Epoch);
		messageWriter.Write(AgentAuthority.HostTime);
		messageWriter.Write(action);
		return messageWriter;
	}

	private static ProjectileNodePose[] Capture(Source source)
	{
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_012e: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0186: Unknown result type (might be due to invalid IL or missing references)
		//IL_0193: Unknown result type (might be due to invalid IL or missing references)
		if (!global::UnityObjectOps.Truthy((Object)(object)source.Component) && source.LastPoses != null)
		{
			return source.LastPoses;
		}
		ProjectileNodePose[] array = new ProjectileNodePose[source.Renderers.Length];
		for (int i = 0; i < array.Length; i++)
		{
			Renderer val = source.Renderers[i];
			if (!global::UnityObjectOps.Truthy((Object)(object)val))
			{
				if (source.LastPoses != null)
				{
					array[i] = source.LastPoses[i];
				}
				continue;
			}
			SpriteRenderer val2 = (SpriteRenderer)(object)((val is SpriteRenderer) ? val : null);
			BatchedSprite val3 = (global::UnityObjectOps.Truthy((Object)(object)val2) ? ((Component)val2).GetComponent<BatchedSprite>() : null);
			bool flag = val.enabled;
			if (global::UnityObjectOps.Truthy((Object)(object)val3))
			{
				Renderer[] array2 = GeneratedRenderers(val3);
				if (array2.Length != 0)
				{
					flag = false;
					Renderer[] array3 = array2;
					foreach (Renderer val4 in array3)
					{
						if (global::UnityObjectOps.Truthy((Object)(object)val4) && val4.enabled)
						{
							flag = true;
							break;
						}
					}
				}
			}
			array[i] = new ProjectileNodePose
			{
				Position = ((Component)val).transform.position,
				Rotation = ((Component)val).transform.rotation,
				Kind = (global::UnityObjectOps.Truthy((Object)(object)val3) ? ((byte)2) : (global::UnityObjectOps.Truthy((Object)(object)val2) ? ((byte)1) : ((byte)0))),
				Scale = ((Component)val).transform.lossyScale,
				Visible = (flag && ((Component)val).gameObject.activeInHierarchy),
				FlipX = (global::UnityObjectOps.Truthy((Object)(object)val2) && val2.flipX),
				FlipY = (global::UnityObjectOps.Truthy((Object)(object)val2) && val2.flipY),
				Color = (global::UnityObjectOps.Truthy((Object)(object)val2) ? val2.color : Color.white),
				SpriteId = (ushort)(global::UnityObjectOps.Truthy((Object)(object)val2) ? VisualResources.GetId(val2.sprite) : 0)
			};
		}
		source.LastPoses = array;
		return array;
	}

	private static void WritePoses(BinaryWriter writer, ProjectileNodePose[] poses)
	{
		ProjectileFrame.Write(writer, poses);
	}

	private static ProjectileNodePose[] ReadPoses(BinaryReader reader)
	{
		return ProjectileFrame.Read(reader);
	}

	private static Renderer[] GeneratedRenderers(BatchedSprite batch)
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Expected O, but got Unknown
		Array array = (Array)batchedRends.GetValue(batch);
		Renderer[] array2 = (Renderer[])(object)new Renderer[array?.Length ?? 0];
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i] = (Renderer)batchedRenderer.GetValue(array.GetValue(i));
		}
		return array2;
	}

	public static void LateTick()
	{
		//IL_04f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_04fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0502: Unknown result type (might be due to invalid IL or missing references)
		//IL_0515: Unknown result type (might be due to invalid IL or missing references)
		//IL_051c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0523: Unknown result type (might be due to invalid IL or missing references)
		//IL_0536: Unknown result type (might be due to invalid IL or missing references)
		//IL_053d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0544: Unknown result type (might be due to invalid IL or missing references)
		//IL_06b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_064e: Unknown result type (might be due to invalid IL or missing references)
		//IL_05ea: Unknown result type (might be due to invalid IL or missing references)
		if (!Session.Active || !global::UnityObjectOps.Truthy((Object)(object)island))
		{
			return;
		}
		if (!CampaignSync.ClosingBattle && Session.IsHost && pendingSources.Count > 0)
		{
			List<Component> list = new List<Component>(pendingSources);
			pendingSources.Clear();
			foreach (Component item in list)
			{
				if (global::UnityObjectOps.Truthy((Object)(object)item) && item.gameObject.activeInHierarchy)
				{
					Started(item);
				}
			}
		}
		if (!CampaignSync.ClosingBattle && Session.IsHost && DeploySync.BattleStarted && Time.unscaledTime >= nextSample)
		{
			nextSample = Time.unscaledTime + 0.05f;
			List<Source> list2 = new List<Source>(sources.Values);
			using PoseBatch poseBatch = ((Session.Transport is SteamTransport) ? new PoseBatch(SendPosePacket, MsgType.WorldEvent) : null);
			foreach (Source item2 in list2)
			{
				if (!global::UnityObjectOps.Truthy((Object)(object)item2.Component) || !item2.Component.gameObject.activeInHierarchy)
				{
					poseBatch?.Flush();
					Ended(item2.Component);
					continue;
				}
				FireVisuals.ObserveArrow(item2.Component, item2.Id, ((Component)item2.Renderers[0]).transform);
				ProjectileNodePose[] array = Capture(item2);
				oldTrafficBytes += 49 + 52 * array.Length;
				if (poseBatch != null && VisualResources.HasPendingDefinitions)
				{
					poseBatch.Flush();
				}
				AgentAuthority.FlushResources();
				using MessageWriter messageWriter = Header(1);
				messageWriter.Write(item2.Id);
				WritePoses(messageWriter, array);
				if (poseBatch != null)
				{
					poseBatch.Add(messageWriter.ToArray());
				}
				else
				{
					SendPosePacket(messageWriter.ToArray());
				}
			}
		}
		if (Session.IsHost && Time.unscaledTime - trafficStart >= 5f)
		{
			float num = Time.unscaledTime - trafficStart;
			Plugin.Log.LogInfo((object)("[投射物流量] epoch=" + AgentAuthority.Epoch + " 活动=" + sources.Count + " 接受包=" + trafficPackets + " 拒绝=" + trafficRejected + " 姿态KB/s=" + ((float)trafficBytes / num / 1000f).ToString("F1") + " 同样本旧编码KB/s=" + ((float)oldTrafficBytes / num / 1000f).ToString("F1") + "（含会话头，非线路带宽）"));
			trafficStart = Time.unscaledTime;
			trafficBytes = (oldTrafficBytes = 0L);
			trafficPackets = (trafficRejected = 0);
		}
		if (CampaignSync.ClosingBattle || !AgentAuthority.ClientPlayback)
		{
			return;
		}
		float playbackTime = AgentAuthority.PlaybackTime;
		List<int> list3 = new List<int>();
		foreach (KeyValuePair<int, Playback> playback in playbacks)
		{
			Playback value = playback.Value;
			if (playbackTime >= value.End)
			{
				FireVisuals.RemoveOwner(0, playback.Key);
				Object.Destroy((Object)(object)value.Root);
				list3.Add(playback.Key);
			}
			else
			{
				if (playbackTime < value.Start || value.Poses.Count == 0)
				{
					continue;
				}
				while (value.Times.Count > 1 && value.Times[1] <= playbackTime)
				{
					value.Times.RemoveAt(0);
					value.Poses.RemoveAt(0);
				}
				ProjectileNodePose[] array2 = value.Poses[0];
				ProjectileNodePose[] array3 = ((value.Poses.Count > 1) ? value.Poses[1] : array2);
				float num2 = ((value.Times.Count > 1 && value.Times[1] > value.Times[0]) ? Mathf.Clamp01((playbackTime - value.Times[0]) / (value.Times[1] - value.Times[0])) : 0f);
				for (int i = 0; i < array2.Length; i++)
				{
					Renderer val = value.Renderers[i];
					ProjectileNodePose projectileNodePose = array2[i];
					ProjectileNodePose projectileNodePose2 = array3[i];
					ProjectileNodePose projectileNodePose3 = ((num2 >= 0.9999f) ? projectileNodePose2 : projectileNodePose);
					((Component)val).transform.position = Vector3.Lerp(projectileNodePose.Position, projectileNodePose2.Position, num2);
					((Component)val).transform.rotation = Quaternion.Slerp(projectileNodePose.Rotation, projectileNodePose2.Rotation, num2);
					((Component)val).transform.localScale = Vector3.Lerp(projectileNodePose.Scale, projectileNodePose2.Scale, num2);
					val.enabled = projectileNodePose3.Visible;
					SpriteRenderer val2 = (SpriteRenderer)(object)((val is SpriteRenderer) ? val : null);
					if (global::UnityObjectOps.Truthy((Object)(object)val2))
					{
						if (!VisualResources.TryResolve(projectileNodePose3.SpriteId, out var sprite) || !global::UnityObjectOps.Truthy((Object)(object)sprite))
						{
							((Component)val).gameObject.SetActive(false);
							continue;
						}
						val2.flipX = projectileNodePose3.FlipX;
						val2.flipY = projectileNodePose3.FlipY;
						if (value.Kinds[i] == 2)
						{
							BatchedSprite val3 = value.Batches[i];
							if (!global::UnityObjectOps.Truthy((Object)(object)val3))
							{
								val2.sprite = sprite;
								val2.color = projectileNodePose3.Color;
								val3 = ((Component)val2).gameObject.AddComponent<BatchedSprite>();
								tangentField.SetValue(val3, value.Tangents[i]);
								val3.Awake();
								value.Batches[i] = val3;
								value.Drawers[i] = GeneratedRenderers(val3);
							}
							val3.bSprite = sprite;
							val3.color = projectileNodePose3.Color;
							val3.UpdateScale();
							Renderer[] array4 = value.Drawers[i];
							foreach (Renderer val4 in array4)
							{
								if (global::UnityObjectOps.Truthy((Object)(object)val4))
								{
									val4.enabled = projectileNodePose3.Visible;
								}
							}
							((Renderer)val2).enabled = false;
						}
						else
						{
							val2.sprite = sprite;
							val2.color = projectileNodePose3.Color;
						}
					}
					((Component)val).gameObject.SetActive(true);
				}
				value.Root.SetActive(true);
			}
		}
		foreach (int item3 in list3)
		{
			playbacks.Remove(item3);
		}
		int num3 = 0;
		int num4 = effects.Count - 1;
		while (num4 >= 0 && num3 < 128)
		{
			if (!(effects[num4].Time > playbackTime))
			{
				PlayEffect(effects[num4]);
				effects.RemoveAt(num4);
				num3++;
			}
			num4--;
		}
	}

	private static void Receive(BinaryReader reader)
	{
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		byte b = reader.ReadByte();
		int num = reader.ReadInt32();
		float num2 = reader.ReadSingle();
		if (CampaignSync.ClosingBattle || !Session.IsClient || !global::UnityObjectOps.Truthy((Object)(object)island) || b != 3 || num != AgentAuthority.Epoch)
		{
			reader.BaseStream.Position = reader.BaseStream.Length;
			return;
		}
		if (float.IsNaN(num2) || float.IsInfinity(num2) || num2 < 0f)
		{
			throw new IOException("飞行物时间非法");
		}
		byte b2 = reader.ReadByte();
		if (b2 == 3)
		{
			if (Session.OptionalEnabled)
			{
				if (effects.Count >= 2048)
				{
					SceneVisuals.Warn("explosion-queue", "爆炸显示队列达到2048预算");
					return;
				}
				effects.Add(new Effect
				{
					Time = num2,
					Definition = VisualWire.ReadString(reader, 512),
					Position = WorldAuthority.ReadVector(reader),
					Normal = WorldAuthority.ReadVector(reader),
					Forward = WorldAuthority.ReadVector(reader)
				});
				reader.RequireEnd();
			}
			return;
		}
		if (b2 > 2)
		{
			throw new IOException("飞行物事件类型异常");
		}
		int num3 = reader.ReadInt32();
		if (num3 <= 0)
		{
			throw new IOException("飞行物编号非法");
		}
		byte kind = byte.MaxValue;
		string text = null;
		if (b2 == 0)
		{
			kind = reader.ReadByte();
			text = VisualWire.ReadString(reader, 512);
		}
		ProjectileNodePose[] array = ReadPoses(reader);
		reader.RequireEnd();
		Playback value;
		if (b2 == 0)
		{
			if (playbacks.ContainsKey(num3))
			{
				return;
			}
			if (playbacks.Count >= 4096)
			{
				AgentAuthority.Fail("飞行物数量超过安全上限");
				return;
			}
			value = Create(kind, text);
			if (value == null || value.Renderers.Length != array.Length)
			{
				if (value != null)
				{
					Object.Destroy((Object)(object)value.Root);
				}
				AgentAuthority.Fail("飞行物资源与主机不一致：" + text);
				return;
			}
			value.Start = num2;
			playbacks[num3] = value;
		}
		else if (!playbacks.TryGetValue(num3, out value))
		{
			return;
		}
		if (b2 == 2)
		{
			value.End = num2;
		}
		if (value.Renderers.Length != array.Length)
		{
			AgentAuthority.Fail("飞行物渲染节点在飞行中变化");
			return;
		}
		for (int i = 0; i < array.Length; i++)
		{
			if (value.Kinds[i] != array[i].Kind)
			{
				AgentAuthority.Fail("飞行物绘制节点类型与主机不一致");
				return;
			}
		}
		if (value.Times.Count <= 0 || !(num2 < value.Times[value.Times.Count - 1]))
		{
			if (value.Times.Count > 0 && num2 == value.Times[value.Times.Count - 1])
			{
				value.Times.RemoveAt(value.Times.Count - 1);
				value.Poses.RemoveAt(value.Poses.Count - 1);
			}
			value.Times.Add(num2);
			value.Poses.Add(array);
			if (value.Times.Count > 64)
			{
				value.Times.RemoveAt(0);
				value.Poses.RemoveAt(0);
			}
		}
	}

	private static Playback Create(byte kind, string name)
	{
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Expected O, but got Unknown
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Expected O, but got Unknown
		//IL_01f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_028f: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ca: Expected O, but got Unknown
		string key = kind + ":" + name;
		if (!templates.TryGetValue(key, out var value) || !global::UnityObjectOps.Truthy((Object)(object)value))
		{
			Type type = TypeFor(kind);
			if (type == null)
			{
				return null;
			}
			Object[] array = Resources.FindObjectsOfTypeAll(type);
			foreach (Object obj in array)
			{
				Component val = (Component)(object)((obj is Component) ? obj : null);
				if (global::UnityObjectOps.Truthy((Object)(object)val) && CleanName(((Object)val).name) == name)
				{
					value = val;
					break;
				}
			}
			if (!global::UnityObjectOps.Truthy((Object)(object)value))
			{
				return null;
			}
			templates[key] = value;
		}
		Renderer[] array2 = Renderers(value);
		Playback playback = new Playback
		{
			Root = new GameObject("Together_Projectile_" + name),
			Renderers = (Renderer[])(object)new Renderer[array2.Length],
			Batches = (BatchedSprite[])(object)new BatchedSprite[array2.Length],
			Drawers = new Renderer[array2.Length][],
			Kinds = new byte[array2.Length],
			Tangents = new float[array2.Length]
		};
		playback.Root.transform.SetParent(island.runContainer, false);
		playback.Root.SetActive(false);
		for (int j = 0; j < array2.Length; j++)
		{
			Renderer val2 = array2[j];
			GameObject val3 = new GameObject(((Object)val2).name);
			val3.layer = ((Component)val2).gameObject.layer;
			val3.transform.SetParent(playback.Root.transform, false);
			SpriteRenderer val4 = (SpriteRenderer)(object)((val2 is SpriteRenderer) ? val2 : null);
			BatchedSprite val5 = (global::UnityObjectOps.Truthy((Object)(object)val4) ? ((Component)val4).GetComponent<BatchedSprite>() : null);
			playback.Kinds[j] = (global::UnityObjectOps.Truthy((Object)(object)val5) ? ((byte)2) : (global::UnityObjectOps.Truthy((Object)(object)val4) ? ((byte)1) : ((byte)0)));
			if (global::UnityObjectOps.Truthy((Object)(object)val5))
			{
				playback.Tangents[j] = (float)tangentField.GetValue(val5);
			}
			Renderer val6;
			if (global::UnityObjectOps.Truthy((Object)(object)val4))
			{
				SpriteRenderer obj2 = val3.AddComponent<SpriteRenderer>();
				obj2.sprite = val4.sprite;
				obj2.color = val4.color;
				obj2.flipX = val4.flipX;
				obj2.flipY = val4.flipY;
				val6 = (Renderer)(object)obj2;
			}
			else
			{
				MeshFilter component = ((Component)val2).GetComponent<MeshFilter>();
				if (!global::UnityObjectOps.Truthy((Object)(object)component) || !global::UnityObjectOps.Truthy((Object)(object)component.sharedMesh))
				{
					Object.Destroy((Object)(object)playback.Root);
					return null;
				}
				val3.AddComponent<MeshFilter>().sharedMesh = component.sharedMesh;
				val6 = (Renderer)(object)val3.AddComponent<MeshRenderer>();
			}
			val6.sharedMaterials = val2.sharedMaterials;
			val6.sortingLayerID = val2.sortingLayerID;
			val6.sortingOrder = val2.sortingOrder;
			val6.shadowCastingMode = val2.shadowCastingMode;
			val6.receiveShadows = val2.receiveShadows;
			val6.lightProbeUsage = val2.lightProbeUsage;
			val6.reflectionProbeUsage = val2.reflectionProbeUsage;
			MaterialPropertyBlock val7 = new MaterialPropertyBlock();
			val2.GetPropertyBlock(val7);
			val6.SetPropertyBlock(val7);
			playback.Renderers[j] = val6;
		}
		return playback;
	}

	private static void Explosion(Vector3 position, Vector3 normal, Vector3 forward, ExplosionDef definition)
	{
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		if (!Session.OptionalEnabled || CampaignSync.ClosingBattle || !Session.IsHost || !global::UnityObjectOps.Truthy((Object)(object)island) || !DeploySync.BattleStarted || !global::UnityObjectOps.Truthy((Object)(object)definition))
		{
			return;
		}
		using MessageWriter messageWriter = Header(3);
		VisualWire.WriteString(messageWriter, ((Object)definition).name, 512);
		WorldAuthority.WriteVector(messageWriter, position);
		WorldAuthority.WriteVector(messageWriter, normal);
		WorldAuthority.WriteVector(messageWriter, forward);
		Session.SendOptional(messageWriter.ToArray());
	}

	private static void PlayEffect(Effect effect)
	{
		//IL_0138: Unknown result type (might be due to invalid IL or missing references)
		//IL_0157: Unknown result type (might be due to invalid IL or missing references)
		//IL_014f: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0162: Unknown result type (might be due to invalid IL or missing references)
		//IL_0167: Unknown result type (might be due to invalid IL or missing references)
		Object[] array = Resources.FindObjectsOfTypeAll(typeof(ExplosionDef));
		foreach (Object obj in array)
		{
			ExplosionDef val = (ExplosionDef)(object)((obj is ExplosionDef) ? obj : null);
			if (!global::UnityObjectOps.Truthy((Object)(object)val) || ((Object)val).name != effect.Definition)
			{
				continue;
			}
			if (global::UnityObjectOps.Truthy((Object)(object)val.explosionParticle))
			{
				ParticleSystem[] componentsInChildren = ((Component)((ReusableEffect)val.explosionParticle).GetOrCreate((Transform)null)).GetComponentsInChildren<ParticleSystem>(true);
				foreach (ParticleSystem item3 in new List<ParticleSystem>(effectParticles))
				{
					if (!global::UnityObjectOps.Truthy((Object)(object)item3))
					{
						effectParticles.Remove(item3);
					}
				}
				int num = 0;
				ParticleSystem[] array2 = componentsInChildren;
				foreach (ParticleSystem item in array2)
				{
					if (!effectParticles.Contains(item))
					{
						num++;
					}
				}
				if (effectParticles.Count + num > 512)
				{
					SceneVisuals.Warn("explosion-particles", "爆炸粒子追踪达到512预算，跳过本次显示");
					return;
				}
				array2 = componentsInChildren;
				foreach (ParticleSystem item2 in array2)
				{
					effectParticles.Add(item2);
				}
				((ReusableEffect)val.explosionParticle).PlayAt(effect.Position, Quaternion.LookRotation((effect.Forward.sqrMagnitude > 0.001f) ? effect.Forward : Vector3.forward, effect.Normal), Vector3.one);
			}
			FabricWrapper.PostEvent(val.fabricAudioID, ((Component)island).gameObject);
			return;
		}
		Plugin.Log.LogWarning((object)("[场景] 未找到爆炸显示资源 " + effect.Definition));
	}
}
