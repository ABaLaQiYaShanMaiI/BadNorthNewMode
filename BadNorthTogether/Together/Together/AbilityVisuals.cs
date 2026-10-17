using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.Upgrades;

namespace Together;

internal static class AbilityVisuals
{
	private sealed class Frame
	{
		public ActiveAbility Ability;

		public AbilityWire.State State;
	}

	private sealed class Aura
	{
		public ActiveAbility Ability;

		public SquadParticleDistributer Template;

		public GameObject Root;

		public ParticleSystem[] Particles;

		public float Next;

		public float Time;

		public float End;

		public bool Emitting;

		public int Index;
	}

	private static readonly VisualEventQueue<Frame> queue = new VisualEventQueue<Frame>();

	private static readonly Dictionary<string, AbilityWire.State> shown = new Dictionary<string, AbilityWire.State>();

	private static readonly Dictionary<string, AbilityWire.State> queued = new Dictionary<string, AbilityWire.State>();

	private static readonly Dictionary<string, Aura> auras = new Dictionary<string, Aura>();

	private static GameObject staging;

	private static bool subscribed;

	private static readonly FieldInfo completeAudio = AccessTools.Field(typeof(ActiveAbility), "cooldownCompleteAudioID");

	internal static void Enqueue(ActiveAbility ability, AbilityWire.State state)
	{
		if (!subscribed)
		{
			subscribed = true;
			Session.OptionalStopped += Reset;
		}
		if (Session.OptionalEnabled && !CampaignSync.ClosingBattle && (!queued.TryGetValue(state.Key, out var value) || value.Active != state.Active || value.Activations != state.Activations || value.Cooldowns != state.Cooldowns || value.EvacState != state.EvacState || (state.Active && !(Mathf.Abs(value.EffectEnd - state.EffectEnd) < 0.001f))))
		{
			if (!queue.Add(new Frame
			{
				Ability = ability,
				State = state
			}, state.Key, state.Time, Time.unscaledTime))
			{
				SceneVisuals.Warn("ability-queue", "技能显示队列已满");
			}
			else
			{
				queued[state.Key] = state;
			}
		}
	}

	internal static void Reset()
	{
		queue.Clear();
		shown.Clear();
		queued.Clear();
		foreach (Aura value in auras.Values)
		{
			if (global::UnityObjectOps.Truthy((Object)(object)value.Root))
			{
				Object.Destroy((Object)(object)value.Root);
			}
		}
		auras.Clear();
		if (global::UnityObjectOps.Truthy((Object)(object)staging))
		{
			Object.Destroy((Object)(object)staging);
		}
		staging = null;
	}

	internal static void LateTick()
	{
		//IL_01c9: Unknown result type (might be due to invalid IL or missing references)
		if (!Session.OptionalEnabled || CampaignSync.ClosingBattle)
		{
			Reset();
		}
		else
		{
			if (AgentAuthority.HasFailure)
			{
				return;
			}
			float playbackTime = AgentAuthority.PlaybackTime;
			queue.Process(playbackTime, Time.unscaledTime, delegate(Frame frame)
			{
				if (!global::UnityObjectOps.Truthy((Object)(object)frame.Ability) || !global::UnityObjectOps.Truthy((Object)(object)((UpgradeComponent)frame.Ability).squad))
				{
					return true;
				}
				try
				{
					Apply(frame.Ability, frame.State);
				}
				catch (Exception ex)
				{
					SceneVisuals.Warn("ability-apply", "技能显示降级：" + ex.GetBaseException().Message);
				}
				return true;
			}, delegate
			{
				SceneVisuals.Warn("ability-wait", "技能显示等待超时");
			});
			foreach (string item in new List<string>(auras.Keys))
			{
				Aura aura = auras[item];
				if (aura.Emitting && playbackTime >= aura.End)
				{
					aura.Emitting = false;
					aura.End = playbackTime + 5f;
				}
				if (!global::UnityObjectOps.Truthy((Object)(object)aura.Root) || !global::UnityObjectOps.Truthy((Object)(object)aura.Ability) || !global::UnityObjectOps.Truthy((Object)(object)((UpgradeComponent)aura.Ability).squad) || !((Squad)((UpgradeComponent)aura.Ability).squad).alive || (!aura.Emitting && playbackTime >= aura.End))
				{
					if (global::UnityObjectOps.Truthy((Object)(object)aura.Root))
					{
						Object.Destroy((Object)(object)aura.Root);
					}
					auras.Remove(item);
					continue;
				}
				float num = Mathf.Max(0f, playbackTime - aura.Time);
				if (num <= 0f)
				{
					continue;
				}
				ParticleSystem[] particles;
				if (aura.Emitting && playbackTime >= aura.Next)
				{
					List<Agent> livingAgents = ((Squad)((UpgradeComponent)aura.Ability).squad).livingAgents;
					if (livingAgents.Count > 0)
					{
						Agent val = livingAgents[aura.Index++ % livingAgents.Count];
						if (global::UnityObjectOps.Truthy((Object)(object)val))
						{
							aura.Root.transform.position = val.chestPos;
							particles = aura.Particles;
							for (int num2 = 0; num2 < particles.Length; num2++)
							{
								particles[num2].Emit(1);
							}
						}
					}
					float num3 = (float)AccessTools.Field(typeof(SquadParticleDistributer), "frequencyPerAgent").GetValue(aura.Template);
					float num4 = (float)AccessTools.Field(typeof(SquadParticleDistributer), "maxFrequency").GetValue(aura.Template);
					aura.Next = playbackTime + 1f / Mathf.Max(0.1f, Mathf.Min(num4, num3 * (float)livingAgents.Count));
				}
				particles = aura.Particles;
				foreach (ParticleSystem obj in particles)
				{
					obj.Simulate(num, false, false, false);
					obj.Pause(false);
				}
				aura.Time = playbackTime;
			}
		}
	}

