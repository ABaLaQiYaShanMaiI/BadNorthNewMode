using System;
using System.Collections.Generic;
using System.IO;

namespace Together;

internal sealed class BattleSummary
{
	internal sealed class Entry
	{
		public string Reference;

		public int Type;

		public int Kills;

		public bool OnIsland;

		public string Key => Type + ":" + Reference;
	}

	public readonly List<Entry> Entries = new List<Entry>();

	public void Write(BinaryWriter writer)
	{
		Validate();
		writer.Write(Entries.Count);
		foreach (Entry entry in Entries)
		{
			writer.Write(entry.Reference);
			writer.Write(entry.Type);
			writer.Write(entry.Kills);
			writer.Write(entry.OnIsland);
		}
	}

	public static BattleSummary Read(BinaryReader reader)
	{
		int num = reader.ReadInt32();
		if (num < 0 || num > 128)
		{
			throw new IOException("结算敌兵类别数量非法");
		}
		BattleSummary battleSummary = new BattleSummary();
		for (int i = 0; i < num; i++)
		{
			battleSummary.Entries.Add(new Entry
			{
				Reference = reader.ReadString(),
				Type = reader.ReadInt32(),
				Kills = reader.ReadInt32(),
				OnIsland = reader.ReadBoolean()
			});
		}
		battleSummary.Validate();
		return battleSummary;
	}

	private void Validate()
	{
		if (Entries.Count > 128)
		{
			throw new IOException("结算敌兵类别超出预算");
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (Entry entry in Entries)
		{
			if (entry == null || string.IsNullOrEmpty(entry.Reference) || entry.Reference.Length > 256 || entry.Type < 0 || entry.Type > 4095 || entry.Kills < 0 || entry.Kills > 10000000 || !hashSet.Add(entry.Key))
			{
				throw new IOException("结算敌兵身份或计数非法");
			}
		}
	}
}
