using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Together;

internal static class VisualWire
{
	public static void WriteString(BinaryWriter writer, string value, int maxBytes)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
		if (bytes.Length > maxBytes)
		{
			throw new IOException("表现字符串过长");
		}
		writer.Write((ushort)bytes.Length);
		writer.Write(bytes);
	}

	public static string ReadString(BinaryReader reader, int maxBytes)
	{
		int num = reader.ReadUInt16();
		if (num > maxBytes)
		{
			throw new IOException("表现字符串长度非法");
		}
		byte[] array = reader.ReadBytes(num);
		if (array.Length != num)
		{
			throw new EndOfStreamException();
		}
		return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(array);
	}

	public static float ReadFinite(BinaryReader reader, float maximum)
	{
		float num = reader.ReadSingle();
		if (float.IsNaN(num) || float.IsInfinity(num) || Mathf.Abs(num) > maximum)
		{
			throw new IOException("表现坐标超出范围");
		}
		return num;
	}

	public static void WriteVector(BinaryWriter writer, Vector3 value)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		if (!FiniteCoordinate(value.x) || !FiniteCoordinate(value.y) || !FiniteCoordinate(value.z))
		{
			throw new IOException("主机表现坐标超出范围");
		}
		writer.Write(value.x);
		writer.Write(value.y);
		writer.Write(value.z);
	}

	private static bool FiniteCoordinate(float value)
	{
		if (!float.IsNaN(value) && !float.IsInfinity(value))
		{
			return Mathf.Abs(value) <= 100000f;
		}
		return false;
	}

	public static Vector3 ReadVector(BinaryReader reader)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		return new Vector3(ReadFinite(reader, 100000f), ReadFinite(reader, 100000f), ReadFinite(reader, 100000f));
	}

	public static void WriteCompactVector(BinaryWriter writer, Vector3 value)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		WriteQuantized(writer, value.x, 512f);
		WriteQuantized(writer, value.y, 512f);
		WriteQuantized(writer, value.z, 512f);
	}

	public static Vector3 ReadCompactVector(BinaryReader reader)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		return new Vector3((float)reader.ReadInt16() / 512f, (float)reader.ReadInt16() / 512f, (float)reader.ReadInt16() / 512f);
	}

	private static void WriteQuantized(BinaryWriter writer, float value, float multiplier)
	{
		if (float.IsNaN(value) || float.IsInfinity(value) || Mathf.Abs(value * multiplier) > 32767f)
		{
			throw new IOException("表现局部姿态超过量化范围，拒绝截断");
		}
		writer.Write((short)Mathf.RoundToInt(value * multiplier));
	}

	public static void WriteRotation(BinaryWriter writer, Quaternion rotation)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0100: Unknown result type (might be due to invalid IL or missing references)
		double num = (double)rotation.x * (double)rotation.x + (double)rotation.y * (double)rotation.y + (double)rotation.z * (double)rotation.z + (double)rotation.w * (double)rotation.w;
		if (double.IsNaN(num) || double.IsInfinity(num) || num == 0.0)
		{
			throw new IOException(string.Format(CultureInfo.InvariantCulture, "主机表现旋转无效（零值或非有限分量）：({0:R}, {1:R}, {2:R}, {3:R})", rotation.x, rotation.y, rotation.z, rotation.w));
		}
		double num2 = 1.0 / Math.Sqrt(num);
		WriteQuantized(writer, (float)((double)rotation.x * num2), 32767f);
		WriteQuantized(writer, (float)((double)rotation.y * num2), 32767f);
		WriteQuantized(writer, (float)((double)rotation.z * num2), 32767f);
		WriteQuantized(writer, (float)((double)rotation.w * num2), 32767f);
	}

	public static Quaternion ReadRotation(BinaryReader reader)
	{
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		float num = (float)reader.ReadInt16() / 32767f;
		float num2 = (float)reader.ReadInt16() / 32767f;
		float num3 = (float)reader.ReadInt16() / 32767f;
		float num4 = (float)reader.ReadInt16() / 32767f;
		float num5 = Mathf.Sqrt(num * num + num2 * num2 + num3 * num3 + num4 * num4);
		if (num5 < 0.9f || num5 > 1.1f)
		{
			throw new IOException("表现旋转不是单位四元数");
		}
		return new Quaternion(num / num5, num2 / num5, num3 / num5, num4 / num5);
	}
}