	private static void Apply(ActiveAbility ability, AbilityWire.State state)
	{
		bool num = shown.TryGetValue(state.Key, out var value);
		if (num && state.Activations > value.Activations && !string.IsNullOrEmpty(ability.actionConfirmAudioID))
		{
			FabricWrapper.PostEvent(ability.actionConfirmAudioID);
		}
		if (num && state.Cooldowns > value.Cooldowns)
		{
			FloatingIcon(ability);
			string text = completeAudio.GetValue(ability) as string;
			if (!string.IsNullOrEmpty(text) && global::UnityObjectOps.Truthy((Object)(object)((UpgradeComponent)ability).squad.standard))
			{
				FabricWrapper.PostEvent(text, ((Component)((UpgradeComponent)ability).squad.standard).gameObject);
			}
		}
		if (num && state.EvacState >= 3 && value.EvacState < 3 && ability is EvacuateAbility)
		{
			FloatingIcon(ability);
			FabricWrapper.PostEvent("UI/InGame/UpgradePing", ((Component)((UpgradeComponent)ability).squad.heroAgent).gameObject);
		}
		shown[state.Key] = state;
		if (!(ability is InvulnerableAbility))
		{
			return;
		}
		Aura value2;
		if (state.Active)
		{
			if (!auras.TryGetValue(state.Key, out value2))
			{
				value2 = CreateAura(ability);
				if (value2 == null)
				{
					return;
				}
				auras[state.Key] = value2;
			}
			value2.Emitting = true;
			value2.End = state.EffectEnd;
		}
		else if (auras.TryGetValue(state.Key, out value2))
		{
			value2.Emitting = false;
			value2.End = state.Time + 5f;
		}
	}

	private static void FloatingIcon(ActiveAbility ability)
	{
		if (global::UnityObjectOps.Truthy((Object)(object)ability.iconSprite) && global::UnityObjectOps.Truthy((Object)(object)((UpgradeComponent)ability).squad.heroAgent) && global::UnityObjectOps.Truthy((Object)(object)ScriptableObjectSingleton<PrefabManager>.instance))
		{
			((SelfPoolingPrefab)ScriptableObjectSingleton<PrefabManager>.instance.abilityFloatingIcon).GetInstance<FloatingIcon>().Setup(ability.iconSprite, ((Component)((UpgradeComponent)ability).squad.heroAgent).transform);
		}
	}

	private static Aura CreateAura(ActiveAbility ability)
	{
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Expected O, but got Unknown
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0164: Unknown result type (might be due to invalid IL or missing references)
		//IL_017f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0184: Unknown result type (might be due to invalid IL or missing references)
		if (auras.Count >= 32)
		{
			SceneVisuals.Warn("ability-aura-budget", "技能光环数量已满");
			return null;
		}
		SquadParticleDistributer component = ((Component)ability).GetComponent<SquadParticleDistributer>();
		if (!global::UnityObjectOps.Truthy((Object)(object)component) || !global::UnityObjectOps.Truthy((Object)(object)component.agentParticle))
		{
			return null;
		}
		if (!global::UnityObjectOps.Truthy((Object)(object)staging))
		{
			staging = new GameObject("Together_AbilityParticles");
			staging.SetActive(false);
		}
		GameObject val = Object.Instantiate<GameObject>(((Component)component.agentParticle).gameObject, staging.transform, false);
		try
		{
			val.SetActive(false);
			((Object)val).name = "Together_AbilityAura";
			MonoBehaviour[] componentsInChildren = val.GetComponentsInChildren<MonoBehaviour>(true);
			foreach (MonoBehaviour obj in componentsInChildren)
			{
				((Behaviour)obj).enabled = false;
				Object.DestroyImmediate((Object)(object)obj);
			}
			Component[] componentsInChildren2 = val.GetComponentsInChildren<Component>(true);
			foreach (Component val2 in componentsInChildren2)
			{
				if (global::UnityObjectOps.Truthy((Object)(object)val2) && !FireVisualPolicy.KeepComponent(((object)val2).GetType()))
				{
					Object.DestroyImmediate((Object)(object)val2);
				}
			}
			ParticleSystem[] componentsInChildren3 = val.GetComponentsInChildren<ParticleSystem>(true);
			if (componentsInChildren3.Length == 0)
			{
				Object.Destroy((Object)(object)val);
				SceneVisuals.Warn("ability-aura-template", "原生无敌模板没有可复用的纯粒子");
				return null;
			}
			val.AddComponent<LocalVisualObject>();
			val.transform.SetParent(((Component)AbilityCommands.CurrentIsland).transform, false);
			val.SetActive(true);
			ParticleSystem[] array = componentsInChildren3;
			foreach (ParticleSystem obj2 in array)
			{
				obj2.Stop(false, (ParticleSystemStopBehavior)0);
				ParticleSystem.MainModule main = obj2.main;
				main.loop = false;
				main.playOnAwake = false;
				main.simulationSpace = (ParticleSystemSimulationSpace)1;
				ParticleSystem.EmissionModule emission = obj2.emission;
				emission.enabled = false;
				obj2.Play(false);
				obj2.Pause(false);
			}
			return new Aura
			{
				Ability = ability,
				Template = component,
				Root = val,
				Particles = componentsInChildren3,
				Time = AgentAuthority.PlaybackTime,
				Next = AgentAuthority.PlaybackTime
			};
		}
		catch
		{
			Object.Destroy((Object)(object)val);
			throw;
		}
	}
}
