using System.IO;
using UnityEngine;

namespace Together;

internal sealed class SceneDecorationData
{
	public const byte Corpse = 0;

	public const byte Paint = 1;

	public const byte Clear = 2;

	public byte Kind;

	public byte AnchorKind;

	public int Sequence;

	public int AnchorId;

	public int Group;

	public int Slot;

	public int Capacity;

	public int MeshIndex;

	public int ActorId;

	public float Time;

	public string[] Names = new string[0];

	public ushort[] Ordinals = new ushort[0];

	public Vector3 Position;

	public Vector3[] Vertices;

	public Color32[] Colors;

	public string Key => AnchorKind + ":" + AnchorId + ":" + ((Kind == 1) ? ("paint:" + VisualHierarchy.Key(Names, Ordinals)) : ("corpse:" + Group));

	public void Write(BinaryWriter writer)
	{
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_0123: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		Validate();
		writer.Write(Kind);
		writer.Write(AnchorKind);
		writer.Write(AnchorId);
		writer.Write(Sequence);
		writer.Write(Time);
		if (Kind == 1)
		{
			writer.Write((byte)Names.Length);
			for (int i = 0; i < Names.Length; i++)
			{
				VisualWire.WriteString(writer, Names[i], 256);
				writer.Write(Ordinals[i]);
			}
			VisualWire.WriteVector(writer, Position);
			return;
		}
		writer.Write(Group);
		if (Kind != 2)
		{
			writer.Write((ushort)Slot);
			writer.Write((ushort)Capacity);
			writer.Write((ushort)MeshIndex);
			writer.Write(ActorId);
			writer.Write((ushort)Vertices.Length);
			for (int j = 0; j < Vertices.Length; j++)
			{
				VisualWire.WriteVector(writer, Vertices[j]);
				Color32 val = Colors[j];
				writer.Write(val.r);
				writer.Write(val.g);
				writer.Write(val.b);
				writer.Write(val.a);
			}
		}
	}

	public static SceneDecorationData Read(BinaryReader reader)
	{
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_016e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0173: Unknown result type (might be due to invalid IL or missing references)
		SceneDecorationData sceneDecorationData = new SceneDecorationData
		{
			Kind = reader.ReadByte(),
			AnchorKind = reader.ReadByte(),
			AnchorId = reader.ReadInt32(),
			Sequence = reader.ReadInt32(),
			Time = reader.ReadSingle()
		};
		if (sceneDecorationData.Kind == 1)
		{
			int num = reader.ReadByte();
			if (num > 24)
			{
				throw new IOException("场景染血路径过深");
			}
			sceneDecorationData.Names = new string[num];
			sceneDecorationData.Ordinals = new ushort[num];
			for (int i = 0; i < num; i++)
			{
				sceneDecorationData.Names[i] = VisualWire.ReadString(reader, 256);
				sceneDecorationData.Ordinals[i] = reader.ReadUInt16();
			}
			sceneDecorationData.Position = VisualWire.ReadVector(reader);
		}
		else
		{
			sceneDecorationData.Group = reader.ReadInt32();
			if (sceneDecorationData.Kind != 2)
			{
				sceneDecorationData.Slot = reader.ReadUInt16();
				sceneDecorationData.Capacity = reader.ReadUInt16();
				sceneDecorationData.MeshIndex = reader.ReadUInt16();
				sceneDecorationData.ActorId = reader.ReadInt32();
				int num2 = reader.ReadUInt16();
				if (num2 < 3 || num2 > 512)
				{
					throw new IOException("尸体顶点数量非法");
				}
				sceneDecorationData.Vertices = (Vector3[])(object)new Vector3[num2];
				sceneDecorationData.Colors = (Color32[])(object)new Color32[num2];
				for (int j = 0; j < num2; j++)
				{
					sceneDecorationData.Vertices[j] = VisualWire.ReadVector(reader);
					sceneDecorationData.Colors[j] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
				}
			}
		}
		sceneDecorationData.Validate();
		return sceneDecorationData;
	}

	private void Validate()
	{
		if (Kind > 2 || (AnchorKind != 1 && AnchorKind != 2) || AnchorId < 0 || (AnchorKind == 1 && AnchorId != 0) || Sequence <= 0 || float.IsNaN(Time) || float.IsInfinity(Time) || Time < 0f)
		{
			throw new IOException("场景装饰消息头非法");
		}
		if (Kind == 1)
		{
			VisualHierarchy.Key(Names, Ordinals);
			return;
		}
		if (Group <= 0 || Group > 10000000)
		{
			throw new IOException("尸体批次编号非法");
		}
		if (Kind == 2 || (Slot >= 0 && Capacity >= 1 && Capacity <= 1024 && Slot < Capacity && MeshIndex >= 0 && MeshIndex <= 1024 && ActorId > 0 && ActorId <= 10000000 && Vertices != null && Vertices.Length >= 3 && Vertices.Length <= 512 && Colors != null && Colors.Length == Vertices.Length))
		{
			return;
		}
		throw new IOException("尸体几何描述非法");
	}
}
