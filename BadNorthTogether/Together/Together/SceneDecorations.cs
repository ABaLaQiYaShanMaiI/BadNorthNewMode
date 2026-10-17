using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Together.Net;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.SpriteMagic;

namespace Together;

internal static class SceneDecorations
{
	private sealed class Appearance
	{
		public MaterialPropertyBlock Block;

		public Color32 Tint;

		public string AtlasKey;
	}

	private sealed class RetiredAppearance
	{
		public Appearance Appearance;

		public float Expires;
	}

	private sealed class Pending
	{
		public SceneDecorationData Data;

		public Appearance Appearance;
	}

	private sealed class Piece
	{
		public SceneDecorationData Data;

		public Color32 Tint;
	}

	private sealed class Batch
	{
		public GameObject Root;

		public Mesh Mesh;

		public MeshRenderer Renderer;

		public readonly Dictionary<int, Piece> Pieces = new Dictionary<int, Piece>();

		public bool Dirty;

		public int Capacity;

		public int AnchorId;

		public int VertexCount;

		public byte AnchorKind;

		public string SourceKey;
	}

	[HarmonyPatch(typeof(CorpseObject), "AddCorpse")]
	private static class CorpsePatch
	{
		private static void Postfix(CorpseObject __instance, SpriteAnimator spriteAnimator)
		{
			CaptureCorpse(__instance, spriteAnimator);
		}
	}

	[HarmonyPatch(typeof(CorpseObject), "Wipe")]
	private static class WipePatch
	{
		private static void Postfix(CorpseObject __instance)
		{
			CaptureClear(__instance);
		}
	}

