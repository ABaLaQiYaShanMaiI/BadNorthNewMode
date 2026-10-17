using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Together;

internal sealed class LifeCensus
{
	internal sealed class Entry
	{
		public int Id;

		public byte Flags;

		public float Health;

		public float X;

		public float Y;

		public float Z;

		public PathProgress Progress;

		public byte PathKind = byte.MaxValue;

		public int PathId;

		public int PathRevision;

		public bool Living => (Flags & 3) == 3;
	}

	public const byte Spawned = 1;

	public const byte Alive = 2;

	public const byte Grounded = 4;

	public const byte OnMain = 8;

	public const byte Active = 16;

	public const byte Dormant = 32;

	public const int MaxActors = 8192;

	public const int MaxId = 10000000;

	public int Sequence;

	public int LastId;

	public float Time;

	public bool Frozen;

	public readonly List<Entry> Entries = new List<Entry>();

	public void Write(BinaryWriter writer)
	{
		Validate();
		writer.Write(Sequence);
		writer.Write(Time);
		writer.Write(LastId);
		writer.Write(Frozen);
		writer.Write(Entries.Count);
		foreach (Entry entry in Entries)
		{
			writer.Write(entry.Id);
			writer.Write(entry.Flags);
			writer.Write(entry.Health);
			writer.Write(entry.X);
			writer.Write(entry.Y);
			writer.Write(entry.Z);
			entry.Progress.Write(writer);
			writer.Write(entry.PathKind);
			writer.Write(entry.PathId);
			writer.Write(entry.PathRevision);
		}
	}

	public static LifeCensus Read(BinaryReader reader)
	{
		LifeCensus lifeCensus = new LifeCensus
		{
			Sequence = reader.ReadInt32(),
			Time = reader.ReadSingle(),
			LastId = reader.ReadInt32(),
			Frozen = reader.ReadBoolean()
		};
		int num = reader.ReadInt32();
		if (num < 0 || num > 8192)
		{
			throw new IOException("人物事实清单数量非法");
		}
		for (int i = 0; i < num; i++)
		{
			lifeCensus.Entries.Add(new Entry
			{
				Id = reader.ReadInt32(),
				Flags = reader.ReadByte(),
				Health = reader.ReadSingle(),
				X = reader.ReadSingle(),
				Y = reader.ReadSingle(),
				Z = reader.ReadSingle(),
				Progress = PathProgress.Read(reader),
				PathKind = reader.ReadByte(),
				PathId = reader.ReadInt32(),
				PathRevision = reader.ReadInt32()
			});
		}
		lifeCensus.Validate();
		return lifeCensus;
	}

	private void Validate()
	{
		if (Sequence < 0 || !Finite(Time) || Time < 0f || LastId < 0 || LastId > 10000000 || Entries.Count > 8192)
		{
			throw new IOException("人物事实清单头非法");
		}
		HashSet<int> hashSet = new HashSet<int>();
		foreach (Entry entry in Entries)
		{
			if (entry == null || entry.Id <= 0 || entry.Id > LastId || !hashSet.Add(entry.Id))
			{
				throw new IOException("人物事实清单存在非法或重复身份");
			}
			try
			{
				ValidateFacts(entry.Flags, entry.Health, entry.PathKind, entry.PathId, entry.PathRevision);
			}
			catch (IOException ex)
			{
				throw new IOException("人物事实清单 id=" + entry.Id + "：" + ex.Message, ex);
			}
			if (!Coordinate(entry.X) || !Coordinate(entry.Y) || !Coordinate(entry.Z))
			{
				throw new IOException("人物事实清单 id=" + entry.Id + " 坐标非法");
			}
		}
	}

	internal static void ValidateFacts(byte flags, float health, byte pathKind, int pathId, int pathRevision)
	{
		string text = (((flags & -64) != 0) ? "Flags" : ((!Finite(health)) ? "Health" : ((!ValidPath(pathKind, pathId)) ? "PathKind/PathId" : ((pathRevision < 0) ? "PathRevision" : null))));
		if (text != null)
		{
			throw new IOException(string.Format(CultureInfo.InvariantCulture, "人物状态非法：字段={0} flags={1} health={2:R} pathKind={3} pathId={4} pathRevision={5}", text, flags, health, pathKind, pathId, pathRevision));
		}
	}

	public HashSet<int> Ids()
	{
		HashSet<int> hashSet = new HashSet<int>();
		foreach (Entry entry in Entries)
		{
			hashSet.Add(entry.Id);
		}
		return hashSet;
	}

	public List<int> Missing(IEnumerable<int> known)
	{
		HashSet<int> hashSet = Ids();
		List<int> list = new List<int>();
		foreach (int item in known)
		{
			if (item <= LastId && !hashSet.Contains(item))
			{
				list.Add(item);
			}
		}
		return list;
	}

	internal static bool ValidPath(byte kind, int id)
	{
		switch (kind)
		{
		case 3:
			return id >= 0;
		default:
			return false;
		case 1:
		case 2:
			if (id >= 0)
			{
				return id <= 1000000;
			}
			return false;
		case 0:
			return id == 0;
		case byte.MaxValue:
			return id == 0;
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

	private static bool Coordinate(float value)
	{
		if (Finite(value))
		{
			return Math.Abs(value) <= 100000f;
		}
		return false;
	}
}
