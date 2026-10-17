using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Together;

internal static class VisualResources
{
	internal const int MaxResources = 32767;

	private static readonly Dictionary<Sprite, ushort> localIds = new Dictionary<Sprite, ushort>();

	private static readonly Dictionary<string, Sprite> localSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);

	private static readonly HashSet<string> ambiguous = new HashSet<string>(StringComparer.Ordinal);

	private static readonly Dictionary<ushort, string> definitions = new Dictionary<ushort, string>();

	private static readonly Dictionary<ushort, Sprite> resolved = new Dictionary<ushort, Sprite>();

	private static readonly HashSet<ushort> missing = new HashSet<ushort>();

	private static readonly HashSet<ushort> required = new HashSet<ushort>();

	private static readonly List<ushort> pending = new List<ushort>();

	private static float nextRefresh;

	private static float nextMissingRetry;

	private static bool initialized;

	public static bool HasPendingDefinitions => pending.Count != 0;

	public static bool HasPendingRequiredDefinitions
	{
		get
		{
			foreach (ushort item in pending)
			{
				if (required.Contains(item))
				{
					return true;
				}
			}
			return false;
		}
	}

	public static int UnresolvedCount
	{
		get
		{
			RetryMissing();
			return missing.Count;
		}
	}

	public static void Reset()
	{
		localIds.Clear();
		localSprites.Clear();
		ambiguous.Clear();
		definitions.Clear();
		resolved.Clear();
		missing.Clear();
		required.Clear();
		pending.Clear();
		initialized = false;
		nextRefresh = (nextMissingRetry = 0f);
	}

	private static void RetryMissing()
	{
		if (missing.Count != 0 && !(Time.realtimeSinceStartup < nextMissingRetry))
		{
			nextMissingRetry = Time.realtimeSinceStartup + 1f;
			Initialize();
			ushort[] array = new ushort[missing.Count];
			missing.CopyTo(array);
			ushort[] array2 = array;
			for (int i = 0; i < array2.Length; i++)
			{
				TryResolve(array2[i], out var _);
			}
		}
	}

	public static void Initialize()
	{
		localSprites.Clear();
		ambiguous.Clear();
		Sprite[] array = Resources.FindObjectsOfTypeAll<Sprite>();
		foreach (Sprite val in array)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)val) && global::UnityObjectOps.Truthy((Object)(object)val.texture))
			{
				AddLocal(val, Identity(val));
			}
		}
		initialized = true;
		nextRefresh = Time.realtimeSinceStartup + 1f;
	}

	private static void AddLocal(Sprite sprite, string identity)
	{
		if (localSprites.TryGetValue(identity, out var value))
		{
			if (global::UnityObjectOps.Truthy((Object)(object)value) && (Object)(object)value.texture != (Object)(object)sprite.texture)
			{
				ambiguous.Add(identity);
			}
		}
		else
		{
			localSprites.Add(identity, sprite);
		}
	}

	public static ushort GetId(Sprite sprite)
	{
		return Register(sprite, mandatory: true);
	}

	public static ushort GetOptionalId(Sprite sprite)
	{
		return Register(sprite, mandatory: false);
	}

	private static ushort Require(ushort id, bool mandatory)
	{
		if (mandatory && required.Add(id) && !pending.Contains(id))
		{
			pending.Add(id);
		}
		return id;
	}

	private static ushort Register(Sprite sprite, bool mandatory)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)sprite))
		{
			return 0;
		}
		if (localIds.TryGetValue(sprite, out var value))
		{
			return Require(value, mandatory);
		}
		if (!initialized)
		{
			Initialize();
		}
		string text = Identity(sprite);
		AddLocal(sprite, text);
		if (ambiguous.Contains(text))
		{
			throw new IOException("精灵资源身份冲突，拒绝错误匹配：" + ((Object)sprite).name);
		}
		foreach (KeyValuePair<ushort, string> definition in definitions)
		{
			if (!(definition.Value != text))
			{
				localIds.Add(sprite, definition.Key);
				return Require(definition.Key, mandatory);
			}
		}
		if (definitions.Count >= 32767)
		{
			throw new IOException("精灵资源表超过安全上限");
		}
		value = (ushort)(definitions.Count + 1);
		definitions.Add(value, text);
		resolved[value] = sprite;
		localIds.Add(sprite, value);
		pending.Add(value);
		return Require(value, mandatory);
	}

	public static void WritePendingDefinitions(BinaryWriter writer)
	{
		writer.Write((ushort)pending.Count);
		foreach (ushort item in pending)
		{
			writer.Write(item);
			writer.Write(required.Contains(item));
			VisualWire.WriteString(writer, definitions[item], 2048);
		}
		pending.Clear();
	}

	public static void ReadDefinitions(BinaryReader reader)
	{
		int num = reader.ReadUInt16();
		if (num > 32767)
		{
			throw new IOException("精灵资源定义数量非法");
		}
		if (!initialized)
		{
			Initialize();
		}
		for (int i = 0; i < num; i++)
		{
			ushort num2 = reader.ReadUInt16();
			bool num3 = reader.ReadBoolean();
			string text = VisualWire.ReadString(reader, 2048);
			if (num2 == 0 || num2 > 32767 || text.Length == 0)
			{
				throw new IOException("精灵资源编号非法");
			}
			if (definitions.TryGetValue(num2, out var value) && value != text)
			{
				throw new IOException("主机重定义了已有精灵资源编号");
			}
			definitions[num2] = text;
			if (num3)
			{
				required.Add(num2);
			}
			TryResolve(num2, out var _);
		}
	}

	public static bool TryResolve(ushort id, out Sprite sprite)
	{
		sprite = null;
		if (id == 0)
		{
			return true;
		}
		if (resolved.TryGetValue(id, out sprite) && global::UnityObjectOps.Truthy((Object)(object)sprite))
		{
			return true;
		}
		if (!definitions.TryGetValue(id, out var value))
		{
			ReportMissing(id, "未收到资源定义");
			return false;
		}
		if (!initialized || Time.realtimeSinceStartup >= nextRefresh)
		{
			Initialize();
		}
		if (!ambiguous.Contains(value) && localSprites.TryGetValue(value, out sprite) && global::UnityObjectOps.Truthy((Object)(object)sprite))
		{
			resolved[id] = sprite;
			if (missing.Remove(id) && Plugin.Log != null)
			{
				Plugin.Log.LogInfo((object)("[表现] 精灵资源已恢复：" + id));
			}
			return true;
		}
		ReportMissing(id, ambiguous.Contains(value) ? "本地身份冲突" : "本地资源尚未加载");
		sprite = null;
		return false;
	}

	private static void ReportMissing(ushort id, string reason)
	{
		if (required.Contains(id) && missing.Add(id) && Plugin.Log != null)
		{
			Plugin.Log.LogError((object)("[表现] 精灵 " + id + " 无法显示：" + reason + "；暂时隐藏受影响部件并自动重试"));
		}
	}

	private static string Identity(Sprite sprite)
	{
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_0139: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		binaryWriter.Write(((Object)sprite).name ?? "");
		binaryWriter.Write(((Object)sprite.texture).name ?? "");
		binaryWriter.Write(((Texture)sprite.texture).width);
		binaryWriter.Write(((Texture)sprite.texture).height);
		Rect rect = sprite.rect;
		binaryWriter.Write(rect.x);
		binaryWriter.Write(rect.y);
		binaryWriter.Write(rect.width);
		binaryWriter.Write(rect.height);
		binaryWriter.Write(sprite.pivot.x);
		binaryWriter.Write(sprite.pivot.y);
		binaryWriter.Write(sprite.pixelsPerUnit);
		Vector2[] vertices = sprite.vertices;
		Vector2[] uv = sprite.uv;
		ushort[] triangles = sprite.triangles;
		binaryWriter.Write(vertices.Length);
		Vector2[] array = vertices;
		foreach (Vector2 val in array)
		{
			binaryWriter.Write(val.x);
			binaryWriter.Write(val.y);
		}
		binaryWriter.Write(uv.Length);
		array = uv;
		foreach (Vector2 val2 in array)
		{
			binaryWriter.Write(val2.x);
			binaryWriter.Write(val2.y);
		}
		binaryWriter.Write(triangles.Length);
		ushort[] array2 = triangles;
		foreach (ushort value in array2)
		{
			binaryWriter.Write(value);
		}
		binaryWriter.Flush();
		using SHA256 sHA = SHA256.Create();
		byte[] array3 = sHA.ComputeHash(memoryStream.ToArray());
		StringBuilder stringBuilder = new StringBuilder(64);
		byte[] array4 = array3;
		foreach (byte b in array4)
		{
			stringBuilder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
		}
		return stringBuilder.ToString();
	}
}
