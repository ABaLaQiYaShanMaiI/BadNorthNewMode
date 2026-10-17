using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.SpriteMagic;

namespace Together;

internal sealed class VisualEquipment : IDisposable
{
	private sealed class Hidden
	{
		public Transform Object;

		public Transform Parent;

		public int Sibling;

		public Vector3 Position;

		public Vector3 Scale;

		public Quaternion Rotation;

		public bool Active;
	}

	private sealed class Preparation
	{
		public VisualEquipmentDescription[] Values;

		public Transform[] OldProxies;

		public Hidden[] OldPlacements;

		public int HiddenCount;

		public int AnchorCount;

		public HashSet<Behaviour> DisabledBefore;

		public bool Replaced;
	}

	private const string VikingType = "Wei_Jing_Chang_Mao.VikingSpearPersonalShield";

	private const string VikingBrainType = "Wei_Jing_Chang_Mao.VikingSpearBrain";

	private const string AnchorName = "VSM_AnimatedEquipmentAnchor";

	private static readonly FieldInfo standardField = AccessTools.Field(typeof(SpearShield), "shieldStd");

	private static readonly FieldInfo eliteField = AccessTools.Field(typeof(SpearShield), "shieldElite");

	private static readonly FieldInfo tangentField = AccessTools.Field(typeof(BatchedSprite), "tangentW");

	private readonly Agent agent;

	private readonly List<Hidden> hidden = new List<Hidden>();

	private readonly List<Transform> anchors = new List<Transform>();

	private readonly Dictionary<Behaviour, bool> disabled = new Dictionary<Behaviour, bool>();

	private readonly Transform[] proxies = (Transform[])(object)new Transform[2];

	private VisualEquipmentDescription[] published;

	private VisualEquipmentDescription[] applied;

	private Preparation preparation;

	private GameObject storage;

	private bool disposed;

