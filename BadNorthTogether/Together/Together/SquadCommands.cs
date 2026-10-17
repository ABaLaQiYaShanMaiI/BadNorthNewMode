using System.Collections.Generic;
using System.IO;
using Together.Net;
using UnityEngine;
using Voxels.TowerDefense;

namespace Together;

internal static class SquadCommands
{
	private struct Command
	{
		public int Epoch;

		public int Level;

		public int Hero;

		public int Target;

		public int Sequence;

		public float Time;

		public byte Kind;

		public int[] Heroes;

		public int[] Spots;

		public int[] Revisions;
	}

	private const float HostPrioritySeconds = 0.3f;

	private static readonly Queue<Command> clientQueue = new Queue<Command>();

	private static readonly Dictionary<int, float> hostMoveTimes = new Dictionary<int, float>();

	private static int epoch;

	private static int sequence;

	private static int receivedSequence;

	private static int operationDepth;

	private static bool initialized;

	private static bool dirty;

	private static bool applyingHostIntent;

	public static void Initialize()
	{
		if (!initialized)
		{
			initialized = true;
			Session.On(MsgType.SquadMove, Receive);
			Session.Stopped += Reset;
		}
	}

	private static void Reset()
	{
		clientQueue.Clear();
		hostMoveTimes.Clear();
		epoch = (sequence = (receivedSequence = (operationDepth = 0)));
		dirty = false;
	}

	private static void EnsureBattle()
	{
		if (epoch != CampaignSync.CurrentBattleEpoch)
		{
			Reset();
			epoch = CampaignSync.CurrentBattleEpoch;
		}
	}

	public static void Tick()
	{
		if (!Session.Active || !InLevel() || CampaignSync.ClosingBattle)
		{
			clientQueue.Clear();
			return;
		}
		EnsureBattle();
		if (!Session.IsClient)
		{
			return;
		}
		while (clientQueue.Count > 0)
		{
			Command command = clientQueue.Dequeue();
			if (Matches(command) && command.Sequence > receivedSequence)
			{
				ApplyFacts(command);
				receivedSequence = command.Sequence;
			}
		}
	}

	public static bool SendClientMove(EnglishSquad squad, NavSpot spot)
	{
		return SendClientIntent(squad, global::UnityObjectOps.Truthy((Object)(object)spot) ? spot.index : (-1), 0);
	}

	public static bool SendClientSwap(EnglishSquad squad, NavSpotController other)
	{
		if (global::UnityObjectOps.Truthy((Object)(object)other) && global::UnityObjectOps.Truthy((Object)(object)other.enSquad))
		{
			return SendClientIntent(squad, other.enSquad.hero.id, 1);
		}
		return false;
	}

	private static bool SendClientIntent(EnglishSquad squad, int target, byte kind)
	{
		if (!Session.IsClient || !InLevel() || CampaignSync.ClosingBattle || !global::UnityObjectOps.Truthy((Object)(object)squad) || ((Squad)squad).dead || target < 0)
		{
			return false;
		}
		AbilityCommands.ApplySquadUiFacts(squad);
		if (!squad.navSpotOccupant.canMove)
		{
			return false;
		}
		EnsureBattle();
		return Session.Send(Write(new Command
		{
			Epoch = epoch,
			Level = Profile.campaign.lastPlayedLevelIdx,
			Hero = squad.hero.id,
			Target = target,
			Kind = kind
		}));
	}

	private static byte[] Write(Command command)
	{
		MessageWriter messageWriter = Protocol.Begin(MsgType.SquadMove);
		messageWriter.Write(14);
		messageWriter.Write(command.Epoch);
		messageWriter.Write(command.Level);
		messageWriter.Write(command.Kind);
		if (command.Kind != 2)
		{
			messageWriter.Write(command.Hero);
			messageWriter.Write(command.Target);
		}
		else
		{
			messageWriter.Write(command.Sequence);
			messageWriter.Write(command.Time);
			messageWriter.Write(command.Heroes.Length);
			for (int i = 0; i < command.Heroes.Length; i++)
			{
				messageWriter.Write(command.Heroes[i]);
				messageWriter.Write(command.Spots[i]);
				messageWriter.Write(command.Revisions[i]);
			}
		}
		return messageWriter.ToArray();
	}