	[HarmonyPatch(typeof(MeshPainter), "Paint")]
	private static class PaintPatch
	{
		private static void Postfix(MeshPainter __instance, Vector3 worldPos)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			CapturePaint(__instance, worldPos);
		}
	}

	private static readonly FieldInfo corpseMesh = AccessTools.Field(typeof(CorpseObject), "corpseMesh");

	private static readonly Type meshType = typeof(CorpseObject).GetNestedType("CorpseMesh", BindingFlags.NonPublic);

	private static readonly FieldInfo slots = AccessTools.Field(meshType, "ms");

	private static readonly FieldInfo index = AccessTools.Field(meshType, "index");

	private static readonly FieldInfo vertices = AccessTools.Field(meshType, "mVerts");

	private static readonly FieldInfo colors = AccessTools.Field(meshType, "mColor");

	private static readonly Type slotType = meshType.GetNestedType("M", BindingFlags.NonPublic);

	private static readonly FieldInfo slotStart = AccessTools.Field(slotType, "start");

	private static readonly FieldInfo slotCount = AccessTools.Field(slotType, "count");

	private static readonly FieldInfo slotMesh = AccessTools.Field(slotType, "meshIndex");

	private static readonly Dictionary<CorpseObject, int> groups = new Dictionary<CorpseObject, int>();

	private static readonly Dictionary<string, Batch> batches = new Dictionary<string, Batch>(StringComparer.Ordinal);

	private static readonly Dictionary<string, Batch> slotOwners = new Dictionary<string, Batch>(StringComparer.Ordinal);

	private static readonly VisualEventQueue<Pending> pending = new VisualEventQueue<Pending>();

	private static int nextGroup;

	private static int sequence;

	private static int lastSequence;

	private static int storedVertices;

	private static float nextPrune;

	private static bool initialized;

	private static Material landMaterial;

	private static readonly Dictionary<int, RetiredAppearance> retiredAppearances = new Dictionary<int, RetiredAppearance>();

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			Session.On(MsgType.SceneDecoration, Receive);
			Session.Stopped += Reset;
			Session.OptionalStopped += Reset;
		}
	}

	public static void Reset()
	{
		foreach (Batch value in batches.Values)
		{
			Destroy(value);
		}
		if (global::UnityObjectOps.Truthy((Object)(object)landMaterial))
		{
			Object.Destroy((Object)(object)landMaterial);
		}
		landMaterial = null;
		batches.Clear();
		slotOwners.Clear();
		groups.Clear();
		pending.Clear();
		retiredAppearances.Clear();
		nextGroup = (sequence = (lastSequence = (storedVertices = 0)));
		nextPrune = 0f;
	}

	private static void Destroy(Batch batch)
	{
		storedVertices -= batch.VertexCount;
		foreach (int key in batch.Pieces.Keys)
		{
			slotOwners.Remove(batch.SourceKey + ":" + key);
		}
		if (global::UnityObjectOps.Truthy((Object)(object)batch.Root))
		{
			Object.Destroy((Object)(object)batch.Root);
		}
		if (global::UnityObjectOps.Truthy((Object)(object)batch.Mesh))
		{
			Object.Destroy((Object)(object)batch.Mesh);
		}
	}

	private static void Send(SceneDecorationData data)
	{
		data.Time = AgentAuthority.HostTime;
		data.Sequence = ++sequence;
		using MessageWriter messageWriter = Protocol.Begin(MsgType.SceneDecoration);
		messageWriter.Write(14);
		messageWriter.Write(AgentAuthority.Epoch);
		data.Write(messageWriter);
		if (!Session.SendOptional(messageWriter.ToArray()))
		{
			SceneVisuals.Warn("decoration-send", "尸体/染血事件发送失败，未中断战斗");
		}
	}

	private static void CaptureCorpse(CorpseObject source, SpriteAnimator animator)
	{
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01da: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f4: Unknown result type (might be due to invalid IL or missing references)
		if (!SceneVisuals.Capturing || !global::UnityObjectOps.Truthy((Object)(object)source) || !global::UnityObjectOps.Truthy((Object)(object)animator))
		{
			return;
		}
		try
		{
			if (!SceneVisuals.DescribeWorld(((Component)source).transform, out var kind, out var id, out var root))
			{
				return;
			}
			int num = AgentAuthority.FindId(((Component)animator).GetComponentInParent<Agent>());
			if (num == 0)
			{
				return;
			}
			object value = corpseMesh.GetValue(source);
			Array array = (Array)slots.GetValue(value);
			int slot = ((int)index.GetValue(value) + array.Length - 1) % array.Length;
			object value2 = array.GetValue(slot);
			int num2 = (int)slotStart.GetValue(value2);
			int num3 = (int)slotCount.GetValue(value2);
			if (!groups.TryGetValue(source, out var value3))
			{
				if (groups.Count >= 256)
				{
					SceneVisuals.Warn("corpse-groups", "尸体材质批次达到装饰上限");
					return;
				}
				groups.Add(source, value3 = ++nextGroup);
			}
			SceneDecorationData sceneDecorationData = new SceneDecorationData
			{
				Kind = 0,
				AnchorKind = kind,
				AnchorId = id,
				Group = value3,
				ActorId = num,
				Capacity = array.Length,
				Slot = slot,
				MeshIndex = (int)slotMesh.GetValue(value2),
				Vertices = (Vector3[])(object)new Vector3[num3],
				Colors = (Color32[])(object)new Color32[num3]
			};
			Vector3[] array2 = (Vector3[])vertices.GetValue(value);
			Color32[] array3 = (Color32[])colors.GetValue(value);
			Matrix4x4 val = root.worldToLocalMatrix * ((Component)source).transform.localToWorldMatrix;
			for (int i = 0; i < num3; i++)
			{
				sceneDecorationData.Vertices[i] = val.MultiplyPoint3x4(array2[num2 + i]);
				sceneDecorationData.Colors[i] = array3[num2 + i];
			}
			Send(sceneDecorationData);
		}
		catch (Exception ex)
		{
			SceneVisuals.Warn("corpse-capture", "尸体采样失败：" + ex.Message);
		}
	}

	private static void CapturePaint(MeshPainter painter, Vector3 worldPos)
	{
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		if (!SceneVisuals.Capturing)
		{
			return;
		}
		try
		{
			if (SceneVisuals.Describe(((Component)painter).transform, out var kind, out var id, out var names, out var ordinals) && kind != 0)
			{
				Transform val = SceneVisuals.Root(kind, id);
				Send(new SceneDecorationData
				{
					Kind = 1,
					AnchorKind = kind,
					AnchorId = id,
					Names = names,
					Ordinals = ordinals,
					Position = val.InverseTransformPoint(worldPos)
				});
			}
		}
		catch (Exception ex)
		{
			SceneVisuals.Warn("paint-capture", "船面染血采样失败：" + ex.Message);
		}
	}

	private static void CaptureClear(CorpseObject source)
	{
		if (!SceneVisuals.Capturing)
		{
			return;
		}
		try
		{
			if (groups.TryGetValue(source, out var value) && SceneVisuals.DescribeWorld(((Component)source).transform, out var kind, out var id, out var _))
			{
				Send(new SceneDecorationData
				{
					Kind = 2,
					AnchorKind = kind,
					AnchorId = id,
					Group = value
				});
			}
		}
		catch (Exception ex)
		{
			SceneVisuals.Warn("corpse-clear", "尸体清除事件失败：" + ex.Message);
		}
	}

	private static void Prepare(Pending entry, Agent agent)
	{
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		if (entry.Appearance == null && entry.Data != null && retiredAppearances.TryGetValue(entry.Data.ActorId, out var value) && Time.unscaledTime <= value.Expires)
		{
			entry.Appearance = value.Appearance;
		}
		if (entry.Appearance == null && global::UnityObjectOps.Truthy((Object)(object)agent) && global::UnityObjectOps.Truthy((Object)(object)agent.spriteAnimator))
		{
			SpriteAnimator spriteAnimator = agent.spriteAnimator;
			if (global::UnityObjectOps.Truthy((Object)(object)spriteAnimator.sprite2))
			{
				spriteAnimator.UpdateSprite2();
			}
			MaterialPropertyBlock materialPropertyBlock = ((BatchedSprite)spriteAnimator).GetMaterialPropertyBlock();
			Texture texture = materialPropertyBlock.GetTexture(Shader.PropertyToID("_MainTex"));
			Texture texture2 = materialPropertyBlock.GetTexture(Shader.PropertyToID("_PartTex"));
			entry.Appearance = new Appearance
			{
				Block = materialPropertyBlock,
				Tint = (Color32)(((BatchedSprite)spriteAnimator).color),
				AtlasKey = (global::UnityObjectOps.Truthy((Object)(object)texture) ? ((Object)texture).GetInstanceID() : 0) + ":" + (global::UnityObjectOps.Truthy((Object)(object)texture2) ? ((Object)texture2).GetInstanceID() : 0)
			};
		}
	}

	internal static void BeforeActorRemoval(int id, Agent agent)
	{
		try
		{
			if (!Session.OptionalEnabled)
			{
				return;
			}
			Pending pending = new Pending
			{
				Data = new SceneDecorationData
				{
					ActorId = id
				}
			};
			Prepare(pending, agent);
			if (pending.Appearance != null)
			{
				if (retiredAppearances.Count >= 512 && !retiredAppearances.ContainsKey(id))
				{
					int key = 0;
					float num = float.MaxValue;
					foreach (KeyValuePair<int, RetiredAppearance> retiredAppearance in retiredAppearances)
					{
						if (retiredAppearance.Value.Expires < num)
						{
							key = retiredAppearance.Key;
							num = retiredAppearance.Value.Expires;
						}
					}
					retiredAppearances.Remove(key);
				}
				retiredAppearances[id] = new RetiredAppearance
				{
					Appearance = pending.Appearance,
					Expires = Time.unscaledTime + 10f
				};
			}
			SceneDecorations.pending.Visit(delegate(Pending entry)
			{
				if (entry.Data.Kind == 0 && entry.Data.ActorId == id)
				{
					Prepare(entry, agent);
				}
			});
		}
		catch (Exception ex)
		{
			SceneVisuals.Warn("corpse-retire", "退场前保存尸体材质失败：" + ex.Message);
		}
	}

	private static void Receive(BinaryReader reader)
	{
		try
		{
			int num = reader.ReadInt32();
			int num2 = reader.ReadInt32();
			SceneDecorationData sceneDecorationData = SceneDecorationData.Read(reader);
			reader.RequireEnd();
			if (Session.OptionalEnabled && !CampaignSync.ClosingBattle && num == 14 && num2 == AgentAuthority.Epoch && AgentAuthority.ClientPlayback && Session.IsClient && sceneDecorationData.Sequence > lastSequence)
			{
				lastSequence = sceneDecorationData.Sequence;
				Pending pending = new Pending
				{
					Data = sceneDecorationData
				};
				if (sceneDecorationData.Kind == 0)
				{
					Prepare(pending, AgentAuthority.FindAgent(sceneDecorationData.ActorId));
				}
				if (!SceneDecorations.pending.Add(pending, sceneDecorationData.Key, sceneDecorationData.Time, Time.unscaledTime))
				{
					SceneVisuals.Warn("decoration-queue", "尸体/染血装饰队列已满");
				}
			}
		}
		catch (Exception ex)
		{
			SceneVisuals.Warn("decoration-read", "跳过无效装饰事件：" + ex.Message);
		}
	}

	public static void LateTick()
	{
		if (Time.unscaledTime >= nextPrune)
		{
			nextPrune = Time.unscaledTime + 1f;
			foreach (int item in new List<int>(retiredAppearances.Keys))
			{
				if (Time.unscaledTime > retiredAppearances[item].Expires)
				{
					retiredAppearances.Remove(item);
				}
			}
			foreach (CorpseObject item2 in new List<CorpseObject>(groups.Keys))
			{
				if (!global::UnityObjectOps.Truthy((Object)(object)item2))
				{
					groups.Remove(item2);
				}
			}
			foreach (string item3 in new List<string>(batches.Keys))
			{
				Batch batch = batches[item3];
				if (!global::UnityObjectOps.Truthy((Object)(object)batch.Root) || SceneVisuals.Retired(batch.AnchorKind, batch.AnchorId))
				{
					Destroy(batch);
					batches.Remove(item3);
				}
			}
		}
		if (!AgentAuthority.ClientPlayback || AgentAuthority.HasFailure)
		{
			return;
		}
		pending.Process(AgentAuthority.PlaybackTime, Time.unscaledTime, ApplySafe, delegate
		{
			SceneVisuals.Warn("decoration-wait", "尸体/染血等待人物或船体资源超时");
		});
		foreach (Batch value in batches.Values)
		{
			if (value.Dirty && global::UnityObjectOps.Truthy((Object)(object)value.Root))
			{
				try
				{
					Rebuild(value);
				}
				catch (Exception ex)
				{
					SceneVisuals.Warn("corpse-build", "尸体绘制失败：" + ex.Message);
				}
				value.Dirty = false;
			}
		}
	}

	private static bool ApplySafe(Pending value)
	{
		try
		{
			return Apply(value);
		}
		catch (Exception ex)
		{
			SceneVisuals.Warn("decoration-apply", "跳过装饰播放错误：" + ex.Message);
			return true;
		}
	}

	private static bool Apply(Pending value)
	{
		//IL_0105: Unknown result type (might be due to invalid IL or missing references)
		//IL_010a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01be: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c5: Expected O, but got Unknown
		//IL_01fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0204: Expected O, but got Unknown
		//IL_0409: Unknown result type (might be due to invalid IL or missing references)
		//IL_040e: Unknown result type (might be due to invalid IL or missing references)
		SceneDecorationData data = value.Data;
		if (SceneVisuals.Retired(data.AnchorKind, data.AnchorId))
		{
			return true;
		}
		if (data.Kind == 2)
		{
			foreach (string item in new List<string>(batches.Keys))
			{
				if (batches[item].SourceKey == data.Key)
				{
					Destroy(batches[item]);
					batches.Remove(item);
				}
			}
			return true;
		}
		Transform val = SceneVisuals.Root(data.AnchorKind, data.AnchorId);
		if (!global::UnityObjectOps.Truthy((Object)(object)val))
		{
			return false;
		}
		if (data.Kind == 1)
		{
			Transform val2 = SceneVisuals.Resolve(data.AnchorKind, data.AnchorId, data.Names, data.Ordinals);
			MeshPainter val3 = (global::UnityObjectOps.Truthy((Object)(object)val2) ? ((Component)val2).GetComponent<MeshPainter>() : null);
			if (!global::UnityObjectOps.Truthy((Object)(object)val3))
			{
				return false;
			}
			val3.Paint(val.TransformPoint(data.Position));
			return true;
		}
		Prepare(value, AgentAuthority.FindAgent(data.ActorId));
		if (value.Appearance == null)
		{
			return false;
		}
		Mesh[] corpseMeshes = ScriptableObjectSingleton<PrefabManager>.instance.corpseMeshes;
		if (data.MeshIndex >= corpseMeshes.Length || corpseMeshes[data.MeshIndex].vertexCount != data.Vertices.Length)
		{
			throw new IOException("本机尸体模板与主机不一致");
		}
		string key = data.Key + ":" + value.Appearance.AtlasKey;
		if (!batches.TryGetValue(key, out var value2))
		{
			if (batches.Count >= 128)
			{
				SceneVisuals.Warn("corpse-limit", "尸体显示批次数已达上限");
				return true;
			}
			GameObject val4 = new GameObject("Together_Corpses");
			val4.transform.SetParent(val, false);
			val4.AddComponent<LocalVisualObject>();
			val4.layer = LayerMaster.blood.id;
			value2 = new Batch
			{
				Root = val4,
				Mesh = new Mesh(),
				Renderer = val4.AddComponent<MeshRenderer>(),
				Capacity = data.Capacity,
				AnchorKind = data.AnchorKind,
				AnchorId = data.AnchorId,
				SourceKey = data.Key
			};
			val4.AddComponent<MeshFilter>().sharedMesh = value2.Mesh;
			Material corpseMaterial = ScriptableObjectSingleton<PrefabManager>.instance.corpseMaterial;
			if (data.AnchorKind == 1)
			{
				if (!global::UnityObjectOps.Truthy((Object)(object)landMaterial))
				{
					landMaterial = Object.Instantiate<Material>(corpseMaterial);
					landMaterial.EnableKeyword("_ISLAND_ON");
				}
				corpseMaterial = landMaterial;
			}
			((Renderer)value2.Renderer).sharedMaterial = corpseMaterial;
			((Renderer)value2.Renderer).SetPropertyBlock(value.Appearance.Block);
			batches.Add(key, value2);
		}
		if (value2.Capacity != data.Capacity)
		{
			throw new IOException("尸体槽位容量在同一批次内变化");
		}
		string key2 = data.Key + ":" + data.Slot;
		int num = (slotOwners.TryGetValue(key2, out var value3) ? value3.Pieces[data.Slot].Data.Vertices.Length : 0);
		int num2 = data.Vertices.Length - num;
		int num3 = data.Vertices.Length - ((value3 == value2) ? num : 0);
		if (storedVertices + num2 > 120000 || value2.VertexCount + num3 > 60000)
		{
			SceneVisuals.Warn("corpse-vertices", "尸体显示顶点达到装饰预算");
			return true;
		}
		if (value3 != null)
		{
			value3.Pieces.Remove(data.Slot);
			value3.VertexCount -= num;
			value3.Dirty = true;
		}
		storedVertices += num2;
		value2.VertexCount += data.Vertices.Length;
		slotOwners[key2] = value2;
		value2.Pieces[data.Slot] = new Piece
		{
			Data = data,
			Tint = value.Appearance.Tint
		};
		value2.Dirty = true;
		return true;
	}

	private static void Rebuild(Batch batch)
	{
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0178: Unknown result type (might be due to invalid IL or missing references)
		List<Vector3> list = new List<Vector3>();
		List<Vector2> list2 = new List<Vector2>();
		List<Vector2> list3 = new List<Vector2>();
		List<Vector3> list4 = new List<Vector3>();
		List<Vector4> list5 = new List<Vector4>();
		List<Color32> list6 = new List<Color32>();
		List<int> list7 = new List<int>();
		Mesh[] corpseMeshes = ScriptableObjectSingleton<PrefabManager>.instance.corpseMeshes;
		foreach (Piece value in batch.Pieces.Values)
		{
			SceneDecorationData data = value.Data;
			Mesh val = corpseMeshes[data.MeshIndex];
			int count = list.Count;
			if (count + data.Vertices.Length > 60000)
			{
				break;
			}
			Vector2[] uv = val.uv;
			Vector2[] uv2 = val.uv2;
			Vector3[] normals = val.normals;
			Vector4[] tangents = val.tangents;
			list.AddRange(data.Vertices);
			for (int i = 0; i < data.Vertices.Length; i++)
			{
				list2.Add((i < uv.Length) ? uv[i] : Vector2.zero);
				list3.Add((i < uv2.Length) ? uv2[i] : Vector2.zero);
				list4.Add((i < normals.Length) ? normals[i] : Vector3.up);
				list5.Add((i < tangents.Length) ? tangents[i] : Vector4.zero);
				Color32 item = data.Colors[i];
				item.r = value.Tint.r;
				item.g = value.Tint.g;
				list6.Add(item);
			}
			int[] triangles = val.triangles;
			foreach (int num in triangles)
			{
				list7.Add(count + num);
			}
		}
		batch.Mesh.Clear();
		batch.Mesh.vertices = list.ToArray();
		batch.Mesh.uv = list2.ToArray();
		batch.Mesh.uv2 = list3.ToArray();
		batch.Mesh.normals = list4.ToArray();
		batch.Mesh.tangents = list5.ToArray();
		batch.Mesh.colors32 = list6.ToArray();
		batch.Mesh.triangles = list7.ToArray();
		batch.Mesh.RecalculateBounds();
	}
}
