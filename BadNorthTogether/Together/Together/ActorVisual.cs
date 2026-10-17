using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.Flag;
using Voxels.TowerDefense.SpriteMagic;

namespace Together;

internal sealed class ActorVisual : IDisposable
{
	private sealed class Node
	{
		public Transform Transform;

		public string[] Names;

		public ushort[] Ordinals;

		public byte Kind;

		public Renderer Renderer;

		public SpriteRenderer SpriteRenderer;

		public BatchedSprite BatchedSprite;

		public SpriteAnimator SpriteAnimator;

		public Billboard Billboard;

		public bool WorldAim;

		public Archery Bow;

		public Renderer[] DescendantRenderers;

		public Renderer[] GeneratedRenderers;

		public Array GeneratedSource;

		public Vector3 LastLossyScale;

		public Sprite LastSprite2;

		public Sprite PreparedSprite;
	}

	private sealed class SceneTree : IVisualTree<Transform>
	{
		private readonly ActorVisual owner;

		public SceneTree(ActorVisual owner)
		{
			this.owner = owner;
		}

		public string Name(Transform node)
		{
			return ((Object)node).name;
		}

		public int ChildCount(Transform node)
		{
			return node.childCount;
		}

		public Transform Child(Transform node, int index)
		{
			return node.GetChild(index);
		}

