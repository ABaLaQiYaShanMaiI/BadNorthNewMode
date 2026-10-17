using System;
using System.IO;

namespace Together;

internal static class AbilityWire
{
	internal sealed class Command
	{
		public int Epoch;

		public int Request;

		public int Hero;

		public int Ordinal;

		public int Target;

		public int Source;

		public float Time;

		public string Type;

		public byte Action;

		public bool Instant;

		public void Write(BinaryWriter writer)
		{
			Validate();
			writer.Write((byte)2);
			writer.Write(Epoch);
			writer.Write(Time);
			writer.Write(Request);
			writer.Write(Hero);
			writer.Write(Type);
			writer.Write(Ordinal);
			writer.Write(Action);
			writer.Write(Target);
			writer.Write(Source);
			writer.Write(Instant);
		}

		public static Command Read(BinaryReader reader)
		{
			if (reader.ReadByte() != 2)
			{
				throw new IOException("技能命令版本不匹配");
			}
			Command command = new Command();
			command.Epoch = reader.ReadInt32();
			command.Time = reader.ReadSingle();
			command.Request = reader.ReadInt32();
			command.Hero = reader.ReadInt32();
			command.Type = reader.ReadString();
			command.Ordinal = reader.ReadInt32();
			command.Action = reader.ReadByte();
			command.Target = reader.ReadInt32();
			command.Source = reader.ReadInt32();
			command.Instant = reader.ReadBoolean();
			command.Validate();
			return command;
		}

		private void Validate()
		{
			Identity(Hero, Type, Ordinal);
			Finite(Time);
			if (Epoch <= 0 || Request <= 0 || Action > 5 || Target < -1 || Source < -1 || (Action != 5 && Source != -1) || (Instant && Action != 2) || ((Action == 0 || Action == 4) && Target != -1))
			{
				throw new IOException("技能命令字段非法");
			}
		}
	}

	internal sealed class State
	{
		public int Hero;

		public int Ordinal;

		public int Charges;

		public int EvacState = -1;

		public int Origin = -1;

		public int Location = -1;

		public int MaxCount;

		public int Activations;

		public int Cooldowns;

		public int Casts;

		public string Type;

		public string Error = string.Empty;

		public bool Active;

		public bool Cooling;

		public bool Blocked;

		public bool BlocksMove;

		public bool Cancelable;

		public bool Available;

		public bool InstantAvailable;

		public bool Current;

		public bool Selectable;

		public bool Display;

		public bool Formation;

		public bool SwitchOn;

		public bool NeedsHouse;

		public bool Equipped;

		public bool PlungeUsed;

		public bool NoRegen;

		public bool Crippled;

		public float Remaining;

		public float Period;

		public float EffectEnd;

		public float Time;

		public int[] Targets = new int[0];

		public int[] Sources = new int[0];

		public string Key => Hero + ":" + Type + ":" + Ordinal;

		public bool CanUse(bool instant)
		{
			if (!Blocked)
			{
				if (!instant)
				{
					return Available;
				}
				return InstantAvailable;
			}
			return false;
		}

		public bool HasTarget(int value)
		{
			return Array.IndexOf(Targets, value) >= 0;
		}

		public void Write(BinaryWriter writer)
		{
			Validate();
			writer.Write(Hero);
			writer.Write(Type);
			writer.Write(Ordinal);
			writer.Write(Charges);
			writer.Write(Active);
			writer.Write(Cooling);
			writer.Write(Blocked);
			writer.Write(BlocksMove);
			writer.Write(Cancelable);
			writer.Write(Available);
			writer.Write(InstantAvailable);
			writer.Write(Current);
			writer.Write(Selectable);
			writer.Write(Display);
			writer.Write(Remaining);
			writer.Write(Period);
			writer.Write(EffectEnd);
			writer.Write(EvacState);
			writer.Write(Origin);
			writer.Write(Location);
			writer.Write(MaxCount);
			writer.Write(Activations);
			writer.Write(Cooldowns);
			writer.Write(Casts);
			writer.Write(Formation);
			writer.Write(SwitchOn);
			writer.Write(NeedsHouse);
			writer.Write(Equipped);
			writer.Write(PlungeUsed);
			writer.Write(NoRegen);
			writer.Write(Crippled);
			writer.Write(Error);
			WriteIds(writer, Targets);
			WriteIds(writer, Sources);
		}