	public VisualEquipment(Agent agent)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)agent))
		{
			throw new ArgumentNullException("agent");
		}
		this.agent = agent;
	}

	public void Write(BinaryWriter writer)
	{
		EnsureAlive();
		VisualEquipmentDescription[] values = Capture();
		VisualEquipmentCodec.Write(writer, values);
		published = values;
	}

	public bool IsCurrent()
	{
		if (disposed || !global::UnityObjectOps.Truthy((Object)(object)agent) || published == null)
		{
			return false;
		}
		VisualEquipmentDescription[] array = Capture();
		if (VisualEquipmentCodec.Same(published[0], array[0], compareIdentity: true))
		{
			return VisualEquipmentCodec.Same(published[1], array[1], compareIdentity: true);
		}
		return false;
	}

	public void Read(BinaryReader reader)
	{
		EnsureAlive();
		Rollback();
		VisualEquipmentDescription[] array = VisualEquipmentCodec.Read(reader);
		Transform[] array2 = (Transform[])(object)new Transform[2];
		for (int i = 0; i < 2; i++)
		{
			VisualEquipmentDescription visualEquipmentDescription = array[i];
			if (visualEquipmentDescription.Kind != 0)
			{
				if (i == 0 && !global::UnityObjectOps.Truthy((Object)(object)((Component)agent).GetComponent<Spear>()))
				{
					throw new IOException("非英格兰长矛人物收到矛盾描述");
				}
				if (i == 1 && !global::UnityObjectOps.Truthy((Object)(object)FindVikingShield()))
				{
					throw new IOException("非维京长矛人物收到随身盾描述");
				}
				array2[i] = ResolveSource(i, visualEquipmentDescription.Kind);
				if (!global::UnityObjectOps.Truthy((Object)(object)array2[i]))
				{
					throw new IOException("本机缺少盾牌外观来源：槽 " + i + "，种类 " + visualEquipmentDescription.Kind);
				}
				if (i == 0 && BaseName(visualEquipmentDescription.Name) != BaseName(((Object)array2[i]).name))
				{
					throw new IOException("矛盾名称与本机已知外观来源不同：" + visualEquipmentDescription.Name);
				}
			}
		}
		bool flag = applied != null && VisualEquipmentCodec.Same(applied[0], array[0], compareIdentity: false) && VisualEquipmentCodec.Same(applied[1], array[1], compareIdentity: false);
		flag &= (array[0].Kind == 0 || global::UnityObjectOps.Truthy((Object)(object)proxies[0])) && (array[1].Kind == 0 || global::UnityObjectOps.Truthy((Object)(object)proxies[1]));
		Transform[] array3 = (Transform[])(object)new Transform[2];
		try
		{
			if (!flag)
			{
				for (int j = 0; j < 2; j++)
				{
					if (array[j].Kind != 0)
					{
						EnsureStorage();
						array3[j] = CopyVisual(array2[j], array[j].Name);
					}
				}
			}
			preparation = new Preparation
			{
				Values = array,
				OldProxies = (Transform[])proxies.Clone(),
				OldPlacements = new Hidden[2],
				HiddenCount = hidden.Count,
				AnchorCount = anchors.Count,
				DisabledBefore = new HashSet<Behaviour>(disabled.Keys),
				Replaced = !flag
			};
			HideLocalEquipment();
			if (flag)
			{
				return;
			}
			for (int k = 0; k < 2; k++)
			{
				if (global::UnityObjectOps.Truthy((Object)(object)proxies[k]))
				{
					preparation.OldPlacements[k] = Remember(proxies[k]);
					((Component)proxies[k]).gameObject.SetActive(false);
					proxies[k].SetParent(storage.transform, false);
				}
				proxies[k] = null;
				if (global::UnityObjectOps.Truthy((Object)(object)array3[k]))
				{
					Transform val = ResolveParent(array[k], k);
					proxies[k] = array3[k];
					array3[k] = null;
					proxies[k].SetParent(val, false);
					SetOrdinal(proxies[k], array[k].Ordinal);
				}
			}
		}
		catch
		{
			Transform[] array4 = array3;
			foreach (Transform target in array4)
			{
				Retire(target);
			}
			Rollback();
			throw;
		}
	}

	public void Commit()
	{
		if (preparation == null)
		{
			return;
		}
		if (preparation.Replaced)
		{
			Transform[] oldProxies = preparation.OldProxies;
			foreach (Transform target in oldProxies)
			{
				Retire(target);
			}
		}
		applied = preparation.Values;
		preparation = null;
		RefreshSelection();
	}

	public void Rollback()
	{
		Preparation preparation = this.preparation;
		if (preparation == null)
		{
			return;
		}
		this.preparation = null;
		if (preparation.Replaced)
		{
			for (int i = 0; i < 2; i++)
			{
				if ((Object)(object)proxies[i] != (Object)(object)preparation.OldProxies[i])
				{
					Retire(proxies[i]);
				}
				proxies[i] = preparation.OldProxies[i];
				if (preparation.OldPlacements[i] != null)
				{
					Restore(preparation.OldPlacements[i]);
				}
			}
		}
		for (int num = hidden.Count - 1; num >= preparation.HiddenCount; num--)
		{
			Restore(hidden[num]);
			hidden.RemoveAt(num);
		}
		foreach (Behaviour item in new List<Behaviour>(disabled.Keys))
		{
			if (!preparation.DisabledBefore.Contains(item))
			{
				if (global::UnityObjectOps.Truthy((Object)(object)item))
				{
					item.enabled = disabled[item];
				}
				disabled.Remove(item);
			}
		}
		for (int num2 = anchors.Count - 1; num2 >= preparation.AnchorCount; num2--)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)anchors[num2]) && anchors[num2].childCount == 0)
			{
				Retire(anchors[num2]);
			}
			anchors.RemoveAt(num2);
		}
		RefreshSelection();
	}

	private void RefreshSelection()
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)agent))
		{
			return;
		}
		AgentSelected component = ((Component)agent).GetComponent<AgentSelected>();
		if (!global::UnityObjectOps.Truthy((Object)(object)component))
		{
			return;
		}
		try
		{
			component.RefreshRenderers();
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("[表现装备] 刷新选择高亮失败：" + ex.Message));
		}
	}

	private VisualEquipmentDescription[] Capture()
	{
		VisualEquipmentDescription[] array = new VisualEquipmentDescription[2]
		{
			new VisualEquipmentDescription(),
			new VisualEquipmentDescription()
		};
		SpearShield component = ((Component)agent).GetComponent<SpearShield>();
		if (global::UnityObjectOps.Truthy((Object)(object)component) && global::UnityObjectOps.Truthy((Object)(object)component.shieldTransform))
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)agent.squad))
			{
				throw new IOException("矛盾人物缺少小队");
			}
			array[0] = Describe(component.shieldTransform, (byte)((agent.squad.level < 3) ? 1u : 2u), 0);
		}
		Behaviour val = FindVikingShield();
		if (global::UnityObjectOps.Truthy((Object)(object)val))
		{
			Transform vikingTransform = GetVikingTransform(val);
			if (global::UnityObjectOps.Truthy((Object)(object)vikingTransform))
			{
				int num = Convert.ToInt32(RequiredField(((object)val).GetType(), "_shieldType").GetValue(val));
				if (num < 1 || num > 2)
				{
					throw new IOException("维京随身盾种类与外观不一致");
				}
				array[1] = Describe(vikingTransform, (byte)num, 1);
			}
		}
		return array;
	}

	private VisualEquipmentDescription Describe(Transform shield, byte kind, int slot)
	{
		List<string> list = new List<string>();
		List<ushort> list2 = new List<ushort>();
		Transform parent = shield.parent;
		while (global::UnityObjectOps.Truthy((Object)(object)parent) && (Object)(object)parent != (Object)(object)((Component)agent).transform)
		{
			if (list.Count >= 3)
			{
				throw new IOException("盾牌不在已知装备锚点下");
			}
			list.Add(((Object)parent).name);
			list2.Add(GetOrdinal(parent));
			parent = parent.parent;
		}
		if ((Object)(object)parent != (Object)(object)((Component)agent).transform)
		{
			throw new IOException("盾牌已离开人物显示树");
		}
		list.Reverse();
		list2.Reverse();
		VisualEquipmentDescription obj = new VisualEquipmentDescription
		{
			Kind = kind,
			Original = shield,
			Name = ((Object)shield).name,
			Parents = list.ToArray(),
			Ordinals = list2.ToArray(),
			Ordinal = GetOrdinal(shield)
		};
		VisualEquipmentCodec.Validate(obj, slot);
		return obj;
	}

	private Transform ResolveSource(int slot, byte kind)
	{
		//IL_01ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fb: Invalid comparison between Unknown and I4
		//IL_0112: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Invalid comparison between Unknown and I4
		//IL_0124: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Invalid comparison between Unknown and I4
		if (slot == 0)
		{
			FieldInfo fieldInfo = ((kind == 1) ? standardField : eliteField);
			object obj;
			if (fieldInfo == null)
			{
				obj = null;
			}
			else
			{
				object value = fieldInfo.GetValue(null);
				obj = ((value is Shield) ? value : null);
			}
			Shield val = (Shield)obj;
			if (global::UnityObjectOps.Truthy((Object)(object)val) && global::UnityObjectOps.Truthy((Object)(object)val.shield))
			{
				return val.shield;
			}
			Transform val2 = null;
			Transform val3 = null;
			Transform val4 = null;
			foreach (KeyValuePair<string, Object> item in LevelStateObjectReferences.dict)
			{
				Object value2 = item.Value;
				EnglishSquad val5 = (EnglishSquad)(object)((value2 is EnglishSquad) ? value2 : null);
				if (!global::UnityObjectOps.Truthy((Object)(object)val4) && global::UnityObjectOps.Truthy((Object)(object)val5) && global::UnityObjectOps.Truthy((Object)(object)val5.minionPrefab))
				{
					Shield componentInChildren = ((Component)val5.minionPrefab).GetComponentInChildren<Shield>(true);
					if (global::UnityObjectOps.Truthy((Object)(object)componentInChildren) && global::UnityObjectOps.Truthy((Object)(object)componentInChildren.shield))
					{
						val4 = componentInChildren.shield;
					}
				}
				Object value3 = item.Value;
				VikingReference val6 = (VikingReference)(object)((value3 is VikingReference) ? value3 : null);
				if (!global::UnityObjectOps.Truthy((Object)(object)val6) || !global::UnityObjectOps.Truthy((Object)(object)val6.agent))
				{
					continue;
				}
				Shield componentInChildren2 = ((Component)val6.agent).GetComponentInChildren<Shield>(true);
				if (global::UnityObjectOps.Truthy((Object)(object)componentInChildren2) && global::UnityObjectOps.Truthy((Object)(object)componentInChildren2.shield))
				{
					if ((int)val6.type == 1)
					{
						val2 = componentInChildren2.shield;
					}
					if ((int)val6.type == 4)
					{
						val3 = componentInChildren2.shield;
					}
				}
			}
			if (!global::UnityObjectOps.Truthy((Object)(object)val2))
			{
				val2 = val4;
			}
			if (kind != 1)
			{
				if (!global::UnityObjectOps.Truthy((Object)(object)val3))
				{
					return val2;
				}
				return val3;
			}
			return val2;
		}
		Behaviour val7 = FindVikingShield();
		if (global::UnityObjectOps.Truthy((Object)(object)val7))
		{
			FieldInfo fieldInfo2 = AccessTools.Field(((object)val7).GetType(), (kind == 1) ? "_yellowShieldSource" : "_redShieldSource");
			object obj2;
			if (fieldInfo2 == null)
			{
				obj2 = null;
			}
			else
			{
				object value4 = fieldInfo2.GetValue(null);
				obj2 = ((value4 is Transform) ? value4 : null);
			}
			Transform val8 = (Transform)obj2;
			if (global::UnityObjectOps.Truthy((Object)(object)val8))
			{
				return val8;
			}
		}
		foreach (VikingReference referencedObject in LevelStateObjectReferences.GetReferencedObjects<VikingReference>())
		{
			if (global::UnityObjectOps.Truthy((Object)(object)referencedObject) && global::UnityObjectOps.Truthy((Object)(object)referencedObject.vikingClone) && (int)referencedObject.type == ((kind == 1) ? 1 : 4))
			{
				Shield componentInChildren3 = ((Component)referencedObject.vikingClone).GetComponentInChildren<Shield>(true);
				if (global::UnityObjectOps.Truthy((Object)(object)componentInChildren3) && global::UnityObjectOps.Truthy((Object)(object)componentInChildren3.shield))
				{
					return componentInChildren3.shield;
				}
			}
		}
		return null;
	}

	private Transform ResolveParent(VisualEquipmentDescription value, int slot)
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		Transform val = ((Component)agent).transform;
		for (int i = 0; i < value.Parents.Length; i++)
		{
			Transform val2 = FindChild(val, value.Parents[i]);
			if (!global::UnityObjectOps.Truthy((Object)(object)val2))
			{
				if (slot != 1)
				{
					throw new IOException("矛盾父节点不存在");
				}
				val2 = new GameObject(value.Parents[i]).transform;
				if (i == 2)
				{
					val2.SetParent(((Component)agent).transform, false);
					val2.SetParent(val, true);
				}
				else
				{
					val2.SetParent(val, false);
					if (i == 0)
					{
						val2.localPosition = new Vector3(0f, 0.2f, 0f);
					}
				}
				anchors.Add(val2);
			}
			val = val2;
		}
		return val;
	}

	private void HideLocalEquipment()
	{
		SpearShield component = ((Component)agent).GetComponent<SpearShield>();
		if (global::UnityObjectOps.Truthy((Object)(object)component))
		{
			Hide(component.shieldTransform);
			Disable((Behaviour)(object)component);
		}
		Behaviour val = FindVikingShield();
		if (global::UnityObjectOps.Truthy((Object)(object)val))
		{
			Hide(GetVikingTransform(val));
			Disable(val);
		}
		Behaviour[] components = ((Component)agent).GetComponents<Behaviour>();
		foreach (Behaviour val2 in components)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)val2) && ((object)val2).GetType().FullName == "Wei_Jing_Chang_Mao.VikingSpearBrain")
			{
				Disable(val2);
			}
		}
	}

	private void Hide(Transform target)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)target) || (Object)(object)target == (Object)(object)proxies[0] || (Object)(object)target == (Object)(object)proxies[1])
		{
			return;
		}
		foreach (Hidden item in hidden)
		{
			if ((Object)(object)item.Object == (Object)(object)target)
			{
				return;
			}
		}
		EnsureStorage();
		hidden.Add(Remember(target));
		target.SetParent(storage.transform, false);
		((Component)target).gameObject.SetActive(false);
	}

	private static Hidden Remember(Transform target)
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		return new Hidden
		{
			Object = target,
			Parent = target.parent,
			Sibling = target.GetSiblingIndex(),
			Position = target.localPosition,
			Rotation = target.localRotation,
			Scale = target.localScale,
			Active = ((Component)target).gameObject.activeSelf
		};
	}

	private void Restore(Hidden value)
	{
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		if (global::UnityObjectOps.Truthy((Object)(object)value.Object))
		{
			Transform val = (global::UnityObjectOps.Truthy((Object)(object)value.Parent) ? value.Parent : (global::UnityObjectOps.Truthy((Object)(object)agent) ? ((Component)agent).transform : null));
			value.Object.SetParent(val, false);
			if (global::UnityObjectOps.Truthy((Object)(object)val))
			{
				value.Object.SetSiblingIndex(value.Sibling);
			}
			value.Object.localPosition = value.Position;
			value.Object.localRotation = value.Rotation;
			value.Object.localScale = value.Scale;
			((Component)value.Object).gameObject.SetActive(global::UnityObjectOps.Truthy((Object)(object)val) && value.Active);
		}
	}

	private void Disable(Behaviour behaviour)
	{
		if (!disabled.ContainsKey(behaviour))
		{
			disabled.Add(behaviour, behaviour.enabled);
		}
		behaviour.enabled = false;
	}

	private Transform CopyVisual(Transform source, string name)
	{
		Dictionary<Transform, Transform> dictionary = new Dictionary<Transform, Transform>();
		Transform value = null;
		try
		{
			int count = 0;
			value = CopyNode(source, storage.transform, dictionary, ref count);
			((Object)value).name = name;
			((Component)value).gameObject.SetActive(false);
			foreach (KeyValuePair<Transform, Transform> item in dictionary)
			{
				SpriteAnimator component = ((Component)item.Key).GetComponent<SpriteAnimator>();
				SpriteAnimator component2 = ((Component)item.Value).GetComponent<SpriteAnimator>();
				if (global::UnityObjectOps.Truthy((Object)(object)component) && global::UnityObjectOps.Truthy((Object)(object)component2))
				{
					if (!global::UnityObjectOps.Truthy((Object)(object)component.aimer) || !dictionary.TryGetValue(component.aimer, out var value2))
					{
						throw new IOException("盾牌内动画精灵引用了外部节点");
					}
					component2.aimer = value2;
				}
			}
			return value;
		}
		catch
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)value))
			{
				dictionary.TryGetValue(source, out value);
			}
			Retire(value);
			throw;
		}
	}

	private Transform CopyNode(Transform source, Transform parent, Dictionary<Transform, Transform> copies, ref int count)
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Expected O, but got Unknown
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		if (++count > 128)
		{
			throw new IOException("盾牌外观节点过多");
		}
		GameObject val = new GameObject(((Object)source).name);
		val.SetActive(false);
		Transform transform = val.transform;
		transform.SetParent(parent, false);
		copies.Add(source, transform);
		transform.localPosition = source.localPosition;
		transform.localRotation = source.localRotation;
		transform.localScale = source.localScale;
		val.layer = ((Component)source).gameObject.layer;
		SpriteRenderer component = ((Component)source).GetComponent<SpriteRenderer>();
		BatchedSprite component2 = ((Component)source).GetComponent<BatchedSprite>();
		SpriteAnimator val2 = (SpriteAnimator)(object)((component2 is SpriteAnimator) ? component2 : null);
		Sprite val3 = (global::UnityObjectOps.Truthy((Object)(object)component) ? component.sprite : null);
		if (!global::UnityObjectOps.Truthy((Object)(object)val3) && global::UnityObjectOps.Truthy((Object)(object)val2))
		{
			val3 = val2.sprite;
		}
		if (global::UnityObjectOps.Truthy((Object)(object)component2) && (!global::UnityObjectOps.Truthy((Object)(object)component) || !global::UnityObjectOps.Truthy((Object)(object)val3)))
		{
			throw new IOException("盾牌批量精灵尚未准备好有效图片");
		}
		if (global::UnityObjectOps.Truthy((Object)(object)component))
		{
			SpriteRenderer val4 = val.AddComponent<SpriteRenderer>();
			val4.sprite = val3;
			val4.color = component.color;
			val4.flipX = component.flipX;
			val4.flipY = component.flipY;
			CopyRenderer((Renderer)(object)component, (Renderer)(object)val4);
		}
		MeshFilter component3 = ((Component)source).GetComponent<MeshFilter>();
		if (global::UnityObjectOps.Truthy((Object)(object)component3))
		{
			val.AddComponent<MeshFilter>().sharedMesh = component3.sharedMesh;
		}
		MeshRenderer component4 = ((Component)source).GetComponent<MeshRenderer>();
		if (global::UnityObjectOps.Truthy((Object)(object)component4))
		{
			CopyRenderer((Renderer)(object)component4, (Renderer)(object)val.AddComponent<MeshRenderer>());
		}
		if (global::UnityObjectOps.Truthy((Object)(object)component2))
		{
			BatchedSprite obj2;
			if (global::UnityObjectOps.Truthy((Object)(object)val2))
			{
				SpriteAnimator obj = val.AddComponent<SpriteAnimator>();
				obj.sprite = (global::UnityObjectOps.Truthy((Object)(object)val2.sprite) ? val2.sprite : val3);
				obj.sprite2 = val2.sprite2;
				((Behaviour)obj).enabled = false;
				obj2 = (BatchedSprite)(object)obj;
			}
			else
			{
				if (((object)component2).GetType() != typeof(BatchedSprite))
				{
					throw new IOException("盾牌使用了尚未支持的精灵类型：" + ((object)component2).GetType().FullName);
				}
				obj2 = val.AddComponent<BatchedSprite>();
			}
			if (tangentField != null)
			{
				tangentField.SetValue(obj2, tangentField.GetValue(component2));
			}
		}
		Billboard component5 = ((Component)source).GetComponent<Billboard>();
		if (global::UnityObjectOps.Truthy((Object)(object)component5))
		{
			Billboard obj3 = val.AddComponent<Billboard>();
			obj3.flippable = component5.flippable;
			obj3.backward = component5.backward;
			obj3.scale = component5.scale;
			obj3.flipped = component5.flipped;
			((Behaviour)obj3).enabled = ((Behaviour)component5).enabled;
		}
		for (int i = 0; i < source.childCount; i++)
		{
			Transform child = source.GetChild(i);
			if (!IsTransient(child) && !IsBatchedOutput(child, component2))
			{
				CopyNode(child, transform, copies, ref count);
			}
		}
		val.SetActive(((Component)source).gameObject.activeSelf);
		return transform;
	}

	private static bool IsBatchedOutput(Transform child, BatchedSprite parent)
	{
		if (global::UnityObjectOps.Truthy((Object)(object)parent) && global::UnityObjectOps.Truthy((Object)(object)((Component)child).GetComponent<MeshRenderer>()) && global::UnityObjectOps.Truthy((Object)(object)((Component)child).GetComponent<MeshFilter>()))
		{
			return !global::UnityObjectOps.Truthy((Object)(object)((Component)child).GetComponent<BatchedSprite>());
		}
		return false;
	}

	private static void CopyRenderer(Renderer source, Renderer target)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Expected O, but got Unknown
		target.sharedMaterials = source.sharedMaterials;
		target.enabled = source.enabled;
		target.sortingLayerID = source.sortingLayerID;
		target.sortingOrder = source.sortingOrder;
		target.shadowCastingMode = source.shadowCastingMode;
		target.receiveShadows = source.receiveShadows;
		MaterialPropertyBlock val = new MaterialPropertyBlock();
		source.GetPropertyBlock(val);
		target.SetPropertyBlock(val);
	}

	private void SetOrdinal(Transform target, ushort ordinal)
	{
		Transform parent = target.parent;
		int num = 0;
		for (int i = 0; i < parent.childCount; i++)
		{
			Transform child = parent.GetChild(i);
			if (!((Object)(object)child == (Object)(object)target) && !(((Object)child).name != ((Object)target).name) && HasPermanentVisual(child) && num++ == ordinal)
			{
				target.SetSiblingIndex(i);
				return;
			}
		}
		if (num != ordinal)
		{
			throw new IOException("盾牌同名序号无法匹配：" + ((Object)target).name + "，主机=" + ordinal + "，本地=" + num);
		}
		target.SetAsLastSibling();
	}

	private static ushort GetOrdinal(Transform target)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)target.parent))
		{
			return 0;
		}
		int num = 0;
		for (int i = 0; i < target.parent.childCount; i++)
		{
			Transform child = target.parent.GetChild(i);
			if ((Object)(object)child == (Object)(object)target)
			{
				return (ushort)num;
			}
			if (((Object)child).name == ((Object)target).name && HasPermanentVisual(child))
			{
				num++;
			}
		}
		throw new IOException("盾牌父节点不含该外观");
	}

	private static bool HasPermanentVisual(Transform branch)
	{
		Renderer[] componentsInChildren = ((Component)branch).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if (!(val is SpriteRenderer) && !(val is MeshRenderer))
			{
				continue;
			}
			bool flag = false;
			Transform val2 = ((Component)val).transform;
			while (global::UnityObjectOps.Truthy((Object)(object)val2) && (Object)(object)val2 != (Object)(object)branch.parent)
			{
				if (IsTransient(val2) || IsBatchedOutput(val2, global::UnityObjectOps.Truthy((Object)(object)val2.parent) ? ((Component)val2.parent).GetComponent<BatchedSprite>() : null))
				{
					flag = true;
					break;
				}
				val2 = val2.parent;
			}
			if (!flag)
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsTransient(Transform value)
	{
		return VisualScenePolicy.IsTransient(value);
	}

	private static Transform FindChild(Transform parent, string name)
	{
		for (int i = 0; i < parent.childCount; i++)
		{
			if (((Object)parent.GetChild(i)).name == name)
			{
				return parent.GetChild(i);
			}
		}
		return null;
	}

	private Behaviour FindVikingShield()
	{
		Behaviour[] components = ((Component)agent).GetComponents<Behaviour>();
		foreach (Behaviour val in components)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)val) && ((object)val).GetType().FullName == "Wei_Jing_Chang_Mao.VikingSpearPersonalShield")
			{
				return val;
			}
		}
		return null;
	}

	private static Transform GetVikingTransform(Behaviour component)
	{
		object value = RequiredField(((object)component).GetType(), "_shieldTransform").GetValue(component);
		return (Transform)((value is Transform) ? value : null);
	}

	private static FieldInfo RequiredField(Type type, string name)
	{
		return AccessTools.Field(type, name) ?? throw new IOException("装备兼容字段不存在：" + type.FullName + "." + name);
	}

	private static string BaseName(string name)
	{
		while (name.EndsWith("(Clone)", StringComparison.Ordinal))
		{
			name = name.Substring(0, name.Length - "(Clone)".Length);
		}
		return name;
	}

	private void EnsureStorage()
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected O, but got Unknown
		if (!global::UnityObjectOps.Truthy((Object)(object)storage))
		{
			storage = new GameObject("Together_EquipmentStorage");
			storage.SetActive(false);
			storage.transform.SetParent(((Component)agent).transform.parent, false);
		}
	}

	private void Retire(Transform target)
	{
		if (global::UnityObjectOps.Truthy((Object)(object)target))
		{
			((Component)target).gameObject.SetActive(false);
			if (global::UnityObjectOps.Truthy((Object)(object)storage))
			{
				target.SetParent(storage.transform, false);
			}
			Object.Destroy((Object)(object)((Component)target).gameObject);
		}
	}

	private void EnsureAlive()
	{
		if (disposed || !global::UnityObjectOps.Truthy((Object)(object)agent))
		{
			throw new ObjectDisposedException("VisualEquipment");
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}
		Rollback();
		disposed = true;
		Transform[] array = proxies;
		foreach (Transform target in array)
		{
			Retire(target);
		}
		foreach (Hidden item in hidden)
		{
			Restore(item);
		}
		foreach (KeyValuePair<Behaviour, bool> item2 in disabled)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)item2.Key))
			{
				item2.Key.enabled = item2.Value;
			}
		}
		for (int num = anchors.Count - 1; num >= 0; num--)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)anchors[num]) && anchors[num].childCount == 0)
			{
				Retire(anchors[num]);
			}
		}
		hidden.Clear();
		disabled.Clear();
		anchors.Clear();
		if (global::UnityObjectOps.Truthy((Object)(object)storage))
		{
			Object.Destroy((Object)(object)storage);
		}
		storage = null;
	}
}
