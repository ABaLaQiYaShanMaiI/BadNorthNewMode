using System.IO;
using UnityEngine;

namespace Together;

internal static class ProjectileFrame
{
	public const int MaxNodes = 64;

	public static void Write(BinaryWriter writer, ProjectileNodePose[] poses)
	{
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0109: Unknown result type (might be due to invalid IL or missing references)
		//IL_0159: Unknown result type (might be due to invalid IL or missing references)
		//IL_011b: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_017c: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Unknown result type (might be due to invalid IL or missing references)
		//IL_019f: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c1: Unknown result type (might be due to invalid IL or missing references)
		if (poses == null || poses.Length == 0 || poses.Length > 64)
		{
			throw new IOException("飞行物姿态节点数量异常");
		}
		writer.Write((byte)poses.Length);
		for (int i = 0; i < poses.Length; i++)
		{
			ProjectileNodePose pose = poses[i];
			Validate(pose);
			byte b = (byte)((pose.Visible ? 1u : 0u) | (uint)(pose.FlipX ? 2 : 0) | (uint)(pose.FlipY ? 4 : 0));
			if (pose.Scale.x != 1f || pose.Scale.y != 1f || pose.Scale.z != 1f)
			{
				b |= 8;
			}
			if (pose.Color.r != 1f || pose.Color.g != 1f || pose.Color.b != 1f || pose.Color.a != 1f)
			{
				b |= 0x10;
			}
			if (pose.SpriteId != 0)
			{
				b |= 0x20;
			}
			if (pose.Rotation.x != 0f || pose.Rotation.y != 0f || pose.Rotation.z != 0f || pose.Rotation.w != 1f)
			{
				b |= 0x40;
			}
			writer.Write(pose.Kind);
			writer.Write(b);
			VisualWire.WriteVector(writer, pose.Position);
			if ((b & 0x40) != 0)
			{
				VisualWire.WriteRotation(writer, pose.Rotation);
			}
			if ((b & 8) != 0)
			{
				VisualWire.WriteVector(writer, pose.Scale);
			}
			if ((b & 0x10) != 0)
			{
				writer.Write(pose.Color.r);
				writer.Write(pose.Color.g);
				writer.Write(pose.Color.b);
				writer.Write(pose.Color.a);
			}
			if ((b & 0x20) != 0)
			{
				writer.Write(pose.SpriteId);
			}
		}
	}

	public static ProjectileNodePose[] Read(BinaryReader reader)
	{
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		int num = reader.ReadByte();
		if (num == 0 || num > 64)
		{
			throw new IOException("飞行物姿态节点数量异常");
		}
		ProjectileNodePose[] array = new ProjectileNodePose[num];
		for (int i = 0; i < num; i++)
		{
			ProjectileNodePose projectileNodePose = new ProjectileNodePose
			{
				Kind = reader.ReadByte()
			};
			byte b = reader.ReadByte();
			if ((b & 0x80) != 0)
			{
				throw new IOException("飞行物字段标志非法");
			}
			projectileNodePose.Position = VisualWire.ReadVector(reader);
			projectileNodePose.Visible = (b & 1) != 0;
			projectileNodePose.FlipX = (b & 2) != 0;
			projectileNodePose.FlipY = (b & 4) != 0;
			projectileNodePose.Rotation = (((b & 0x40) != 0) ? VisualWire.ReadRotation(reader) : Quaternion.identity);
			projectileNodePose.Scale = (((b & 8) != 0) ? VisualWire.ReadVector(reader) : Vector3.one);
			projectileNodePose.Color = (Color)(((b & 0x10) != 0) ? new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()) : Color.white);
			projectileNodePose.SpriteId = (ushort)(((b & 0x20) != 0) ? reader.ReadUInt16() : 0);
			Validate(projectileNodePose);
			array[i] = projectileNodePose;
		}
		return array;
	}

	private static void Validate(ProjectileNodePose pose)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		if (pose.Kind > 2 || pose.SpriteId > 32767 || (pose.Kind == 0 && pose.SpriteId != 0) || !Finite(pose.Color.r) || !Finite(pose.Color.g) || !Finite(pose.Color.b) || !Finite(pose.Color.a))
		{
			throw new IOException("飞行物绘制类型或精灵资源非法");
		}
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
