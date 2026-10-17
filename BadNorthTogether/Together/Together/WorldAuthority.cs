using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Together.Net;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;

namespace Together;

internal static class WorldAuthority
{
	private sealed class Frame
	{
		public float Time;

		public readonly List<ShipFrame> Ships = new List<ShipFrame>();

		public readonly List<HouseFrame> Houses = new List<HouseFrame>();
	}

	private sealed class ShipCallbacks
	{
		public Action Arrival;

		public Action Exit;
	}

	private struct ShipFrame
	{
		public int Id;

		public uint Signature;

		public Vector3 Position;

		public Quaternion Rotation;

		public float Interpolator;

		public float AnimationTime;

		public int Animation;

		public int Vikings;

		public bool Active;

		public bool Landed;

		public bool Outgoing;

		public bool EvacAvailable;
	}

	private struct HouseFrame
	{
		public float Fire;

		public float Smooth;

		public float AnimationTime;

		public int Coins;

		public int Animation;

		public bool Burning;

		public bool Destroying;

		public bool Pillaged;

		public bool ReplenishAvailable;
	}

	[HarmonyPatch(typeof(Longship), "Setup")]
	private static class ShipSetupPatch
	{
		private static void Postfix(Longship __instance)
		{
			RegisterShip(__instance);
		}
	}

	[HarmonyPatch(typeof(Longship), "Update")]
	private static class ShipUpdatePatch
	{
		private static bool Prefix(Longship __instance)
		{
			if (!OwnsShip(__instance))
			{
				return true;
			}
			IEnumerator enumerator = (IEnumerator)spawnRoutine.GetValue(__instance);
			if (enumerator != null && !enumerator.MoveNext())
			{
				spawnRoutine.SetValue(__instance, null);
			}
			return false;
		}
	}

	[HarmonyPatch(typeof(Longship), "Explode")]
	private static class ShipImpactPatch
	{
		private static bool Prefix(Longship __instance)
		{
			return !OwnsShip(__instance);
		}
	}

	[HarmonyPatch(typeof(Longship), "LandAnimComplete")]
	private static class ShipLandPatch
	{
		private static bool Prefix(Longship __instance)
		{
			return !OwnsShip(__instance);
		}
	}

	[HarmonyPatch(typeof(Longship), "Launch")]
	private static class ShipLaunchPatch
	{
		private static bool Prefix(Longship __instance)
		{
			return !OwnsShip(__instance);
		}
	}

	[HarmonyPatch(typeof(Longship), "get_numVikings")]
	private static class ShipCountPatch
	{
		private static bool Prefix(Longship __instance, ref int __result)
		{
			if (!AgentAuthority.ClientPlayback || !vikingCounts.TryGetValue(__instance, out var value))
			{
				return true;
			}
			__result = value;
			return false;
		}
	}

	[HarmonyPatch(typeof(SquadReplenishLocation), "get_available")]
	private static class ReplenishAvailablePatch
	{
		private static bool Prefix(SquadReplenishLocation __instance, ref bool __result)
		{
			if (!AgentAuthority.ClientPlayback || !replenishAvailability.TryGetValue(__instance, out var value))
			{
				return true;
			}
			__result = value;
			return false;
		}
	}

	[HarmonyPatch(typeof(SquadEvacuationLocation), "IsAvailable")]
	private static class EvacAvailablePatch
	{
		private static bool Prefix(SquadEvacuationLocation __instance, float requiredArea, ref bool __result)
		{
			if (!AgentAuthority.ClientPlayback || !evacuationAvailability.TryGetValue(__instance, out var value))
			{
				return true;
			}
			__result = value && __instance.area > requiredArea;
			return false;
		}
	}

	[HarmonyPatch(typeof(House), "Update")]
	private static class HouseUpdatePatch
	{
		private static bool Prefix()
		{
			return !AgentAuthority.ClientPlayback;
		}
	}

	[HarmonyPatch(typeof(House), "TorchLand")]
	private static class HouseTorchPatch
	{
		private static bool Prefix()
		{
			return !AgentAuthority.ClientPlayback;
		}
	}

	[HarmonyPatch(typeof(House), "Done")]
	private static class HouseDonePatch
	{
		private static bool Prefix()
		{
			return !AgentAuthority.ClientPlayback;
		}
	}

	private const byte Schema = 2;

