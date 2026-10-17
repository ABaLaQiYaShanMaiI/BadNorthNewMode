using System.IO;
using System.Text;

namespace Together;

internal static class VisualEquipmentCodec
{
	internal const string AnchorName = "VSM_AnimatedEquipmentAnchor";

	public static void Write(BinaryWriter writer, VisualEquipmentDescription[] values)
	{
		if (values == null || values.Length != 2)
		{
			throw new IOException("装备描述必须包含两个固定槽");
		}
		for (int i = 0; i < 2; i++)
		{
			Validate(values[i], i);
		}
		writer.Write((byte)1);
		foreach (VisualEquipmentDescription visualEquipmentDescription in values)
		{
			writer.Write(visualEquipmentDescription.Kind);
			if (visualEquipmentDescription.Kind != 0)
			{
				writer.Write((byte)visualEquipmentDescription.Parents.Length);
				for (int k = 0; k < visualEquipmentDescription.Parents.Length; k++)
				{
					VisualWire.WriteString(writer, visualEquipmentDescription.Parents[k], 256);
					writer.Write(visualEquipmentDescription.Ordinals[k]);
				}
				VisualWire.WriteString(writer, visualEquipmentDescription.Name, 256);
				writer.Write(visualEquipmentDescription.Ordinal);
			}
		}
	}

	public static VisualEquipmentDescription[] Read(BinaryReader reader)
	{
		try
		{
			if (reader.ReadByte() != 1)
			{
				throw new IOException("不支持的装备描述格式");
			}
			VisualEquipmentDescription[] array = new VisualEquipmentDescription[2];
			for (int i = 0; i < 2; i++)
			{
				VisualEquipmentDescription visualEquipmentDescription = new VisualEquipmentDescription
				{
					Kind = reader.ReadByte()
				};
				if (visualEquipmentDescription.Kind > 2)
				{
					throw new IOException("非法盾牌种类");
				}
				if (visualEquipmentDescription.Kind != 0)
				{
					int num = reader.ReadByte();
					if (num > 3)
					{
						throw new IOException("盾牌父层级过深");
					}
					visualEquipmentDescription.Parents = new string[num];
					visualEquipmentDescription.Ordinals = new ushort[num];
					for (int j = 0; j < num; j++)
					{
						visualEquipmentDescription.Parents[j] = VisualWire.ReadString(reader, 256);
						visualEquipmentDescription.Ordinals[j] = reader.ReadUInt16();
					}
					visualEquipmentDescription.Name = VisualWire.ReadString(reader, 256);
					visualEquipmentDescription.Ordinal = reader.ReadUInt16();
				}
				Validate(visualEquipmentDescription, i);
				array[i] = visualEquipmentDescription;
			}
			return array;
		}
		catch (DecoderFallbackException innerException)
		{
			throw new IOException("盾牌名称不是有效的 UTF-8 编码", innerException);
		}
	}

	public static void Validate(VisualEquipmentDescription value, int slot)
	{
		if (slot < 0 || slot > 1 || value == null || value.Kind > 2 || value.Parents == null || value.Ordinals == null || value.Parents.Length != value.Ordinals.Length || value.Parents.Length > 3)
		{
			throw new IOException("非法装备描述");
		}
		if (value.Kind == 0)
		{
			if (!string.IsNullOrEmpty(value.Name) || value.Parents.Length != 0 || value.Ordinal != 0)
			{
				throw new IOException("无盾槽不应包含节点路径");
			}
			return;
		}
		if (string.IsNullOrEmpty(value.Name) || value.Name.IndexOfAny(new char[3] { '/', '\\', '\0' }) >= 0 || value.Ordinal > 8)
		{
			throw new IOException("非法盾牌节点名或同名序号");
		}
		try
		{
			if (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetByteCount(value.Name) > 256)
			{
				throw new IOException("盾牌名称过长");
			}
		}
		catch (EncoderFallbackException innerException)
		{
			throw new IOException("盾牌名称不能编码为 UTF-8", innerException);
		}
		if (slot == 0)
		{
			if (value.Parents.Length != 0)
			{
				throw new IOException("英格兰矛盾必须位于人物根节点");
			}
		}
		else
		{
			string text = ((value.Kind == 1) ? "VSM_YellowShield" : "VSM_RedShield");
			if (value.Name != text)
			{
				throw new IOException("维京盾牌名称与种类不一致");
			}
			if (value.Parents.Length != 0 && (value.Parents.Length != 3 || value.Parents[0] != "BounceAnim" || value.Parents[1] != "ShieldAimer" || value.Parents[2] != "VSM_AnimatedEquipmentAnchor"))
			{
				throw new IOException("维京盾牌必须位于已知身体动画锚点");
			}
		}
		ushort[] ordinals = value.Ordinals;
		for (int i = 0; i < ordinals.Length; i++)
		{
			if (ordinals[i] != 0)
			{
				throw new IOException("装备锚点存在重复的同名节点");
			}
		}
	}

	public static bool Same(VisualEquipmentDescription left, VisualEquipmentDescription right, bool compareIdentity)
	{
		if (left == null || right == null || left.Kind != right.Kind)
		{
			return false;
		}
		if (left.Kind == 0)
		{
			return true;
		}
		if (compareIdentity && left.Original != right.Original)
		{
			return false;
		}
		if (left.Name != right.Name || left.Ordinal != right.Ordinal || left.Parents.Length != right.Parents.Length)
		{
			return false;
		}
		for (int i = 0; i < left.Parents.Length; i++)
		{
			if (left.Parents[i] != right.Parents[i] || left.Ordinals[i] != right.Ordinals[i])
			{
				return false;
			}
		}
		return true;
	}
}
