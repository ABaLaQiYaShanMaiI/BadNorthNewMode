using System;
using System.IO;

namespace Together;

internal sealed class SurfaceMarkData
{
	internal const int MaxCapacity = 512;

	public byte AnchorKind;

	public bool Grounded;

	public float[] NavPosition = new float[3];

	public int ActorId;

	public int Capacity;

	public int Layer;

	public float Time;

	public bool Clear;

	public bool Loose;

	public bool FlipX;

	public bool FlipY;

	public string[] Names;

	public ushort[] Ordinals;

	public ushort SpriteId;

	public string Material;

	public string Shader;

	public float[] Matrix = new float[16];

	public float[] Color = new float[4];

	public string Key => AnchorKind + ":" + ActorId + ":" + VisualHierarchy.Key(Names, Ordinals);

	public void Write(BinaryWriter writer)
	{
		Validate();
		writer.Write(AnchorKind);
		writer.Write(ActorId);
		writer.Write(Time);
		writer.Write(Clear);
		writer.Write((byte)Names.Length);
		for (int i = 0; i < Names.Length; i++)
		{
			VisualWire.WriteString(writer, Names[i], 256);
			writer.Write(Ordinals[i]);
		}
		if (Clear)
		{
			return;
		}
		writer.Write(Grounded);
		float[] navPosition;
		if (Grounded)
		{
			navPosition = NavPosition;
			foreach (float value in navPosition)
			{
				writer.Write(value);
			}
		}
		writer.Write((ushort)Capacity);
		writer.Write((byte)Layer);
		writer.Write(Loose);
		writer.Write(FlipX);
		writer.Write(FlipY);
		writer.Write(SpriteId);
		VisualWire.WriteString(writer, Material, 256);
		VisualWire.WriteString(writer, Shader, 256);
		navPosition = Matrix;
		foreach (float value2 in navPosition)
		{
			writer.Write(value2);
		}
		navPosition = Color;
		foreach (float value3 in navPosition)
		{
			writer.Write(value3);
		}
	}

	public static SurfaceMarkData Read(BinaryReader reader)
	{
		SurfaceMarkData surfaceMarkData = new SurfaceMarkData
		{
			AnchorKind = reader.ReadByte(),
			ActorId = reader.ReadInt32(),
			Time = reader.ReadSingle(),
			Clear = reader.ReadBoolean()
		};
		int num = reader.ReadByte();
		if (num > 24)
		{
			throw new IOException("附着印记路径过深");
		}
		surfaceMarkData.Names = new string[num];
		surfaceMarkData.Ordinals = new ushort[num];
		for (int i = 0; i < num; i++)
		{
			surfaceMarkData.Names[i] = VisualWire.ReadString(reader, 256);
			surfaceMarkData.Ordinals[i] = reader.ReadUInt16();
		}
		if (!surfaceMarkData.Clear)
		{
			surfaceMarkData.Grounded = reader.ReadBoolean();
			if (surfaceMarkData.Grounded)
			{
				for (int j = 0; j < 3; j++)
				{
					surfaceMarkData.NavPosition[j] = reader.ReadSingle();
				}
			}
			surfaceMarkData.Capacity = reader.ReadUInt16();
			surfaceMarkData.Layer = reader.ReadByte();
			surfaceMarkData.Loose = reader.ReadBoolean();
			surfaceMarkData.FlipX = reader.ReadBoolean();
			surfaceMarkData.FlipY = reader.ReadBoolean();
			surfaceMarkData.SpriteId = reader.ReadUInt16();
			surfaceMarkData.Material = VisualWire.ReadString(reader, 256);
			surfaceMarkData.Shader = VisualWire.ReadString(reader, 256);
			for (int k = 0; k < 16; k++)
			{
				surfaceMarkData.Matrix[k] = reader.ReadSingle();
			}
			for (int l = 0; l < 4; l++)
			{
				surfaceMarkData.Color[l] = reader.ReadSingle();
			}
		}
		surfaceMarkData.Validate();
		return surfaceMarkData;
	}

	private void Validate()
	{
		bool num;
		if (AnchorKind != 0)
		{
			if (AnchorKind != 1)
			{
				if (AnchorKind != 2)
				{
					goto IL_006e;
				}
				num = ActorId >= 0;
			}
			else
			{
				num = ActorId == 0;
			}
		}
		else
		{
			if (ActorId <= 0)
			{
				goto IL_006e;
			}
			num = ActorId <= 10000000;
		}
		if (num && Finite(Time) && !(Time < 0f))
		{
			VisualHierarchy.Key(Names, Ordinals);
			if (Clear)
			{
				return;
			}
			if (Capacity < 1 || Capacity > 512 || Layer < 0 || Layer > 31 || SpriteId == 0 || SpriteId > 32767 || string.IsNullOrEmpty(Material) || string.IsNullOrEmpty(Shader) || Matrix == null || Matrix.Length != 16 || Color == null || Color.Length != 4 || NavPosition == null || NavPosition.Length != 3 || (Grounded && AnchorKind == 0))
			{
				throw new IOException("附着印记资源或容量非法：锚点=" + AnchorKind + ":" + ActorId + " 容量=" + Capacity + " 层=" + Layer + " 精灵=" + SpriteId);
			}
			float[] matrix = Matrix;
			for (int i = 0; i < matrix.Length; i++)
			{
				if (!Coordinate(matrix[i]))
				{
					throw new IOException("附着印记变换非法");
				}
			}
			matrix = NavPosition;
			for (int i = 0; i < matrix.Length; i++)
			{
				if (!Coordinate(matrix[i]))
				{
					throw new IOException("附着印记导航位置非法");
				}
			}
			matrix = Color;
			for (int i = 0; i < matrix.Length; i++)
			{
				if (!Finite(matrix[i]))
				{
					throw new IOException("附着印记颜色非法");
				}
			}
			return;
		}
		goto IL_006e;
		IL_006e:
		throw new IOException("附着印记身份或时间非法");
	}

	private static bool Coordinate(float value)
	{
		if (Finite(value))
		{
			return Math.Abs(value) <= 100000f;
		}
		return false;
	}

	private static bool Finite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}
}
