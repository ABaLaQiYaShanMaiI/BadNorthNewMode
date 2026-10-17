using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Together.Net;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.SpriteMagic;

namespace Together;

internal static class SurfaceMarks
{
	[HarmonyPatch(typeof(SpriteStamperRoot), "Add")]
	private static class AddPatch
	{
		private static void Prefix(SpriteStamperRoot __instance, SpriteStampDef stamp)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			Capture(__instance, stamp, clear: false);
		}
	}

	[HarmonyPatch(typeof(SpriteStamperRoot), "ReturnTrunk")]
	private static class ClearPatch
	{
		private static void Prefix(SpriteStamperRoot __instance)
		{
			//IL_0003: Unknown result type (might be due to invalid IL or missing references)
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			Capture(__instance, default(SpriteStampDef), clear: true);
		}
	}

	private static readonly SurfaceMarkQueue pending = new SurfaceMarkQueue();

	private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);

	private static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);

	private static readonly HashSet<SpriteStamperRoot> applied = new HashSet<SpriteStamperRoot>();

	private static float nextPrune;

	private static bool initialized;

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			Session.On(MsgType.SurfaceMark, Receive);
			Session.Stopped += Reset;
			Session.OptionalStopped += Reset;
		}
	}

	public static void Reset()
	{
		foreach (SpriteStamperRoot item in applied)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)item))
			{
				item.ReturnTrunk();
			}
		}
		applied.Clear();
		pending.Clear();
		materials.Clear();
		warned.Clear();
		nextPrune = 0f;
	}

	private static void Capture(SpriteStamperRoot surface, SpriteStampDef stamp, bool clear)
	{
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_011b: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0178: Unknown result type (might be due to invalid IL or missing references)
		//IL_0189: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Unknown result type (might be due to invalid IL or missing references)
		//IL_018f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0194: Unknown result type (might be due to invalid IL or missing references)
		//IL_0199: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01db: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0203: Unknown result type (might be due to invalid IL or missing references)
		//IL_0204: Unknown result type (might be due to invalid IL or missing references)
		if (!SceneVisuals.Capturing || !global::UnityObjectOps.Truthy((Object)(object)surface))
		{
			return;
		}
		try
		{
			if (!SceneVisuals.Describe(((Component)surface).transform, out var kind, out var id, out var names, out var ordinals))
			{
				return;
			}
			SurfaceMarkData surfaceMarkData = new SurfaceMarkData
			{
				AnchorKind = kind,
				ActorId = id,
				Time = AgentAuthority.HostTime,
				Names = names,
				Ordinals = ordinals,
				Clear = clear
			};
			if (!clear)
			{
				if (!global::UnityObjectOps.Truthy((Object)(object)stamp.sprite) || !global::UnityObjectOps.Truthy((Object)(object)stamp.material) || !global::UnityObjectOps.Truthy((Object)(object)stamp.material.shader))
				{
					return;
				}
				surfaceMarkData.SpriteId = VisualResources.GetOptionalId(stamp.sprite);
				AgentAuthority.FlushResources(cosmetic: true);
				surfaceMarkData.Grounded = stamp.navPos.valid;
				if (surfaceMarkData.Grounded)
				{
					surfaceMarkData.NavPosition[0] = stamp.navPos.pos.x;
					surfaceMarkData.NavPosition[1] = stamp.navPos.pos.y;
					surfaceMarkData.NavPosition[2] = stamp.navPos.pos.z;
				}
				surfaceMarkData.Material = ((Object)stamp.material).name;
				surfaceMarkData.Shader = ((Object)stamp.material.shader).name;
				surfaceMarkData.Capacity = surface.maxCount;
				surfaceMarkData.Loose = surface.loose;
				surfaceMarkData.Layer = stamp.layer;
				surfaceMarkData.FlipX = stamp.flipX;
				surfaceMarkData.FlipY = stamp.flipY;
				Matrix4x4 val = ((Component)surface).transform.worldToLocalMatrix * stamp.matrix;
				for (int i = 0; i < 16; i++)
				{
					surfaceMarkData.Matrix[i] = val[i];
				}
				surfaceMarkData.Color[0] = stamp.color.r;
				surfaceMarkData.Color[1] = stamp.color.g;
				surfaceMarkData.Color[2] = stamp.color.b;
				surfaceMarkData.Color[3] = stamp.color.a;
			}
			using MessageWriter messageWriter = Protocol.Begin(MsgType.SurfaceMark);
			messageWriter.Write(14);
			messageWriter.Write(AgentAuthority.Epoch);
			surfaceMarkData.Write(messageWriter);
			if (!Session.SendOptional(messageWriter.ToArray()))
			{
				Warn("send", "附着印记发送失败");
			}
		}
		catch (Exception ex)
		{
			Warn("capture", "附着印记采样失败：对象=" + ((Object)surface).name + " 类型=" + ((object)surface).GetType().FullName + " 容量=" + surface.maxCount + " loose=" + surface.loose + "；" + ex.Message);
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
			Warn("read", "跳过无效印记事件：" + ex.Message);
		}
	}

	private static void ReadEvent(BinaryReader reader)
	{
		if (reader.ReadInt32() != 14)
		{
			throw new IOException("附着印记协议版本不同");
		}
		int num = reader.ReadInt32();
		SurfaceMarkData data = SurfaceMarkData.Read(reader);
		reader.RequireEnd();
		if (Session.OptionalEnabled && !CampaignSync.ClosingBattle && Session.IsClient && AgentAuthority.ClientPlayback && num == AgentAuthority.Epoch && !pending.Add(data, Time.unscaledTime))
		{
			Warn("capacity", "附着印记积压，已跳过新的装饰事件");
		}
	}

	public static void LateTick()
	{
		if (Time.unscaledTime >= nextPrune)
		{
			nextPrune = Time.unscaledTime + 1f;
			foreach (SpriteStamperRoot item in new List<SpriteStamperRoot>(applied))
			{
				if (!global::UnityObjectOps.Truthy((Object)(object)item))
				{
					applied.Remove(item);
				}
			}
		}
		if (AgentAuthority.ClientPlayback && !AgentAuthority.HasFailure)
		{
			pending.Process(AgentAuthority.PlaybackTime, Time.unscaledTime, TryApply, Expired);
		}
	}

	private static bool TryApply(SurfaceMarkData data)
	{
		if (SceneVisuals.Retired(data.AnchorKind, data.ActorId))
		{
			return true;
		}
		try
		{
			return Apply(data);
		}
		catch (Exception ex)
		{
			Warn("apply", "附着印记播放失败：" + ex.Message);
			return true;
		}
	}

	private static void Expired(SurfaceMarkData data)
	{
		Warn(data.Key, "附着印记等待资源或表面超时，锚点 " + data.AnchorKind + ":" + data.ActorId + " " + VisualHierarchy.DisplayPath(data.Names, data.Ordinals));
	}

	private static bool Apply(SurfaceMarkData data)
	{
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		//IL_016c: Unknown result type (might be due to invalid IL or missing references)
		//IL_016e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0226: Unknown result type (might be due to invalid IL or missing references)
		//IL_022f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0234: Unknown result type (might be due to invalid IL or missing references)
		//IL_0236: Unknown result type (might be due to invalid IL or missing references)
		//IL_023b: Unknown result type (might be due to invalid IL or missing references)
		//IL_027a: Unknown result type (might be due to invalid IL or missing references)
		//IL_027c: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ad: Unknown result type (might be due to invalid IL or missing references)
		Transform val = SceneVisuals.Resolve(data.AnchorKind, data.ActorId, data.Names, data.Ordinals);
		if (!global::UnityObjectOps.Truthy((Object)(object)val))
		{
			return false;
		}
		SpriteStamperRoot val2 = ((Component)val).GetComponent<SpriteStamperRoot>();
		if (data.Clear)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)val2))
			{
				val2.ReturnTrunk();
			}
			return true;
		}
		if (!VisualResources.TryResolve(data.SpriteId, out var sprite) || !global::UnityObjectOps.Truthy((Object)(object)sprite))
		{
			return false;
		}
		string key = data.Material + "|" + data.Shader;
		if (!materials.TryGetValue(key, out var value) || !global::UnityObjectOps.Truthy((Object)(object)value))
		{
			Material[] array = Resources.FindObjectsOfTypeAll<Material>();
			foreach (Material val3 in array)
			{
				if (global::UnityObjectOps.Truthy((Object)(object)val3) && global::UnityObjectOps.Truthy((Object)(object)val3.shader) && ((Object)val3).name == data.Material && ((Object)val3.shader).name == data.Shader)
				{
					value = val3;
					materials[key] = val3;
					break;
				}
			}
			if (!global::UnityObjectOps.Truthy((Object)(object)value))
			{
				return false;
			}
		}
		NavPos empty = NavPos.empty;
		if (data.Grounded)
		{
			NavigationMesh val4 = SceneVisuals.Navigation(data.AnchorKind, data.ActorId);
			if (!global::UnityObjectOps.Truthy((Object)(object)val4))
			{
				return false;
			}
			Vector3 val5 = default(Vector3);
			val5 = new Vector3(data.NavPosition[0], data.NavPosition[1], data.NavPosition[2]);
			empty = new NavPos(val4, ((Component)val4).transform.TransformPoint(val5), true, 1f);
			if (!empty.valid)
			{
				return false;
			}
		}
		if ((!global::UnityObjectOps.Truthy((Object)(object)val2) || !applied.Contains(val2)) && applied.Count >= 2048)
		{
			Warn("surfaces", "印记表面追踪达到2048预算，跳过新的装饰表面");
			return true;
		}
		if (!global::UnityObjectOps.Truthy((Object)(object)val2))
		{
			val2 = ((Component)val).gameObject.AddComponent<SpriteStamperRoot>();
		}
		applied.Add(val2);
		val2.maxCount = data.Capacity;
		val2.loose = data.Loose;
		Matrix4x4 val6 = default(Matrix4x4);
		for (int j = 0; j < 16; j++)
		{
			val6[j] = data.Matrix[j];
		}
		val2.Add(new SpriteStampDef
		{
			matrix = val.localToWorldMatrix * val6,
			sprite = sprite,
			material = value,
			layer = data.Layer,
			flipX = data.FlipX,
			flipY = data.FlipY,
			navPos = empty,
			color = new Color(data.Color[0], data.Color[1], data.Color[2], data.Color[3])
		});
		return true;
	}

	private static void Warn(string key, string reason)
	{
		if (warned.Count < 128 && warned.Add(key))
		{
			Plugin.Log.LogWarning((object)("[表面印记] " + reason));
		}
	}
}