	private static Island island;

	private static bool initialized;

	private static float nextSnapshot;

	private static readonly Dictionary<int, Longship> ships = new Dictionary<int, Longship>();

	private static readonly HashSet<Longship> departedShips = new HashSet<Longship>();

	private static readonly HashSet<int> arrivedShipIds = new HashSet<int>();

	private static readonly Dictionary<int, float> pendingArrivals = new Dictionary<int, float>();

	private static readonly Dictionary<Longship, ShipCallbacks> watchedShips = new Dictionary<Longship, ShipCallbacks>();

	private static readonly Dictionary<int, float> missingShips = new Dictionary<int, float>();

	private static readonly Dictionary<int, float> pendingDepartures = new Dictionary<int, float>();

	private static readonly RetiredIds retiredShipIds = new RetiredIds();

	private static readonly Dictionary<Longship, int> vikingCounts = new Dictionary<Longship, int>();

	private static readonly Dictionary<SquadReplenishLocation, bool> replenishAvailability = new Dictionary<SquadReplenishLocation, bool>();

	private static readonly Dictionary<SquadEvacuationLocation, bool> evacuationAvailability = new Dictionary<SquadEvacuationLocation, bool>();

	private static readonly List<Frame> frames = new List<Frame>();

	private static readonly Dictionary<Animator, bool> animatorEnabled = new Dictionary<Animator, bool>();

	private static readonly FieldInfo spawnRoutine = AccessTools.Field(typeof(Longship), "spawnRoutine");

	private static readonly FieldInfo shipInterpolator = AccessTools.Field(typeof(Longship), "<interpolator>k__BackingField");

	private static readonly FieldInfo shipOutgoing = AccessTools.Field(typeof(Longship), "outgoing");

	private static readonly FieldInfo shipExitEvent = AccessTools.Field(typeof(Longship), "onShipExit");

	private static readonly FieldInfo shipArrivalEvent = AccessTools.Field(typeof(Longship), "onShipArrival");

	private static readonly FieldInfo fireField = AccessTools.Field(typeof(House), "<fire>k__BackingField");

	private static readonly FieldInfo smoothFireField = AccessTools.Field(typeof(House), "smoothFire");

	private static readonly FieldInfo windFireField = AccessTools.Field(typeof(House), "windFire");

	private static readonly FieldInfo houseLightField = AccessTools.Field(typeof(House), "houseLight");

	private static readonly MethodInfo shipUpdatePosition = AccessTools.Method(typeof(Longship), "UpdatePosition", new Type[1] { typeof(Vector3) }, (Type[])null);

	public static void Initialize()
	{
		if (!initialized)
		{
			if (shipExitEvent == null || shipArrivalEvent == null)
			{
				throw new MissingFieldException(typeof(Longship).FullName, "船舶生命周期事件");
			}
			initialized = true;
			Session.On(MsgType.WorldSnapshot, Receive);
			Session.On(MsgType.ShipLifecycle, ReceiveShipLifecycle);
			Session.Stopped += Reset;
			ProjectilePlayback.Initialize();
			HostRaidManifest.Initialize();
		}
	}

	public static void BeginIsland(Island value)
	{
		Reset();
		island = value;
		RaidIdentity.BeginIsland(value);
		HostRaidManifest.BeginIsland(value);
		Longship[] componentsInChildren = ((Component)island).GetComponentsInChildren<Longship>(true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			RegisterShip(componentsInChildren[i]);
		}
		ProjectilePlayback.BeginIsland(value);
	}

