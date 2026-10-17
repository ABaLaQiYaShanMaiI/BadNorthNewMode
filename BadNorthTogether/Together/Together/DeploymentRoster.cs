using System;
using System.Collections.Generic;
using System.IO;

namespace Together;

internal static class DeploymentRoster
{
	internal sealed class Message
	{
		public bool State;

		public bool Accepted;

		public int Epoch;

		public int Level;

		public int Request;

		public int Revision;

		public int Slot;

		public int Expected;

		public int Hero;

		public string Reason = string.Empty;

		public int[] Heroes;

		public void Write(BinaryWriter writer)
		{
			Validate();
			writer.Write(14);
			writer.Write(Epoch);
			writer.Write(Level);
			writer.Write(State);
			writer.Write(Request);
			if (State)
			{
				writer.Write(Revision);
				writer.Write(Accepted);
				writer.Write(Reason);
				WriteRoster(writer, Heroes);
			}
			else
			{
				writer.Write(Slot);
				writer.Write(Expected);
				writer.Write(Hero);
			}
		}

		public static Message Read(BinaryReader reader)
		{
			if (reader.ReadInt32() != 14)
			{
				throw new IOException("共享部署名单版本不一致");
			}
			Message message = new Message
			{
				Epoch = reader.ReadInt32(),
				Level = reader.ReadInt32(),
				State = reader.ReadBoolean(),
				Request = reader.ReadInt32()
			};
			if (message.State)
			{
				message.Revision = reader.ReadInt32();
				message.Accepted = reader.ReadBoolean();
				message.Reason = reader.ReadString();
				message.Heroes = ReadRoster(reader);
			}
			else
			{
				message.Slot = reader.ReadInt32();
				message.Expected = reader.ReadInt32();
				message.Hero = reader.ReadInt32();
			}
			message.Validate();
			return message;
		}

		private void Validate()
		{
			if (Epoch <= 0 || Level < 0 || Request < 0)
			{
				throw new IOException("共享部署名单身份非法");
			}
			if (State)
			{
				if (Revision <= 0 || Reason == null || Reason.Length > 256)
				{
					throw new IOException("共享部署名单修订或回复非法");
				}
				ValidateRoster(Heroes);
			}
			else if (Request <= 0 || Slot < 0 || Slot >= 8 || Expected < -1 || Hero < -1)
			{
				throw new IOException("换人请求字段非法");
			}
		}
	}

	public static void ValidateRoster(int[] heroes)
	{
		if (heroes == null || heroes.Length < 1 || heroes.Length > 8)
		{
			throw new IOException("部署槽位数量必须为1至8");
		}
		HashSet<int> hashSet = new HashSet<int>();
		foreach (int num in heroes)
		{
			if (num < -1 || (num >= 0 && !hashSet.Add(num)))
			{
				throw new IOException("部署英雄非法或重复");
			}
		}
	}

	public static void WriteRoster(BinaryWriter writer, int[] heroes)
	{
		ValidateRoster(heroes);
		writer.Write(heroes.Length);
		foreach (int value in heroes)
		{
			writer.Write(value);
		}
	}

	public static int[] ReadRoster(BinaryReader reader)
	{
		int num = reader.ReadInt32();
		if (num < 1 || num > 8)
		{
			throw new IOException("部署槽位数量非法");
		}
		int[] array = new int[num];
		for (int i = 0; i < num; i++)
		{
			array[i] = reader.ReadInt32();
		}
		ValidateRoster(array);
		return array;
	}

	public static bool TryReplace(int[] heroes, int slot, int expected, int hero, int lockedSlot, Predicate<int> available, out string reason)
	{
		ValidateRoster(heroes);
		if (slot < 0 || slot >= heroes.Length || hero < -1)
		{
			reason = "部署槽位或英雄非法";
			return false;
		}
		if (heroes[slot] != expected)
		{
			reason = "该槽位已被另一位玩家修改，请按最新名单选择";
			return false;
		}
		if (lockedSlot == slot && hero != heroes[slot])
		{
			reason = "本岛待招募指挥官的槽位不能替换";
			return false;
		}
		if (hero == heroes[slot])
		{
			reason = "名单未变化";
			return true;
		}
		if (hero >= 0 && !available(hero))
		{
			reason = "该英雄不存在、已阵亡或本回合不可部署";
			return false;
		}
		if (hero >= 0 && Array.IndexOf(heroes, hero) >= 0)
		{
			reason = "该英雄已在另一个部署槽位";
			return false;
		}
		heroes[slot] = hero;
		reason = "换人已确认";
		return true;
	}

	internal static bool CanCommit(int revision, int localReadyRevision, int peerReadyRevision)
	{
		if (revision > 0 && localReadyRevision == revision)
		{
			return peerReadyRevision == revision;
		}
		return false;
	}
}
