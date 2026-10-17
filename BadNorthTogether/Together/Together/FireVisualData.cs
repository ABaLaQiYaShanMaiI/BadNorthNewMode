using System.IO;
using UnityEngine;

namespace Together;

internal sealed class FireVisualData
{
	public const byte Arrow = 0;

	public const byte Agent = 1;

	public const byte Residue = 2;

	public byte Kind;

	public int OwnerId;

	public int Sequence;

	public float Time;

	public float Duration;

	public bool Enabled;

	public Vector3 Position;

	public Vector3 Scale;

	public Quaternion Rotation;

	public FireParticleData[] Particles;

	public string Key => Kind + ":" + OwnerId;

	public bool Expired(float time)
	{
		if (Enabled)
		{
			return time >= Time + Duration;
		}
		return false;
	}

	internal static bool SameParticles(FireParticleData[] a, FireParticleData[] b)
	{
		if (a == null || b == null || a.Length != b.Length)
		{
			return false;
		}
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i].Seed != b[i].Seed || a[i].Lifetime != b[i].Lifetime || a[i].Rate != b[i].Rate || a[i].Speed != b[i].Speed || a[i].Emitting != b[i].Emitting || a[i].Looping != b[i].Looping)
			{
				return false;
			}
		}
		return true;
	}

	public void Write(BinaryWriter writer)
	{
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		Validate();
		writer.Write(Kind);
		writer.Write(OwnerId);
		writer.Write(Sequence);
		writer.Write(Time);
		writer.Write(Enabled);
		if (Enabled)
		{
			writer.Write(Duration);
			VisualWire.WriteVector(writer, Position);
			VisualWire.WriteRotation(writer, Rotation);
			VisualWire.WriteVector(writer, Scale);
			writer.Write((byte)Particles.Length);
			FireParticleData[] particles = Particles;
			for (int i = 0; i < particles.Length; i++)
			{
				FireParticleData fireParticleData = particles[i];
				writer.Write(fireParticleData.Seed);
				writer.Write(fireParticleData.Lifetime);
				writer.Write(fireParticleData.Rate);
				writer.Write(fireParticleData.Speed);
				writer.Write(fireParticleData.Emitting);
				writer.Write(fireParticleData.Looping);
			}
		}
	}

	public static FireVisualData Read(BinaryReader reader)
	{
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		FireVisualData fireVisualData = new FireVisualData
		{
			Kind = reader.ReadByte(),
			OwnerId = reader.ReadInt32(),
			Sequence = reader.ReadInt32(),
			Time = reader.ReadSingle(),
			Enabled = reader.ReadBoolean()
		};
		if (fireVisualData.Enabled)
		{
			fireVisualData.Duration = reader.ReadSingle();
			fireVisualData.Position = VisualWire.ReadVector(reader);
			fireVisualData.Rotation = VisualWire.ReadRotation(reader);
			fireVisualData.Scale = VisualWire.ReadVector(reader);
			int num = reader.ReadByte();
			if (num < 1 || num > 32)
			{
				throw new IOException("火焰发射器数量非法");
			}
			fireVisualData.Particles = new FireParticleData[num];
			for (int i = 0; i < num; i++)
			{
				fireVisualData.Particles[i] = new FireParticleData
				{
					Seed = reader.ReadUInt32(),
					Lifetime = reader.ReadSingle(),
					Rate = reader.ReadSingle(),
					Speed = reader.ReadSingle(),
					Emitting = reader.ReadBoolean(),
					Looping = reader.ReadBoolean()
				};
			}
		}
		fireVisualData.Validate();
		return fireVisualData;
	}

	private void Validate()
	{
		if (Kind > 2 || OwnerId <= 0 || OwnerId > 10000000 || Sequence <= 0 || !Range(Time, 0f, 10000000f))
		{
			throw new IOException("火焰事件身份或时间非法");
		}
		if (!Enabled)
		{
			return;
		}
		if (!Range(Duration, 0.001f, 120f) || !Range(Scale.x, 0.001f, 100f) || !Range(Scale.y, 0.001f, 100f) || !Range(Scale.z, 0.001f, 100f) || Particles == null || Particles.Length < 1 || Particles.Length > 32)
		{
			throw new IOException("火焰时长、缩放或发射器非法");
		}
		FireParticleData[] particles = Particles;
		for (int i = 0; i < particles.Length; i++)
		{
			FireParticleData fireParticleData = particles[i];
			if (!Range(fireParticleData.Lifetime, 0f, 30f) || !Range(fireParticleData.Rate, 0f, 10000f) || !Range(fireParticleData.Speed, 0f, 10f))
			{
				throw new IOException("火焰粒子参数非法");
			}
		}
	}

	private static bool Range(float value, float min, float max)
	{
		if (!float.IsNaN(value) && !float.IsInfinity(value) && value >= min)
		{
			return value <= max;
		}
		return false;
	}
}