	public static void Reset()
	{
		foreach (KeyValuePair<Longship, ShipCallbacks> watchedShip in watchedShips)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)watchedShip.Key))
			{
				watchedShip.Key.onShipArrival -= watchedShip.Value.Arrival;
				watchedShip.Key.onShipExit -= watchedShip.Value.Exit;
			}
		}
		watchedShips.Clear();
		pendingArrivals.Clear();
		arrivedShipIds.Clear();
		foreach (KeyValuePair<Animator, bool> item in animatorEnabled)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)item.Key))
			{
				((Behaviour)item.Key).enabled = item.Value;
			}
		}
		animatorEnabled.Clear();
		island = null;
		ships.Clear();
		frames.Clear();
		vikingCounts.Clear();
		replenishAvailability.Clear();
		evacuationAvailability.Clear();
		nextSnapshot = 0f;
		departedShips.Clear();
		missingShips.Clear();
		pendingDepartures.Clear();
		retiredShipIds.Clear();
		RaidIdentity.Reset();
		HostRaidManifest.Reset();
		ProjectilePlayback.Reset();
	}

	internal static bool IsShipRetired(int id)
	{
		if (!retiredShipIds.Contains(id))
		{
			return RaidIdentity.IsShipRetired(id);
		}
		return true;
	}

	public static int ShipId(Longship ship)
	{
		return RaidIdentity.GetShipId(ship);
	}

	public static Longship FindShip(int id)
	{
		if (IsShipRetired(id))
		{
			return null;
		}
		if (ships.TryGetValue(id, out var value) && global::UnityObjectOps.Truthy((Object)(object)value))
		{
			return value;
		}
		if (!global::UnityObjectOps.Truthy((Object)(object)island))
		{
			return null;
		}
		Longship[] componentsInChildren = ((Component)island).GetComponentsInChildren<Longship>(true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			RegisterShip(componentsInChildren[i]);
		}
		if (!ships.TryGetValue(id, out value))
		{
			return null;
		}
		return value;
	}

	private static void RegisterShip(Longship ship)
	{
		if (!Session.Active || !global::UnityObjectOps.Truthy((Object)(object)ship))
		{
			return;
		}
		int num = ShipId(ship);
		if (num < 0 || IsShipRetired(num))
		{
			return;
		}
		if (ships.TryGetValue(num, out var value) && global::UnityObjectOps.Truthy((Object)(object)value) && (Object)(object)value != (Object)(object)ship)
		{
			throw new IOException("同一稳定船编号对应多条船：" + num);
		}
		ships[num] = ship;
		if (Session.IsHost && !watchedShips.ContainsKey(ship))
		{
			ShipCallbacks shipCallbacks = new ShipCallbacks
			{
				Arrival = delegate
				{
					AnnounceShipEvent(ship, 0);
				},
				Exit = delegate
				{
					AnnounceShipEvent(ship, 1);
				}
			};
			ship.onShipArrival += shipCallbacks.Arrival;
			ship.onShipExit += shipCallbacks.Exit;
			watchedShips.Add(ship, shipCallbacks);
		}
	}

	public static void Tick()
	{
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		if (!Session.Active || !global::UnityObjectOps.Truthy((Object)(object)island) || CampaignSync.ClosingBattle || AgentAuthority.HasFailure)
		{
			return;
		}
		RaidIdentity.Prune();
		PruneShips();
		HostRaidManifest.Tick();
		if (AgentAuthority.HasFailure)
		{
			return;
		}
		Longship[] componentsInChildren = ((Component)island).GetComponentsInChildren<Longship>(true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			RegisterShip(componentsInChildren[i]);
		}
		ProjectilePlayback.Tick();
		if (!Session.IsHost || !DeploySync.BattleStarted || Time.unscaledTime < nextSnapshot)
		{
			return;
		}
		nextSnapshot = Time.unscaledTime + 0.1f;
		using MessageWriter messageWriter = Protocol.Begin(MsgType.WorldSnapshot);
		messageWriter.Write((byte)2);
		messageWriter.Write(AgentAuthority.Epoch);
		messageWriter.Write(AgentAuthority.HostTime);
		List<Longship> list = new List<Longship>();
		foreach (Longship value in ships.Values)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)value) && (!global::UnityObjectOps.Truthy((Object)(object)value.landing.shipGroup) || HostRaidManifest.CanPublishWave(value.landing.shipGroup.wave)))
			{
				list.Add(value);
			}
		}
		messageWriter.Write(list.Count);
		foreach (Longship item in list)
		{
			messageWriter.Write(ShipId(item));
			messageWriter.Write(ShipSignature(item));
			WriteVector(messageWriter, ((Component)item).transform.position);
			WriteRotation(messageWriter, ((Component)item).transform.rotation);
			messageWriter.Write(item.interpolator);
			messageWriter.Write(((Component)item).gameObject.activeInHierarchy);
			messageWriter.Write(item.landed);
			messageWriter.Write((bool)shipOutgoing.GetValue(item));
			messageWriter.Write(item.numVikings);
			messageWriter.Write(global::UnityObjectOps.Truthy((Object)(object)item.evacuationLocation) && item.evacuationLocation.IsAvailable(0f));
			WriteAnimation(messageWriter, ((Component)item).GetComponent<Animator>());
		}
		House[] houses = island.village.houses;
		messageWriter.Write(houses.Length);
		House[] array = houses;
		foreach (House val in array)
		{
			messageWriter.Write(val.fire);
			messageWriter.Write((float)smoothFireField.GetValue(val));
			messageWriter.Write(val.coinCount);
			messageWriter.Write(val.burning.active);
			messageWriter.Write(val.destroying.active);
			messageWriter.Write(val.pillaged.active);
			SquadReplenishLocation component = ((Component)val).GetComponent<SquadReplenishLocation>();
			messageWriter.Write(global::UnityObjectOps.Truthy((Object)(object)component) && component.available);
			WriteAnimation(messageWriter, val.animator);
		}
		Session.Send(messageWriter.ToArray());
	}

	public static void LateTick()
	{
		//IL_02cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0300: Unknown result type (might be due to invalid IL or missing references)
		//IL_04a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_04aa: Expected O, but got Unknown
		if (AgentAuthority.HasFailure || CampaignSync.ClosingBattle)
		{
			return;
		}
		ProjectilePlayback.LateTick();
		if (!AgentAuthority.ClientPlayback || !global::UnityObjectOps.Truthy((Object)(object)island))
		{
			return;
		}
		ApplyArrivals();
		ApplyDepartures();
		if (frames.Count == 0)
		{
			return;
		}
		float playbackTime = AgentAuthority.PlaybackTime;
		while (frames.Count > 1 && frames[1].Time <= playbackTime)
		{
			frames.RemoveAt(0);
		}
		Frame frame = frames[0];
		if (frame.Time > playbackTime)
		{
			return;
		}
		Frame frame2 = ((frames.Count > 1) ? frames[1] : frame);
		float num = ((frame2.Time > frame.Time) ? Mathf.Clamp01((playbackTime - frame.Time) / (frame2.Time - frame.Time)) : 0f);
		foreach (ShipFrame ship in frame.Ships)
		{
			if (IsShipRetired(ship.Id))
			{
				continue;
			}
			Longship val = FindShip(ship.Id);
			if (!global::UnityObjectOps.Truthy((Object)(object)val))
			{
				if (!missingShips.TryGetValue(ship.Id, out var value))
				{
					missingShips[ship.Id] = Time.unscaledTime;
				}
				else if (Time.unscaledTime - value > 30f)
				{
					int id = ship.Id;
					AgentAuthority.Fail("等待主机船舶生成超时，稳定船编号 " + id);
				}
				continue;
			}
			missingShips.Remove(ship.Id);
			if (ShipSignature(val) != ship.Signature)
			{
				int id = ship.Id;
				AgentAuthority.Fail("船舶编成或登陆航线不一致，稳定船编号 " + id);
				return;
			}
			ShipFrame shipFrame = ship;
			foreach (ShipFrame ship2 in frame2.Ships)
			{
				if (ship2.Id == ship.Id)
				{
					shipFrame = ship2;
					break;
				}
			}
			if (((Component)val).gameObject.activeSelf != ship.Active)
			{
				((Component)val).gameObject.SetActive(ship.Active);
			}
			shipInterpolator.SetValue(val, Mathf.Lerp(ship.Interpolator, shipFrame.Interpolator, num));
			shipOutgoing.SetValue(val, ship.Outgoing);
			vikingCounts[val] = ship.Vikings;
			if (global::UnityObjectOps.Truthy((Object)(object)val.evacuationLocation))
			{
				evacuationAvailability[val.evacuationLocation] = ship.EvacAvailable;
			}
			val.landed = ship.Landed;
			shipUpdatePosition.Invoke(val, new object[1] { Vector3.Lerp(ship.Position, shipFrame.Position, num) });
			((Component)val).transform.rotation = Quaternion.Slerp(ship.Rotation, shipFrame.Rotation, num);
			PlayAnimation(((Component)val).GetComponent<Animator>(), ship.Animation, ship.AnimationTime);
			if (ship.Outgoing && !ship.Active && departedShips.Add(val))
			{
				if (shipExitEvent.GetValue(val) is Action action)
				{
					action();
				}
				retiredShipIds.Add(ship.Id);
			}
		}
		House[] houses = island.village.houses;
		if (houses.Length != frame.Houses.Count)
		{
			AgentAuthority.Fail("双方房屋数量不一致");
			return;
		}
		for (int i = 0; i < houses.Length; i++)
		{
			House val2 = houses[i];
			HouseFrame houseFrame = frame.Houses[i];
			fireField.SetValue(val2, houseFrame.Fire);
			smoothFireField.SetValue(val2, houseFrame.Smooth);
			val2.coinCount = houseFrame.Coins;
			SquadReplenishLocation component = ((Component)val2).GetComponent<SquadReplenishLocation>();
			if (global::UnityObjectOps.Truthy((Object)(object)component))
			{
				replenishAvailability[component] = houseFrame.ReplenishAvailable;
			}
			val2.pillaged.SetActive(houseFrame.Pillaged);
			val2.burning.SetActive(houseFrame.Burning && !houseFrame.Pillaged);
			val2.destroying.SetActive(houseFrame.Destroying && !houseFrame.Pillaged);
			WindSmoke val3 = (WindSmoke)windFireField.GetValue(val2);
			if (global::UnityObjectOps.Truthy((Object)(object)val3))
			{
				val3.SetMaterialProperty(Shader.PropertyToID("_Fire"), houseFrame.Smooth);
				((Component)val3).gameObject.SetActive(houseFrame.Smooth > 0f);
			}
			object value2 = houseLightField.GetValue(val2);
			if (value2 != null)
			{
				AccessTools.Method(value2.GetType(), "SetHouseBurning", (Type[])null, (Type[])null).Invoke(value2, new object[1] { houseFrame.Fire > 0f });
			}
			val2.anySmoothFire.SetActive(houseFrame.Smooth > 0f);
			val2.animator.SetFloat("Fire", houseFrame.Smooth);
			PlayAnimation(val2.animator, houseFrame.Animation, houseFrame.AnimationTime);
		}
	}

	private static void Receive(BinaryReader reader)
	{
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		byte b = reader.ReadByte();
		int num = reader.ReadInt32();
		float num2 = reader.ReadSingle();
		if (CampaignSync.ClosingBattle || !Session.IsClient || !global::UnityObjectOps.Truthy((Object)(object)island) || b != 2 || num != AgentAuthority.Epoch)
		{
			return;
		}
		if (float.IsNaN(num2) || float.IsInfinity(num2))
		{
			throw new IOException("场景时间无效");
		}
		Frame frame = new Frame
		{
			Time = num2
		};
		int num3 = reader.ReadInt32();
		if (num3 < 0 || num3 > 2048)
		{
			throw new IOException("船舶数量异常");
		}
		for (int i = 0; i < num3; i++)
		{
			ShipFrame item = new ShipFrame
			{
				Id = reader.ReadInt32(),
				Signature = reader.ReadUInt32(),
				Position = ReadVector(reader),
				Rotation = ReadRotation(reader),
				Interpolator = reader.ReadSingle(),
				Active = reader.ReadBoolean(),
				Landed = reader.ReadBoolean(),
				Outgoing = reader.ReadBoolean(),
				Vikings = reader.ReadInt32(),
				EvacAvailable = reader.ReadBoolean(),
				Animation = reader.ReadInt32(),
				AnimationTime = reader.ReadSingle()
			};
			frame.Ships.Add(item);
		}
		num3 = reader.ReadInt32();
		if (num3 < 0 || num3 > 1024)
		{
			throw new IOException("房屋数量异常");
		}
		for (int j = 0; j < num3; j++)
		{
			frame.Houses.Add(new HouseFrame
			{
				Fire = reader.ReadSingle(),
				Smooth = reader.ReadSingle(),
				Coins = reader.ReadInt32(),
				Burning = reader.ReadBoolean(),
				Destroying = reader.ReadBoolean(),
				Pillaged = reader.ReadBoolean(),
				ReplenishAvailable = reader.ReadBoolean(),
				Animation = reader.ReadInt32(),
				AnimationTime = reader.ReadSingle()
			});
		}
		if (frames.Count <= 0 || !(num2 <= frames[frames.Count - 1].Time))
		{
			frames.Add(frame);
			if (frames.Count > 64)
			{
				AgentAuthority.Fail("场景播放积压超过缓冲上限");
				frames.RemoveAt(0);
			}
		}
	}

	private static void AnnounceShipEvent(Longship ship, byte action)
	{
		if (CampaignSync.ClosingBattle || !Session.IsHost || !DeploySync.BattleStarted || !global::UnityObjectOps.Truthy((Object)(object)ship))
		{
			return;
		}
		int num = ShipId(ship);
		if (num < 0 || ((action == 0) ? (!arrivedShipIds.Add(num)) : (!departedShips.Add(ship))))
		{
			return;
		}
		using (MessageWriter messageWriter = Protocol.Begin(MsgType.ShipLifecycle))
		{
			messageWriter.Write((byte)2);
			messageWriter.Write(AgentAuthority.Epoch);
			messageWriter.Write(action);
			messageWriter.Write(num);
			messageWriter.Write(AgentAuthority.HostTime);
			if (!Session.Send(messageWriter.ToArray()))
			{
				AgentAuthority.Fail("船舶抵岸/离场事实发送失败");
			}
		}
		if (action == 1)
		{
			retiredShipIds.Add(num);
			ReleaseShip(num, ship);
		}
	}

	private static void ReceiveShipLifecycle(BinaryReader reader)
	{
		byte b = reader.ReadByte();
		int num = reader.ReadInt32();
		byte b2 = reader.ReadByte();
		int num2 = reader.ReadInt32();
		float num3 = reader.ReadSingle();
		reader.RequireEnd();
		if (CampaignSync.ClosingBattle || !Session.IsClient || !global::UnityObjectOps.Truthy((Object)(object)island) || b != 2 || num != AgentAuthority.Epoch)
		{
			return;
		}
		if (b2 > 1 || num2 < 0 || num3 < 0f || float.IsNaN(num3) || float.IsInfinity(num3))
		{
			throw new IOException("船舶生命周期消息无效");
		}
		if (IsShipRetired(num2))
		{
			return;
		}
		if (b2 == 0)
		{
			if (!arrivedShipIds.Contains(num2))
			{
				pendingArrivals[num2] = num3;
			}
		}
		else
		{
			pendingDepartures[num2] = num3;
		}
	}

	private static void ApplyArrivals()
	{
		List<int> list = new List<int>();
		foreach (KeyValuePair<int, float> pendingArrival in pendingArrivals)
		{
			if (IsShipRetired(pendingArrival.Key) || arrivedShipIds.Contains(pendingArrival.Key))
			{
				list.Add(pendingArrival.Key);
			}
			else
			{
				if (pendingArrival.Value > AgentAuthority.PlaybackTime)
				{
					continue;
				}
				Longship val = FindShip(pendingArrival.Key);
				if (global::UnityObjectOps.Truthy((Object)(object)val))
				{
					arrivedShipIds.Add(pendingArrival.Key);
					if (shipArrivalEvent.GetValue(val) is Action action)
					{
						action();
					}
					list.Add(pendingArrival.Key);
				}
			}
		}
		foreach (int item in list)
		{
			pendingArrivals.Remove(item);
		}
	}

	private static void ApplyDepartures()
	{
		List<int> list = new List<int>();
		foreach (KeyValuePair<int, float> pendingDeparture in pendingDepartures)
		{
			if (IsShipRetired(pendingDeparture.Key))
			{
				list.Add(pendingDeparture.Key);
			}
			else
			{
				if (pendingDeparture.Value > AgentAuthority.PlaybackTime)
				{
					continue;
				}
				Longship val = FindShip(pendingDeparture.Key);
				if (!global::UnityObjectOps.Truthy((Object)(object)val))
				{
					continue;
				}
				if (departedShips.Add(val))
				{
					if (shipExitEvent.GetValue(val) is Action action)
					{
						action();
					}
					((Component)val).gameObject.SetActive(false);
				}
				retiredShipIds.Add(pendingDeparture.Key);
				ReleaseShip(pendingDeparture.Key, val);
				list.Add(pendingDeparture.Key);
			}
		}
		foreach (int item in list)
		{
			pendingDepartures.Remove(item);
		}
	}

	private static void ReleaseShip(int id, Longship ship)
	{
		ships.Remove(id);
		missingShips.Remove(id);
		pendingArrivals.Remove(id);
		arrivedShipIds.Remove(id);
		if (ship == null)
		{
			return;
		}
		if (watchedShips.TryGetValue(ship, out var value))
		{
			if (global::UnityObjectOps.Truthy((Object)(object)ship))
			{
				ship.onShipArrival -= value.Arrival;
				ship.onShipExit -= value.Exit;
			}
			watchedShips.Remove(ship);
		}
		departedShips.Remove(ship);
		vikingCounts.Remove(ship);
		if (global::UnityObjectOps.Truthy((Object)(object)ship))
		{
			if (global::UnityObjectOps.Truthy((Object)(object)ship.evacuationLocation))
			{
				evacuationAvailability.Remove(ship.evacuationLocation);
			}
			Animator component = ((Component)ship).GetComponent<Animator>();
			if (global::UnityObjectOps.Truthy((Object)(object)component))
			{
				animatorEnabled.Remove(component);
			}
		}
	}

	private static void PruneShips()
	{
		foreach (int item in new List<int>(ships.Keys))
		{
			if (RaidIdentity.IsShipRetired(item))
			{
				retiredShipIds.AddRange(item >> 14 << 14, (item >> 14 << 14) | 0x3FFF);
			}
			if (!global::UnityObjectOps.Truthy((Object)(object)ships[item]) || IsShipRetired(item))
			{
				ReleaseShip(item, ships[item]);
			}
		}
		foreach (Longship item2 in new List<Longship>(watchedShips.Keys))
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)item2))
			{
				watchedShips.Remove(item2);
			}
		}
		foreach (Longship item3 in new List<Longship>(vikingCounts.Keys))
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)item3))
			{
				vikingCounts.Remove(item3);
			}
		}
		foreach (Longship item4 in new List<Longship>(departedShips))
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)item4))
			{
				departedShips.Remove(item4);
			}
		}
		foreach (Animator item5 in new List<Animator>(animatorEnabled.Keys))
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)item5))
			{
				animatorEnabled.Remove(item5);
			}
		}
	}

	internal static void ReleaseWave(int wave)
	{
		retiredShipIds.AddRange(wave << 14, (wave << 14) | 0x3FFF);
		foreach (int item in new List<int>(ships.Keys))
		{
			if (item < 1073741824 && item >> 14 == wave)
			{
				ReleaseShip(item, ships[item]);
			}
		}
	}

	internal static void WriteVector(BinaryWriter writer, Vector3 value)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		writer.Write(value.x);
		writer.Write(value.y);
		writer.Write(value.z);
	}

	internal static Vector3 ReadVector(BinaryReader reader)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
	}

	internal static void WriteRotation(BinaryWriter writer, Quaternion value)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		writer.Write(value.x);
		writer.Write(value.y);
		writer.Write(value.z);
		writer.Write(value.w);
	}

	internal static Quaternion ReadRotation(BinaryReader reader)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
	}

	private static bool OwnsShip(Longship ship)
	{
		if (AgentAuthority.ClientPlayback)
		{
			return ShipId(ship) >= 0;
		}
		return false;
	}

	private static uint ShipSignature(Longship ship)
	{
		if (global::UnityObjectOps.Truthy((Object)(object)ship.landing.shipGroup))
		{
			return RaidIdentity.LandingSignature(ship.landing);
		}
		return (uint)ShipId(ship);
	}

	private static void WriteAnimation(BinaryWriter writer, Animator animator)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		AnimatorStateInfo val = (AnimatorStateInfo)((global::UnityObjectOps.Truthy((Object)(object)animator) && global::UnityObjectOps.Truthy((Object)(object)animator.runtimeAnimatorController)) ? animator.GetCurrentAnimatorStateInfo(0) : default(AnimatorStateInfo));
		writer.Write(val.fullPathHash);
		writer.Write(val.normalizedTime);
	}

	private static void PlayAnimation(Animator animator, int state, float normalized)
	{
		if (global::UnityObjectOps.Truthy((Object)(object)animator) && global::UnityObjectOps.Truthy((Object)(object)animator.runtimeAnimatorController) && state != 0 && ((Component)animator).gameObject.activeInHierarchy)
		{
			if (!animatorEnabled.ContainsKey(animator))
			{
				animatorEnabled[animator] = ((Behaviour)animator).enabled;
			}
			((Behaviour)animator).enabled = false;
			animator.Play(state, 0, normalized);
			animator.Update(0f);
		}
	}
}