		public static State Read(BinaryReader reader, float time)
		{
			State state = new State();
			state.Hero = reader.ReadInt32();
			state.Type = reader.ReadString();
			state.Ordinal = reader.ReadInt32();
			state.Charges = reader.ReadInt32();
			state.Active = reader.ReadBoolean();
			state.Cooling = reader.ReadBoolean();
			state.Blocked = reader.ReadBoolean();
			state.BlocksMove = reader.ReadBoolean();
			state.Cancelable = reader.ReadBoolean();
			state.Available = reader.ReadBoolean();
			state.InstantAvailable = reader.ReadBoolean();
			state.Current = reader.ReadBoolean();
			state.Selectable = reader.ReadBoolean();
			state.Display = reader.ReadBoolean();
			state.Remaining = reader.ReadSingle();
			state.Period = reader.ReadSingle();
			state.EffectEnd = reader.ReadSingle();
			state.EvacState = reader.ReadInt32();
			state.Origin = reader.ReadInt32();
			state.Location = reader.ReadInt32();
			state.MaxCount = reader.ReadInt32();
			state.Activations = reader.ReadInt32();
			state.Cooldowns = reader.ReadInt32();
			state.Casts = reader.ReadInt32();
			state.Formation = reader.ReadBoolean();
			state.SwitchOn = reader.ReadBoolean();
			state.NeedsHouse = reader.ReadBoolean();
			state.Equipped = reader.ReadBoolean();
			state.PlungeUsed = reader.ReadBoolean();
			state.NoRegen = reader.ReadBoolean();
			state.Crippled = reader.ReadBoolean();
			state.Error = reader.ReadString();
			state.Targets = ReadIds(reader);
			state.Sources = ReadIds(reader);
			state.Time = time;
			state.Validate();
			return state;
		}

		private void Validate()
		{
			Identity(Hero, Type, Ordinal);
			Finite(Time);
			Finite(Remaining);
			Finite(Period);
			Finite(EffectEnd);
			if (Charges < 0 || Remaining < 0f || Period < 0f || EvacState < -1 || EvacState > 4 || Origin < -1 || Origin >= 192 || Location < -1 || MaxCount < 0 || MaxCount > 4096 || Activations < 0 || Cooldowns < 0 || Casts < 0 || Error == null || Error.Length > 512)
			{
				throw new IOException("技能状态字段非法");
			}
			ValidateIds(Targets);
			ValidateIds(Sources);
		}
	}

	public const byte Schema = 2;

	public const byte Direct = 0;

	public const byte Nav = 1;

	public const byte House = 2;

	public const byte Ship = 3;

	public const byte Cancel = 4;

	public const byte Ring = 5;

	public const int MaxAbilities = 512;

	public const int MaxTargets = 512;

	internal static bool MatchesFocus(string requestKey, int requestFocus, string currentKey, int currentFocus)
	{
		if (requestFocus == currentFocus)
		{
			return string.Equals(requestKey, currentKey, StringComparison.Ordinal);
		}
		return false;
	}

	internal static bool CompletesSelection(byte action, int requestHero, int requestSelection, int requestChoice, int currentHero, int currentSelection, int currentChoice)
	{
		if (action != 4 && requestHero >= 0 && requestHero == currentHero && requestSelection == currentSelection)
		{
			return requestChoice == currentChoice;
		}
		return false;
	}

	private static void Identity(int hero, string type, int ordinal)
	{
		if (hero < 0 || string.IsNullOrEmpty(type) || type.Length > 256 || ordinal < 0 || ordinal > 16)
		{
			throw new IOException("技能身份非法");
		}
	}

	internal static void Finite(float value)
	{
		if (float.IsNaN(value) || float.IsInfinity(value))
		{
			throw new IOException("技能时间非法");
		}
	}

	private static void ValidateIds(int[] ids)
	{
		if (ids == null || ids.Length > 512)
		{
			throw new IOException("技能目标数量非法");
		}
		for (int i = 0; i < ids.Length; i++)
		{
			if (ids[i] < 0 || Array.IndexOf(ids, ids[i]) != i)
			{
				throw new IOException("技能目标重复或非法");
			}
		}
	}

	private static void WriteIds(BinaryWriter writer, int[] ids)
	{
		ValidateIds(ids);
		writer.Write(ids.Length);
		foreach (int value in ids)
		{
			writer.Write(value);
		}
	}

	private static int[] ReadIds(BinaryReader reader)
	{
		int num = reader.ReadInt32();
		if (num < 0 || num > 512)
		{
			throw new IOException("技能目标数量非法");
		}
		int[] array = new int[num];
		for (int i = 0; i < num; i++)
		{
			array[i] = reader.ReadInt32();
		}
		ValidateIds(array);
		return array;
	}
}