	private static void Receive(BinaryReader reader)
	{
		if (reader.ReadInt32() != 14)
		{
			throw new IOException("小队指令版本不一致");
		}
		Command command = new Command
		{
			Epoch = reader.ReadInt32(),
			Level = reader.ReadInt32(),
			Kind = reader.ReadByte()
		};
		if (command.Kind < 2)
		{
			command.Hero = reader.ReadInt32();
			command.Target = reader.ReadInt32();
		}
		else
		{
			if (command.Kind != 2)
			{
				throw new IOException("未知小队指令");
			}
			command.Sequence = reader.ReadInt32();
			command.Time = reader.ReadSingle();
			int num = reader.ReadInt32();
			if (num < 1 || num > 256 || command.Sequence <= 0 || float.IsNaN(command.Time) || float.IsInfinity(command.Time) || command.Time < 0f)
			{
				throw new IOException("小队占位事实非法");
			}
			command.Heroes = new int[num];
			command.Spots = new int[num];
			command.Revisions = new int[num];
			HashSet<int> hashSet = new HashSet<int>();
			for (int i = 0; i < num; i++)
			{
				command.Heroes[i] = reader.ReadInt32();
				command.Spots[i] = reader.ReadInt32();
				command.Revisions[i] = reader.ReadInt32();
				if (command.Heroes[i] < 0 || command.Spots[i] < -1 || command.Revisions[i] < 0 || !hashSet.Add(command.Heroes[i]))
				{
					throw new IOException("小队占位编号非法");
				}
			}
		}
		reader.RequireEnd();
		if (!InLevel() || !Matches(command))
		{
			return;
		}
		EnsureBattle();
		if (Session.IsHost && command.Kind < 2)
		{
			ApplyIntent(command);
		}
		else if (Session.IsClient && command.Kind == 2)
		{
			if (clientQueue.Count >= 256)
			{
				throw new IOException("小队指令积压超限");
			}
			clientQueue.Enqueue(command);
		}
	}

	private static bool Matches(Command command)
	{
		if (Profile.campaign != null)
		{
			return CommandIdentity.Matches(command.Epoch, CampaignSync.CurrentBattleEpoch, command.Level, Profile.campaign.lastPlayedLevelIdx, CampaignSync.ClosingBattle);
		}
		return false;
	}

	private static EnglishSquad FindSquad(int hero)
	{
		SquadSelector instance = Singleton<SquadSelector>.instance;
		if (!global::UnityObjectOps.Truthy((Object)(object)instance))
		{
			return null;
		}
		foreach (Squad squad in instance.squads)
		{
			EnglishSquad val = (EnglishSquad)(object)((squad is EnglishSquad) ? squad : null);
			if (global::UnityObjectOps.Truthy((Object)(object)val) && val.hero.id == hero)
			{
				return val;
			}
		}
		return null;
	}

	private static bool Conflicts(int hero)
	{
		if (hostMoveTimes.TryGetValue(hero, out var value))
		{
			return Time.unscaledTime - value <= 0.3f;
		}
		return false;
	}

	private static void ApplyIntent(Command command)
	{
		EnglishSquad val = FindSquad(command.Hero);
		if (!global::UnityObjectOps.Truthy((Object)(object)val) || ((Squad)val).dead || !val.navSpotOccupant.canMove)
		{
			return;
		}
		EnglishSquad val2 = ((command.Kind == 1) ? FindSquad(command.Target) : null);
		if (Conflicts(command.Hero) || (global::UnityObjectOps.Truthy((Object)(object)val2) && Conflicts(val2.hero.id)))
		{
			BroadcastFacts();
			return;
		}
		List<NavSpot> navSpots = ((IslandComponent)((Squad)val).faction).island.navSpotter.navSpots;
		if ((command.Kind == 0 && (command.Target < 0 || command.Target >= navSpots.Count)) || (command.Kind == 1 && (!global::UnityObjectOps.Truthy((Object)(object)val2) || (Object)(object)val2 == (Object)(object)val || ((Squad)val2).dead || !val2.navSpotOccupant.canMove || !global::UnityObjectOps.Truthy((Object)(object)val.navSpotOccupant.navSpot) || !global::UnityObjectOps.Truthy((Object)(object)val2.navSpotOccupant.navSpot))))
		{
			return;
		}
		bool applyingRemote = Session.ApplyingRemote;
		Session.ApplyingRemote = true;
		applyingHostIntent = true;
		try
		{
			if (command.Kind == 1)
			{
				NavSpot navSpot = val.navSpotOccupant.navSpot;
				NavSpot navSpot2 = val2.navSpotOccupant.navSpot;
				val2.navSpotOccupant.SetNavSpot(navSpot, true);
				val.navSpotOccupant.SetNavSpot(navSpot2, true);
			}
			else
			{
				SquadMover.MoveToDirect(val, navSpots[command.Target], true);
			}
		}
		finally
		{
			Session.ApplyingRemote = applyingRemote;
			applyingHostIntent = false;
		}
		BroadcastFacts();
		Plugin.Log.LogInfo((object)("[指令] 主机执行客户端" + ((command.Kind == 1) ? "换位" : "移动") + "，战斗 " + epoch + "，英雄 " + command.Hero));
	}