		public bool HasRenderer(Transform node)
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)((Component)node).GetComponent<SpriteRenderer>()))
			{
				return global::UnityObjectOps.Truthy((Object)(object)((Component)node).GetComponent<MeshRenderer>());
			}
			return true;
		}

		public bool IsLocalOnly(Transform node)
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)node.parent) || !global::UnityObjectOps.Truthy((Object)(object)((Component)node.parent).GetComponent<BatchedSprite>()) || !global::UnityObjectOps.Truthy((Object)(object)((Component)node).GetComponent<MeshRenderer>()) || !global::UnityObjectOps.Truthy((Object)(object)((Component)node).GetComponent<MeshFilter>()) || global::UnityObjectOps.Truthy((Object)(object)((Component)node).GetComponent<BatchedSprite>()))
			{
				return owner.IsTransientOrLocalUi(node);
			}
			return true;
		}
	}

	private const byte TransformOnly = 0;

	private const byte PlainSprite = 1;

	private const byte Mesh = 2;

	private const byte Batched = 3;

	private const byte Animated = 4;

	private static readonly FieldInfo bowAimerField = AccessTools.Field(typeof(Archery), "bowAimer");

	private static readonly MethodInfo billboardLateUpdate = AccessTools.Method(typeof(Billboard), "LateUpdate", (Type[])null, (Type[])null);

	private static int billboardFrame = -1;

	private static LevelCamera billboardCamera;

	private static readonly FieldInfo batchedRends = AccessTools.Field(typeof(BatchedSprite), "rends");

	private static readonly FieldInfo batchedRenderer = AccessTools.Field(typeof(BatchedSprite).GetNestedType("Rend", BindingFlags.NonPublic), "mr");

	private readonly Agent agent;

	private readonly Transform root;

	private Node[] nodes;

	private readonly Dictionary<Behaviour, bool> originalEnabled;

	private readonly Dictionary<Animator, AnimatorCullingMode> originalCulling;

	private readonly List<Renderer> unavailableRenderers;

	private bool playback;

	private bool disposed;

	public uint LayoutHash { get; private set; }

	public int NodeCount => nodes.Length;

	public ActorVisual(Agent agent)
	{
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		originalEnabled = new Dictionary<Behaviour, bool>();
		originalCulling = new Dictionary<Animator, AnimatorCullingMode>();
		unavailableRenderers = new List<Renderer>();
		if (!global::UnityObjectOps.Truthy((Object)(object)agent))
		{
			throw new ArgumentNullException("agent");
		}
		this.agent = agent;
		root = ((Component)agent).transform;
		nodes = DiscoverNodes();
		LayoutHash = ComputeHash(nodes);
		Animator[] componentsInChildren = ((Component)agent).GetComponentsInChildren<Animator>(true);
		foreach (Animator val in componentsInChildren)
		{
			originalCulling.Add(val, val.cullingMode);
			val.cullingMode = (AnimatorCullingMode)0;
		}
	}

	public void EnterPlayback()
	{
		if (disposed)
		{
			throw new ObjectDisposedException("ActorVisual");
		}
		if (!playback)
		{
			playback = true;
			SuspendLocalDrivers();
		}
	}

	private void SuspendLocalDrivers()
	{
		SpriteAnimator[] componentsInChildren = ((Component)agent).GetComponentsInChildren<SpriteAnimator>(true);
		foreach (SpriteAnimator val in componentsInChildren)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)val.sprite2))
			{
				val.UpdateSprite2();
			}
		}
		Behaviour[] componentsInChildren2 = ((Component)agent).GetComponentsInChildren<Behaviour>(true);
		foreach (Behaviour val2 in componentsInChildren2)
		{
			bool flag = val2 is Body || val2 is AgentSelected || val2 is Godray || val2 is FlagPhysics || val2 is FlagMesh;
			if ((val2 is AgentComponent && !flag) || val2 is Animator || val2 is SpriteAnimator || ClientDriverPolicy.IsDriver(((object)val2).GetType()))
			{
				if (!originalEnabled.ContainsKey(val2))
				{
					originalEnabled.Add(val2, val2.enabled);
				}
				MonoBehaviour val3 = (MonoBehaviour)(object)((val2 is MonoBehaviour) ? val2 : null);
				if (global::UnityObjectOps.Truthy((Object)(object)val3) && ClientDriverPolicy.IsDriver(((object)val3).GetType()))
				{
					val3.StopAllCoroutines();
				}
				val2.enabled = false;
			}
		}
	}

	public VisualFrame Capture()
	{
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_0165: Unknown result type (might be due to invalid IL or missing references)
		//IL_016a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0189: Unknown result type (might be due to invalid IL or missing references)
		//IL_0224: Unknown result type (might be due to invalid IL or missing references)
		//IL_0229: Unknown result type (might be due to invalid IL or missing references)
		//IL_022e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f1: Unknown result type (might be due to invalid IL or missing references)
		if (disposed || !global::UnityObjectOps.Truthy((Object)(object)agent))
		{
			throw new ObjectDisposedException("ActorVisual");
		}
		VisualFrame visualFrame = new VisualFrame
		{
			LayoutHash = LayoutHash,
			RootPosition = root.position,
			RootRotation = root.rotation,
			Nodes = new VisualNodeFrame[nodes.Length]
		};
		for (int i = 0; i < nodes.Length; i++)
		{
			Node node = nodes[i];
			if (!global::UnityObjectOps.Truthy((Object)(object)node.Transform))
			{
				throw new IOException("角色显示层级已改变，需重新发送布局");
			}
			bool flag = node.WorldAim && (!global::UnityObjectOps.Truthy((Object)(object)node.Bow) || node.Bow.aimInterpolator > 0f);
			VisualNodeFrame visualNodeFrame = new VisualNodeFrame
			{
				Position = ((i == 0) ? Vector3.zero : node.Transform.localPosition),
				Rotation = ((i == 0) ? Quaternion.identity : (flag ? node.Transform.rotation : node.Transform.localRotation)),
				Scale = node.Transform.localScale,
				Flags = (byte)((((Component)node.Transform).gameObject.activeSelf ? 1u : 0u) | (uint)(IsVisible(node) ? 2 : 0) | (uint)(flag ? 4 : 0)),
				Color = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue)
			};
			if (global::UnityObjectOps.Truthy((Object)(object)node.Billboard))
			{
				visualNodeFrame.Scale.x = Mathf.Abs(visualNodeFrame.Scale.x);
			}
			if (global::UnityObjectOps.Truthy((Object)(object)node.BatchedSprite))
			{
				Sprite sprite = (global::UnityObjectOps.Truthy((Object)(object)node.SpriteAnimator) ? node.SpriteAnimator.sprite : node.BatchedSprite.bSprite);
				visualNodeFrame.SpriteId = VisualResources.GetId(sprite);
				visualNodeFrame.Color = (Color32)(node.BatchedSprite.color);
			}
			else if (global::UnityObjectOps.Truthy((Object)(object)node.SpriteRenderer))
			{
				visualNodeFrame.SpriteId = VisualResources.GetId(node.SpriteRenderer.sprite);
				visualNodeFrame.Color = (Color32)(node.SpriteRenderer.color);
			}
			visualFrame.Nodes[i] = visualNodeFrame;
		}
		return visualFrame;
	}

	public void Apply(VisualFrame previous, VisualFrame next, float blend)
	{
		//IL_012f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		//IL_014c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0152: Unknown result type (might be due to invalid IL or missing references)
		//IL_0158: Unknown result type (might be due to invalid IL or missing references)
		//IL_0234: Unknown result type (might be due to invalid IL or missing references)
		//IL_023b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0241: Unknown result type (might be due to invalid IL or missing references)
		//IL_0246: Unknown result type (might be due to invalid IL or missing references)
		//IL_0295: Unknown result type (might be due to invalid IL or missing references)
		//IL_0258: Unknown result type (might be due to invalid IL or missing references)
		//IL_026b: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0648: Unknown result type (might be due to invalid IL or missing references)
		//IL_0632: Unknown result type (might be due to invalid IL or missing references)
		//IL_0639: Unknown result type (might be due to invalid IL or missing references)
		//IL_063f: Unknown result type (might be due to invalid IL or missing references)
		//IL_069e: Unknown result type (might be due to invalid IL or missing references)
		//IL_06a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_06a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_06a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_06c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_06c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_030b: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0302: Unknown result type (might be due to invalid IL or missing references)
		//IL_0537: Unknown result type (might be due to invalid IL or missing references)
		//IL_053c: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_0457: Unknown result type (might be due to invalid IL or missing references)
		//IL_045c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0461: Unknown result type (might be due to invalid IL or missing references)
		//IL_0467: Unknown result type (might be due to invalid IL or missing references)
		//IL_047a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0490: Unknown result type (might be due to invalid IL or missing references)
		//IL_0492: Unknown result type (might be due to invalid IL or missing references)
		if (!playback || disposed || !global::UnityObjectOps.Truthy((Object)(object)agent) || next == null)
		{
			return;
		}
		ValidateFrame(next);
		if (previous == null || previous.LayoutHash != next.LayoutHash || previous.Nodes.Length != next.Nodes.Length)
		{
			previous = next;
		}
		blend = Mathf.Clamp01(blend);
		VisualFrame visualFrame = ((blend >= 0.9999f) ? next : previous);
		for (int i = 0; i < nodes.Length; i++)
		{
			Node node = nodes[i];
			if (!global::UnityObjectOps.Truthy((Object)(object)node.BatchedSprite) && !global::UnityObjectOps.Truthy((Object)(object)node.SpriteRenderer))
			{
				continue;
			}
			node.PreparedSprite = (VisualResources.TryResolve(visualFrame.Nodes[i].SpriteId, out var sprite) ? sprite : null);
			if (global::UnityObjectOps.Truthy((Object)(object)node.BatchedSprite))
			{
				if (global::UnityObjectOps.Truthy((Object)(object)node.PreparedSprite))
				{
					node.BatchedSprite.bSprite = node.PreparedSprite;
				}
				else if (global::UnityObjectOps.Truthy((Object)(object)node.Transform) && ((Component)node.Transform).gameObject.activeSelf)
				{
					((Component)node.Transform).gameObject.SetActive(false);
				}
			}
		}
		root.position = Vector3.Lerp(previous.RootPosition, next.RootPosition, blend);
		root.rotation = Quaternion.Slerp(previous.RootRotation, next.RootRotation, blend);
		unavailableRenderers.Clear();
		for (int j = 0; j < nodes.Length; j++)
		{
			Node node2 = nodes[j];
			if (!global::UnityObjectOps.Truthy((Object)(object)node2.Transform))
			{
				continue;
			}
			VisualNodeFrame visualNodeFrame = previous.Nodes[j];
			VisualNodeFrame visualNodeFrame2 = next.Nodes[j];
			VisualNodeFrame visualNodeFrame3 = visualFrame.Nodes[j];
			if ((visualNodeFrame3.Flags & 4) != 0 && !node2.WorldAim)
			{
				throw new IOException("角色帧试图写入未声明的世界朝向节点");
			}
			bool flag = (visualNodeFrame3.Flags & 1) != 0;
			if (global::UnityObjectOps.Truthy((Object)(object)node2.BatchedSprite) && !global::UnityObjectOps.Truthy((Object)(object)node2.PreparedSprite))
			{
				flag = false;
			}
			if (((Component)node2.Transform).gameObject.activeSelf != flag)
			{
				((Component)node2.Transform).gameObject.SetActive(flag);
			}
			Vector3 val = Vector3.Lerp(visualNodeFrame.Scale, visualNodeFrame2.Scale, blend);
			if (global::UnityObjectOps.Truthy((Object)(object)node2.Billboard))
			{
				val.x = Mathf.Abs(val.x) * ((node2.Transform.localScale.x < 0f) ? (-1f) : 1f);
			}
			node2.Transform.localScale = val;
			if (j != 0)
			{
				node2.Transform.localPosition = Vector3.Lerp(visualNodeFrame.Position, visualNodeFrame2.Position, blend);
				if (!global::UnityObjectOps.Truthy((Object)(object)node2.Billboard) && (visualNodeFrame3.Flags & 4) == 0)
				{
					node2.Transform.localRotation = ((((visualNodeFrame.Flags ^ visualNodeFrame2.Flags) & 4) != 0) ? visualNodeFrame3.Rotation : Quaternion.Slerp(visualNodeFrame.Rotation, visualNodeFrame2.Rotation, blend));
				}
			}
			if (global::UnityObjectOps.Truthy((Object)(object)node2.Renderer) && !global::UnityObjectOps.Truthy((Object)(object)node2.BatchedSprite))
			{
				node2.Renderer.enabled = (visualNodeFrame3.Flags & 2) != 0;
			}
			if (!global::UnityObjectOps.Truthy((Object)(object)node2.BatchedSprite) && !global::UnityObjectOps.Truthy((Object)(object)node2.SpriteRenderer))
			{
				continue;
			}
			Sprite preparedSprite = node2.PreparedSprite;
			if (!global::UnityObjectOps.Truthy((Object)(object)preparedSprite))
			{
				unavailableRenderers.AddRange(node2.DescendantRenderers);
			}
			else if (global::UnityObjectOps.Truthy((Object)(object)node2.BatchedSprite))
			{
				node2.BatchedSprite.bSprite = preparedSprite;
				node2.BatchedSprite.Awake();
				if (RefreshGeneratedRenderers(node2))
				{
					node2.DescendantRenderers = ((Component)node2.Transform).GetComponentsInChildren<Renderer>(true);
				}
				if (global::UnityObjectOps.Truthy((Object)(object)node2.SpriteAnimator))
				{
					if (global::UnityObjectOps.Truthy((Object)(object)node2.SpriteAnimator.sprite2) && (Object)(object)node2.LastSprite2 != (Object)(object)node2.SpriteAnimator.sprite2)
					{
						node2.SpriteAnimator.UpdateSprite2();
						node2.LastSprite2 = node2.SpriteAnimator.sprite2;
					}
					node2.SpriteAnimator.sprite = preparedSprite;
					node2.SpriteAnimator.SetSprite();
					Color32 val2 = (Color32)(node2.BatchedSprite.color);
					val2.b = visualNodeFrame3.Color.b;
					val2.a = visualNodeFrame3.Color.a;
					node2.BatchedSprite.color = (Color32)(val2);
				}
				else
				{
					node2.BatchedSprite.bSprite = preparedSprite;
					node2.BatchedSprite.color = (Color32)(visualNodeFrame3.Color);
				}
				Renderer[] generatedRenderers = node2.GeneratedRenderers;
				foreach (Renderer val3 in generatedRenderers)
				{
					if (global::UnityObjectOps.Truthy((Object)(object)val3))
					{
						val3.enabled = (visualNodeFrame3.Flags & 2) != 0;
					}
				}
				if (global::UnityObjectOps.Truthy((Object)(object)node2.SpriteRenderer))
				{
					((Renderer)node2.SpriteRenderer).enabled = false;
				}
			}
			else
			{
				node2.SpriteRenderer.sprite = preparedSprite;
				node2.SpriteRenderer.color = (Color32)(visualNodeFrame3.Color);
			}
		}
		Node[] array;
		if (global::UnityObjectOps.Truthy((Object)(object)Singleton<LevelCamera>.instance))
		{
			UpdateBillboardRotations();
			array = nodes;
			foreach (Node node3 in array)
			{
				if (global::UnityObjectOps.Truthy((Object)(object)node3.Billboard) && ((Behaviour)node3.Billboard).isActiveAndEnabled)
				{
					billboardLateUpdate.Invoke(node3.Billboard, null);
				}
			}
		}
		for (int l = 0; l < nodes.Length; l++)
		{
			Node node4 = nodes[l];
			VisualNodeFrame visualNodeFrame4 = visualFrame.Nodes[l];
			if (global::UnityObjectOps.Truthy((Object)(object)node4.Transform) && (visualNodeFrame4.Flags & 4) != 0)
			{
				VisualNodeFrame visualNodeFrame5 = previous.Nodes[l];
				VisualNodeFrame visualNodeFrame6 = next.Nodes[l];
				node4.Transform.rotation = ((((visualNodeFrame5.Flags ^ visualNodeFrame6.Flags) & 4) != 0) ? visualNodeFrame4.Rotation : Quaternion.Slerp(visualNodeFrame5.Rotation, visualNodeFrame6.Rotation, blend));
			}
		}
		array = nodes;
		foreach (Node node5 in array)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)node5.BatchedSprite) && global::UnityObjectOps.Truthy((Object)(object)node5.Transform))
			{
				Vector3 lossyScale = node5.Transform.lossyScale;
				if (!(lossyScale == node5.LastLossyScale))
				{
					node5.BatchedSprite.UpdateScale();
					node5.LastLossyScale = lossyScale;
				}
			}
		}
		foreach (Renderer unavailableRenderer in unavailableRenderers)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)unavailableRenderer))
			{
				unavailableRenderer.enabled = false;
			}
		}
		foreach (KeyValuePair<Behaviour, bool> item in originalEnabled)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)item.Key) && item.Key.enabled)
			{
				item.Key.enabled = false;
			}
		}
	}

	private static void UpdateBillboardRotations()
	{
		LevelCamera instance = Singleton<LevelCamera>.instance;
		if (billboardFrame != Time.frameCount || !((Object)(object)billboardCamera == (Object)(object)instance))
		{
			Billboard.UpdateRotations();
			billboardFrame = Time.frameCount;
			billboardCamera = instance;
		}
	}

	private void ValidateFrame(VisualFrame frame)
	{
		if (frame.LayoutHash != LayoutHash || frame.Nodes == null || frame.Nodes.Length != nodes.Length)
		{
			throw new IOException("角色显示布局与采样帧不一致");
		}
	}

	public bool TryDescribeSurface(Transform target, out string[] names, out ushort[] ordinals)
	{
		Node[] array = nodes;
		foreach (Node node in array)
		{
			if ((Object)(object)node.Transform == (Object)(object)target)
			{
				names = node.Names;
				ordinals = node.Ordinals;
				return true;
			}
		}
		names = null;
		ordinals = null;
		return false;
	}

	public Transform ResolveSurface(string[] names, ushort[] ordinals)
	{
		string text = VisualHierarchy.Key(names, ordinals);
		Node[] array = nodes;
		foreach (Node node in array)
		{
			if (VisualHierarchy.Key(node.Names, node.Ordinals) == text)
			{
				return node.Transform;
			}
		}
		return null;
	}

	public void WriteLayout(BinaryWriter writer)
	{
		writer.Write(LayoutHash);
		writer.Write((byte)(nodes.Length - 1));
		Node[] array = nodes;
		foreach (Node node in array)
		{
			writer.Write(node.Kind);
			writer.Write((byte)((global::UnityObjectOps.Truthy((Object)(object)node.Billboard) ? 1u : 0u) | (uint)(node.WorldAim ? 2 : 0)));
			writer.Write((byte)node.Names.Length);
			for (int j = 0; j < node.Names.Length; j++)
			{
				VisualWire.WriteString(writer, node.Names[j], 256);
				writer.Write(node.Ordinals[j]);
			}
		}
	}

	public void ReadLayout(BinaryReader reader, uint expectedHash, int expectedCount)
	{
		uint num = reader.ReadUInt32();
		int num2 = reader.ReadByte() + 1;
		if (num2 > 128)
		{
			throw new IOException("角色布局节点过多");
		}
		if (num != expectedHash || num2 != expectedCount)
		{
			throw new IOException("出生布局与首帧描述不一致");
		}
		Node[] array = new Node[num2];
		HashSet<Transform> hashSet = new HashSet<Transform>();
		Dictionary<string, Node> dictionary = new Dictionary<string, Node>(StringComparer.Ordinal);
		Node[] array2 = DiscoverNodes();
		foreach (Node node in array2)
		{
			dictionary.Add(VisualHierarchy.Key(node.Names, node.Ordinals), node);
		}
		for (int j = 0; j < num2; j++)
		{
			byte b = reader.ReadByte();
			byte b2 = reader.ReadByte();
			int num3 = reader.ReadByte();
			if (b > 4 || b2 > 3 || num3 > 24 || j == 0 != (num3 == 0))
			{
				throw new IOException("角色布局描述非法");
			}
			string[] array3 = new string[num3];
			ushort[] array4 = new ushort[num3];
			for (int k = 0; k < num3; k++)
			{
				array3[k] = VisualWire.ReadString(reader, 256);
				array4[k] = reader.ReadUInt16();
				if (array4[k] > 1024)
				{
					throw new IOException("角色同名节点序号非法");
				}
			}
			Node node2 = VisualHierarchy.Resolve(dictionary, array3, array4);
			if (!hashSet.Add(node2.Transform))
			{
				throw new IOException("本地角色显示节点重复：" + VisualHierarchy.DisplayPath(array3, array4));
			}
			if (node2.Kind != b || (global::UnityObjectOps.Truthy((Object)(object)node2.Billboard) ? 1 : 0) != (b2 & 1) || node2.WorldAim != ((b2 & 2) != 0))
			{
				throw new IOException("双方角色显示组件不一致：" + VisualHierarchy.DisplayPath(array3, array4) + "，主机类型=" + b + "，本机类型=" + node2.Kind);
			}
			array[j] = node2;
		}
		if (ComputeHash(array) != num)
		{
			throw new IOException("角色布局校验不一致");
		}
		if (reader.BaseStream.Position != reader.BaseStream.Length)
		{
			throw new IOException("人物布局含多余数据");
		}
		nodes = array;
		LayoutHash = num;
		if (playback)
		{
			SuspendLocalDrivers();
		}
	}

	public bool IsLayoutCurrent()
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)agent) || disposed)
		{
			return false;
		}
		return ComputeHash(DiscoverNodes()) == LayoutHash;
	}

	private Node[] DiscoverNodes()
	{
		List<Node> list = new List<Node>();
		foreach (VisualTreeEntry<Transform> item in VisualHierarchy.Describe(root, new SceneTree(this)))
		{
			list.Add(Bind(item.Target, item.Names, item.Ordinals));
		}
		return list.ToArray();
	}

	private bool IsTransientOrLocalUi(Transform transform)
	{
		Transform val = transform;
		while (global::UnityObjectOps.Truthy((Object)(object)val) && (Object)(object)val != (Object)(object)root)
		{
			if (VisualScenePolicy.IsTransient(val))
			{
				return true;
			}
			val = val.parent;
		}
		return false;
	}

	private Node Bind(Transform transform, string[] names, ushort[] ordinals)
	{
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0136: Expected O, but got Unknown
		Node node = new Node
		{
			Transform = transform,
			Names = names,
			Ordinals = ordinals
		};
		node.BatchedSprite = ((Component)transform).GetComponent<BatchedSprite>();
		BatchedSprite batchedSprite = node.BatchedSprite;
		node.SpriteAnimator = (SpriteAnimator)(object)((batchedSprite is SpriteAnimator) ? batchedSprite : null);
		node.SpriteRenderer = ((Component)transform).GetComponent<SpriteRenderer>();
		node.Renderer = ((Component)transform).GetComponent<Renderer>();
		node.Billboard = ((Component)transform).GetComponent<Billboard>();
		node.LastLossyScale = transform.lossyScale;
		node.Kind = (byte)(global::UnityObjectOps.Truthy((Object)(object)node.SpriteAnimator) ? 4 : (global::UnityObjectOps.Truthy((Object)(object)node.BatchedSprite) ? 3 : (global::UnityObjectOps.Truthy((Object)(object)node.SpriteRenderer) ? 1 : (global::UnityObjectOps.Truthy((Object)(object)node.Renderer) ? 2 : 0))));
		node.DescendantRenderers = ((Component)transform).GetComponentsInChildren<Renderer>(true);
		node.GeneratedRenderers = (Renderer[])(object)new Renderer[0];
		if (global::UnityObjectOps.Truthy((Object)(object)node.BatchedSprite))
		{
			RefreshGeneratedRenderers(node);
		}
		Spear component = ((Component)agent).GetComponent<Spear>();
		if (global::UnityObjectOps.Truthy((Object)(object)component) && (Object)(object)component.spearAim == (Object)(object)transform)
		{
			node.WorldAim = true;
		}
		Archery component2 = ((Component)agent).GetComponent<Archery>();
		if (global::UnityObjectOps.Truthy((Object)(object)component2) && bowAimerField != null && (Object)(Transform)bowAimerField.GetValue(component2) == (Object)(object)transform)
		{
			node.WorldAim = true;
			node.Bow = component2;
		}
		return node;
	}

	private static bool RefreshGeneratedRenderers(Node node)
	{
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Expected O, but got Unknown
		Array array = (Array)batchedRends.GetValue(node.BatchedSprite);
		if (node.GeneratedSource == array)
		{
			return false;
		}
		node.GeneratedSource = array;
		node.GeneratedRenderers = (Renderer[])(object)new Renderer[array?.Length ?? 0];
		for (int i = 0; i < node.GeneratedRenderers.Length; i++)
		{
			node.GeneratedRenderers[i] = (Renderer)batchedRenderer.GetValue(array.GetValue(i));
		}
		return true;
	}

	private static bool IsVisible(Node node)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)node.BatchedSprite))
		{
			if (global::UnityObjectOps.Truthy((Object)(object)node.Renderer))
			{
				return node.Renderer.enabled;
			}
			return true;
		}
		RefreshGeneratedRenderers(node);
		Renderer[] generatedRenderers = node.GeneratedRenderers;
		foreach (Renderer val in generatedRenderers)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)val) && val.enabled)
			{
				return true;
			}
		}
		if (node.GeneratedRenderers.Length == 0 && global::UnityObjectOps.Truthy((Object)(object)node.SpriteRenderer))
		{
			return ((Renderer)node.SpriteRenderer).enabled;
		}
		return false;
	}

	private static uint ComputeHash(Node[] values)
	{
		uint num = 2166136261u;
		foreach (Node node in values)
		{
			num = Mix(num, node.Kind);
			num = Mix(num, (global::UnityObjectOps.Truthy((Object)(object)node.Billboard) ? 1u : 0u) | (uint)(node.WorldAim ? 2 : 0));
			num = Mix(num, (uint)node.Names.Length);
			for (int j = 0; j < node.Names.Length; j++)
			{
				num = Mix(num, (uint)node.Names[j].Length);
				string text = node.Names[j];
				foreach (char value in text)
				{
					num = Mix(num, value);
				}
				num = Mix(num, node.Ordinals[j]);
			}
		}
		return num;
	}

	private static uint Mix(uint hash, uint value)
	{
		return (hash ^ value) * 16777619;
	}

	public void Dispose()
	{
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		if (disposed)
		{
			return;
		}
		disposed = true;
		foreach (KeyValuePair<Behaviour, bool> item in originalEnabled)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)item.Key))
			{
				item.Key.enabled = item.Value;
			}
		}
		foreach (KeyValuePair<Animator, AnimatorCullingMode> item2 in originalCulling)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)item2.Key))
			{
				item2.Key.cullingMode = item2.Value;
			}
		}
		originalEnabled.Clear();
		originalCulling.Clear();
		unavailableRenderers.Clear();
	}
}
