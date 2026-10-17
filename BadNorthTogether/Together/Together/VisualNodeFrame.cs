using System.IO;
using UnityEngine;

namespace Together;

internal struct VisualNodeFrame
{
	internal const byte Active = 1;

	internal const byte Visible = 2;

	internal const byte WorldAim = 4;

	public byte Flags;

	public Vector3 Position;

	public Vector3 Scale;

	public Quaternion Rotation;

	public ushort SpriteId;

	public Color32 Color;

	public void Write(BinaryWriter writer)
	{
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Unknown result type (might be due to invalid IL or missing references)
		//IL_015c: Unknown result type (might be due to invalid IL or missing references)
		byte b = 0;
		if (Position.x != 0f || Position.y != 0f || Position.z != 0f)
		{
			b |= 1;
		}
		if (Rotation.x != 0f || Rotation.y != 0f || Rotation.z != 0f || Rotation.w != 1f)
		{
			b |= 2;
		}
		if (Scale.x != 1f || Scale.y != 1f || Scale.z != 1f)
		{
			b |= 4;
		}
		if (SpriteId != 0)
		{
			b |= 8;
		}
		if (Color.r != byte.MaxValue || Color.g != byte.MaxValue || Color.b != byte.MaxValue || Color.a != byte.MaxValue)
		{
			b |= 0x10;
		}
		writer.Write(Flags);
		writer.Write(b);
		if ((b & 1) != 0)
		{
			VisualWire.WriteCompactVector(writer, Position);
		}
		if ((b & 2) != 0)
		{
			VisualWire.WriteRotation(writer, Rotation);
		}
		if ((b & 4) != 0)
		{
			VisualWire.WriteCompactVector(writer, Scale);
		}
		if ((b & 8) != 0)
		{
			writer.Write(SpriteId);
		}
		if ((b & 0x10) != 0)
		{
			writer.Write(Color.r);
			writer.Write(Color.g);
			writer.Write(Color.b);
			writer.Write(Color.a);
		}
	}

	public static VisualNodeFrame Read(BinaryReader reader)
	{
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		VisualNodeFrame result = new VisualNodeFrame
		{
			Flags = reader.ReadByte()
		};
		if ((result.Flags & -8) != 0)
		{
			throw new IOException("角色表现标志非法");
		}
		byte b = reader.ReadByte();
		if ((b & -32) != 0)
		{
			throw new IOException("角色表现字段掩码非法");
		}
		result.Position = (((b & 1) != 0) ? VisualWire.ReadCompactVector(reader) : Vector3.zero);
		result.Rotation = (((b & 2) != 0) ? VisualWire.ReadRotation(reader) : Quaternion.identity);
		result.Scale = (((b & 4) != 0) ? VisualWire.ReadCompactVector(reader) : Vector3.one);
		result.SpriteId = (ushort)(((b & 8) != 0) ? reader.ReadUInt16() : 0);
		if (result.SpriteId > 32767)
		{
			throw new IOException("角色精灵编号非法");
		}
		result.Color = (((b & 0x10) != 0) ? new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()) : new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue));
		return result;
	}
}