	private static void ApplyFacts(Command command)
	{
		bool applyingRemote = Session.ApplyingRemote;
		Session.ApplyingRemote = true;
		try
		{
			for (int i = 0; i < command.Heroes.Length; i++)
			{
				EnglishSquad val = FindSquad(command.Heroes[i]);
				if (global::UnityObjectOps.Truthy((Object)(object)val))
				{
					List<NavSpot> navSpots = ((IslandComponent)((Squad)val).faction).island.navSpotter.navSpots;
					int num = command.Spots[i];
					if (num >= navSpots.Count)
					{
						throw new IOException("主机导航格子不存在");
					}
					ClientSquadFacts.ApplyCommandOccupancy(val, num, command.Time, command.Revisions[i], ((IslandComponent)((Squad)val).faction).island);
				}
			}
		}
		finally
		{
			Session.ApplyingRemote = applyingRemote;
		}
	}

	public static void TrackLocalOrder(EnglishSquad squad, NavSpotController other = null)
	{
		if (Session.IsHost && !Session.ApplyingRemote && InLevel() && !CampaignSync.ClosingBattle)
		{
			EnsureBattle();
			hostMoveTimes[squad.hero.id] = Time.unscaledTime;
			if (global::UnityObjectOps.Truthy((Object)(object)other) && global::UnityObjectOps.Truthy((Object)(object)other.enSquad))
			{
				hostMoveTimes[other.enSquad.hero.id] = Time.unscaledTime;
			}
		}
	}

	public static void BeginOperation()
	{
		if (Session.IsHost && InLevel() && !CampaignSync.ClosingBattle)
		{
			EnsureBattle();
			operationDepth++;
		}
	}

	public static void EndOperation()
	{
		if (operationDepth > 0)
		{
			operationDepth--;
			if (operationDepth == 0 && dirty)
			{
				dirty = false;
				BroadcastFacts();
			}
		}
	}

	public static void AfterSetNavSpot(bool changed)
	{
		if (changed && Session.IsHost && !applyingHostIntent && InLevel() && !CampaignSync.ClosingBattle)
		{
			EnsureBattle();
			if (operationDepth > 0)
			{
				dirty = true;
			}
			else
			{
				BroadcastFacts();
			}
		}
	}

	private static void BroadcastFacts()
	{
		if (!Session.IsHost || !InLevel() || CampaignSync.ClosingBattle)
		{
			return;
		}
		List<int> list = new List<int>();
		List<int> list2 = new List<int>();
		List<int> list3 = new List<int>();
		foreach (Squad squad in Singleton<SquadSelector>.instance.squads)
		{
			EnglishSquad val = (EnglishSquad)(object)((squad is EnglishSquad) ? squad : null);
			if (global::UnityObjectOps.Truthy((Object)(object)val) && global::UnityObjectOps.Truthy((Object)(object)val.navSpotOccupant))
			{
				list.Add(val.hero.id);
				list3.Add(ClientSquadFacts.TargetRevision(val));
				NavSpot navSpot = val.navSpotOccupant.navSpot;
				list2.Add(global::UnityObjectOps.Truthy((Object)(object)navSpot) ? navSpot.index : (-1));
			}
		}
		if (list.Count != 0 && list.Count <= 256)
		{
			Session.Send(Write(new Command
			{
				Epoch = epoch,
				Level = Profile.campaign.lastPlayedLevelIdx,
				Kind = 2,
				Sequence = ++sequence,
				Time = AgentAuthority.HostTime,
				Heroes = list.ToArray(),
				Spots = list2.ToArray(),
				Revisions = list3.ToArray()
			}));
		}
	}

	private static bool InLevel()
	{
		Stack instance = Singleton<Stack>.instance;
		if (global::UnityObjectOps.Truthy((Object)(object)instance) && instance.stateLevel.active)
		{
			return Profile.campaign != null;
		}
		return false;
	}
}
