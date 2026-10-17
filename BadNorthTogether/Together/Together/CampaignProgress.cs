using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.CampaignGeneration;
using Voxels.TowerDefense.HeroGeneration;
using Voxels.TowerDefense.ProfileInternals;
using Voxels.TowerDefense.UI;
using Voxels.TowerDefense.UI.UpgradeScreen;

namespace Together;

internal static class CampaignProgress
{
	private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private static readonly string[] mutableLevel = new string[9] { "playCount", "coinCount", "goodSeed", "hasSprite", "_unlocked", "houses", "checkpointState", "seed", "coinTarget" };

	private static CampaignSave gameOverShown;

	public static bool Compatible(CampaignSave current, CampaignSave update)
	{
		return Incompatibility(current, update) == null;
	}

	public static string Incompatibility(CampaignSave current, CampaignSave update)
	{
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0241: Unknown result type (might be due to invalid IL or missing references)
		//IL_0248: Unknown result type (might be due to invalid IL or missing references)
		if (current == null || update == null || current.seed != update.seed || current.serializedVersion != update.serializedVersion || current.levelStates == null || update.levelStates == null || current.levelStates.Count != update.levelStates.Count || current.heroes == null || update.heroes == null || current.heroes.Count != update.heroes.Count || !SameSerialized(current.prefs, update.prefs))
		{
			return "战役身份/版本/关卡数量/英雄数量/难度不同";
		}
		for (int i = 0; i < current.levelStates.Count; i++)
		{
			LevelState val = current.levelStates[i];
			LevelState val2 = update.levelStates[i];
			if (val == null || val2 == null)
			{
				return "岛屿 " + i + "：缺少关卡资料";
			}
			if (val2.hasSprite && val2.houses == null)
			{
				return "岛屿 " + i + "：主机缩略图已就绪但房屋资料为空";
			}
			FieldInfo[] fields = typeof(LevelState).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				if (!fieldInfo.IsNotSerialized && Array.IndexOf(mutableLevel, fieldInfo.Name) < 0 && !SameSerialized(fieldInfo.GetValue(val), fieldInfo.GetValue(val2)))
				{
					return "岛屿 " + i + " 字段 " + fieldInfo.Name + "：" + Describe(fieldInfo.GetValue(val)) + " → " + Describe(fieldInfo.GetValue(val2));
				}
			}
		}
		for (int k = 0; k < current.heroes.Count; k++)
		{
			HeroDefinition val3 = current.heroes[k];
			HeroDefinition val4 = update.heroes[k];
			if (val3 == null || val4 == null || val3.id != val4.id || val3.nameTerm != val4.nameTerm || val3.hue != val4.hue || val3.color != val4.color)
			{
				return "英雄 " + k + "：身份或基础外观不同";
			}
		}
		return null;
	}

	private static string Describe(object value)
	{
		if (value != null)
		{
			if (!(value is IList))
			{
				return value.ToString();
			}
			return "列表数量=" + ((IList)value).Count;
		}
		return "空";
	}

	internal static bool HouseLayoutEqual(LevelState a, LevelState b)
	{
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		if (a.houses == null || b.houses == null)
		{
			if (a.houses == null)
			{
				return b.houses == null;
			}
			return false;
		}
		if (a.houses.Length != b.houses.Length)
		{
			return false;
		}
		for (int i = 0; i < a.houses.Length; i++)
		{
			if (a.houses[i].rect != b.houses[i].rect || a.houses[i].value != b.houses[i].value)
			{
				return false;
			}
		}
		return true;
	}

	internal static bool GenerationChanged(LevelState a, LevelState b)
	{
		if (a.seed == b.seed && a.coinTarget == b.coinTarget)
		{
			return !HouseLayoutEqual(a, b);
		}
		return true;
	}

	internal static string GenerationDifference(LevelState a, LevelState b)
	{
		return "种子 " + a.seed + "→" + b.seed + "，生成金币 " + a.coinTarget + "→" + b.coinTarget + "，房屋数量 " + ((a.houses == null) ? (-1) : a.houses.Length) + "→" + ((b.houses == null) ? (-1) : b.houses.Length) + "，房屋布局相同=" + HouseLayoutEqual(a, b);
	}

	private static bool SameSerialized(object a, object b)
	{
		return SameSerialized(a, b, 0);
	}

	private static bool SameSerialized(object a, object b, int depth)
	{
		if (a == b)
		{
			return true;
		}
		if (a == null || b == null || a.GetType() != b.GetType())
		{
			return false;
		}
		Type type = a.GetType();
		if (type.IsPrimitive || type.IsEnum || a is string || a is decimal || a is DateTime)
		{
			return a.Equals(b);
		}
		if (depth >= 16)
		{
			return false;
		}
		IList list = a as IList;
		IList list2 = b as IList;
		if (list != null)
		{
			if (list.Count != list2.Count)
			{
				return false;
			}
			for (int i = 0; i < list.Count; i++)
			{
				if (!SameSerialized(list[i], list2[i], depth + 1))
				{
					return false;
				}
			}
			return true;
		}
		FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (FieldInfo fieldInfo in fields)
		{
			if (!fieldInfo.IsNotSerialized && !SameSerialized(fieldInfo.GetValue(a), fieldInfo.GetValue(b), depth + 1))
			{
				return false;
			}
		}
		return true;
	}

	private static void CopyFields(object current, object update, params string[] excluded)
	{
		FieldInfo[] fields = current.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (FieldInfo fieldInfo in fields)
		{
			if (!fieldInfo.IsNotSerialized && !fieldInfo.IsInitOnly && Array.IndexOf(excluded, fieldInfo.Name) < 0)
			{
				fieldInfo.SetValue(current, fieldInfo.GetValue(update));
			}
		}
	}

	public static void ApplyValues(CampaignSave current, CampaignSave update)
	{
		ApplyValuesCore(current, update, notifyUnlock: true);
	}

	private static void ApplyValuesCore(CampaignSave current, CampaignSave update, bool notifyUnlock)
	{
		if (!Compatible(current, update))
		{
			throw new IOException("战役结构变化，需要重建");
		}
		CopyFields(current, update, "levelStates", "heroes", "stats", "levelAtlas", "paintAtlas", "inventory", "deployOrder", "oldDeployOrder");
		current.loadTime = update.loadTime;
		CopyFields(current.stats, update.stats);
		current.inventory.Clear();
		current.inventory.AddRange(update.inventory);
		current.deployOrder.Clear();
		current.deployOrder.AddRange(update.deployOrder);
		current.oldDeployOrder.Clear();
		current.oldDeployOrder.AddRange(update.oldDeployOrder);
		for (int i = 0; i < current.heroes.Count; i++)
		{
			HeroDefinition val = current.heroes[i];
			HeroDefinition val2 = update.heroes[i];
			CopyFields(val.propertyBank, val2.propertyBank);
			CopyFields(val.stats, val2.stats);
			CopyFields(val, val2, "propertyBank", "statistics");
			val.upgrades.Clear();
			if (val.classUpgrade != null)
			{
				val.upgrades.Add(val.classUpgrade);
			}
			if (val.skillUpgrade != null)
			{
				val.upgrades.Add(val.skillUpgrade);
			}
			if (val.itemUpgrade != null)
			{
				val.upgrades.Add(val.itemUpgrade);
			}
			if (val.traitUpgrade != null)
			{
				val.upgrades.Add(val.traitUpgrade);
			}
			val.voice = val2.voice;
		}
		for (int j = 0; j < current.levelStates.Count; j++)
		{
			LevelState val3 = current.levelStates[j];
			LevelState val4 = update.levelStates[j];
			bool unlocked = val3.unlocked;
			CopyFields(val3, val4, "connections", "objectReferences", "houses", "_unlocked");
			if (val4.houses == null)
			{
				val3.houses = null;
			}
			else if (val3.houses == null || val3.houses.Length != val4.houses.Length)
			{
				val3.houses = (HouseState[])val4.houses.Clone();
			}
			else
			{
				Array.Copy(val4.houses, val3.houses, val4.houses.Length);
			}
			if (notifyUnlock)
			{
				val3.unlocked = val4.unlocked;
				if (unlocked && !val4.unlocked)
				{
					val3._unlocked = false;
				}
			}
		}
	}

	private static bool TextureCompatible(SavedTexture current, SavedTexture update)
	{
		if (current != null && update != null && global::UnityObjectOps.Truthy((Object)(object)current.tex) && global::UnityObjectOps.Truthy((Object)(object)update.tex) && ((Texture)current.tex).width == ((Texture)update.tex).width)
		{
			return ((Texture)current.tex).height == ((Texture)update.tex).height;
		}
		return false;
	}

	public static bool RuntimeCompatible(Campaign campaign, CampaignSave update)
	{
		return RuntimeIncompatibility(campaign, update) == null;
	}

	public static string RuntimeIncompatibility(Campaign campaign, CampaignSave update)
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)campaign))
		{
			return "客户端没有现有战役";
		}
		string text = Incompatibility(campaign.campaignSave, update);
		if (text != null)
		{
			return text;
		}
		if (campaign.levels.Count != update.levelStates.Count)
		{
			return "运行时岛屿数量不同";
		}
		if (!TextureCompatible(campaign.campaignSave.levelAtlas, update.levelAtlas) || !TextureCompatible(campaign.campaignSave.paintAtlas, update.paintAtlas))
		{
			return "地图/染色图集尺寸不同或图集缺失";
		}
		for (int i = 0; i < campaign.levels.Count; i++)
		{
			if (campaign.levels[i].levelState != campaign.campaignSave.levelStates[i])
			{
				return "岛屿 " + i + "：运行时资料引用失配";
			}
		}
		return null;
	}

	public static IEnumerator Apply(Campaign campaign, CampaignSave update)
	{
		CampaignSave current = campaign.campaignSave;
		List<MonoHero> graphics = new List<MonoHero>();
		List<LevelNode> list = new List<LevelNode>();
		for (int i = 0; i < current.levelStates.Count; i++)
		{
			if (current.levelStates[i].hasSprite != update.levelStates[i].hasSprite || !HouseLayoutEqual(current.levelStates[i], update.levelStates[i]))
			{
				list.Add(campaign.levels[i]);
			}
		}
		for (int j = 0; j < current.heroes.Count; j++)
		{
			if (!SameSerialized(current.heroes[j].classUpgrade, update.heroes[j].classUpgrade) || !SameSerialized(current.heroes[j].itemUpgrade, update.heroes[j].itemUpgrade) || !SameSerialized(current.heroes[j].traitUpgrade, update.heroes[j].traitUpgrade) || current.heroes[j].alive != update.heroes[j].alive)
			{
				graphics.Add(current.heroes[j].monoHero);
			}
		}
		ApplyValuesCore(current, update, notifyUnlock: false);
		foreach (LevelNode item in list)
		{
			Action onActivate = ((AgentState)item.behindFrontier).OnActivate;
			Action onActivate2 = ((AgentState)item.played).OnActivate;
			try
			{
				object value = AccessTools.Field(typeof(LevelVisuals), "houses").GetValue(item.levelVisuals);
				AccessTools.Method(value.GetType(), "ReturnAll", (Type[])null, (Type[])null).Invoke(value, null);
				AccessTools.Method(typeof(LevelVisuals), "SetupHouses", (Type[])null, (Type[])null).Invoke(item.levelVisuals, null);
			}
			finally
			{
				((AgentState)item.behindFrontier).OnActivate = onActivate;
				((AgentState)item.played).OnActivate = onActivate2;
			}
		}
		for (int k = 0; k < current.levelStates.Count; k++)
		{
			current.levelStates[k].unlocked = update.levelStates[k].unlocked;
			if (!update.levelStates[k].unlocked)
			{
				current.levelStates[k]._unlocked = false;
			}
		}
		foreach (MonoHero item2 in graphics)
		{
			if (!global::UnityObjectOps.Truthy((Object)(object)item2))
			{
				continue;
			}
			foreach (SerializableHeroUpgrade upgrade in item2.heroDef.upgrades)
			{
				if (upgrade != null && global::UnityObjectOps.Truthy((Object)(object)upgrade.definition))
				{
					upgrade.definition.OnAttachedToMonoHero(item2, upgrade.level);
				}
			}
			item2.UpdateRankCount();
			foreach (object item3 in item2.UpdateAliveSprite(item2.heroDef.alive, true))
			{
				yield return item3;
			}
		}
		if (graphics.Count > 0)
		{
			foreach (object item4 in MonoHero.BakeSprites((IEnumerable<MonoHero>)graphics))
			{
				yield return item4;
			}
		}
		UpdateTexture(current.levelAtlas, update.levelAtlas);
		UpdateTexture(current.paintAtlas, update.paintAtlas);
		Color32[] pixels = current.paintAtlas.tex.GetPixels32();
		Array.Copy(pixels, campaign.paintAtlas.tex3d.pixels, pixels.Length);
		campaign.paintAtlas.SavePixels();
		RefreshMap(campaign);
		Singleton<CampaignManager>.instance.islandGenerator.QueueUpGenerations(campaign);
	}

	public static void RefreshMap(Campaign campaign)
	{
		//IL_01f4: Unknown result type (might be due to invalid IL or missing references)
		CampaignSave campaignSave = campaign.campaignSave;
		campaign.pendingUnlocks.Clear();
		campaign.lastPlayedLevel = null;
		foreach (LevelNode level in campaign.levels)
		{
			((AgentState)level.unlocked).SetActive(level.IsUnlocked());
			((AgentState)level.played).SetActive(level.IsPlayed());
			((AgentState)level.behindFrontier).SetActive(level.IsBehindFrontier());
			((AgentState)level.available).SetActive(level.IsAvailable());
			((AgentState)level.levelVisuals.spriteState).SetActive(level.levelState.hasSprite);
			level.levelVisuals.UpdateCheckpoint();
			level.levelVisuals.UpdateHouses();
			level.levelVisuals.UpdateEnemies();
			var links = level.links;
			for (int i = 0; i < links.Length; i++)
			{
				links[i].Update(1f);
			}
		}
		((AgentState)campaign.heroesAvaliable).SetActive(campaignSave.HeroesAvailableThisTurn());
		LineFrontier componentInChildren = ((Component)campaign).GetComponentInChildren<LineFrontier>(true);
		if (global::UnityObjectOps.Truthy((Object)(object)componentInChildren))
		{
			componentInChildren.mainLineAnim.SetCurrent((float)campaignSave.vikingFrontierPosition);
			componentInChildren.nextLineAnim.SetCurrent((float)campaignSave.vikingFrontierPosition + 1f);
		}
		SuperUpgradeMenu instance = SuperUpgradeMenu.instance;
		if (global::UnityObjectOps.Truthy((Object)(object)instance))
		{
			instance.RefreshPortraits();
			instance.UpdateCoinBank();
		}
		CampaignMapUI componentInChildren2 = ((Component)Singleton<CampaignManager>.instance).GetComponentInChildren<CampaignMapUI>(true);
		if (global::UnityObjectOps.Truthy((Object)(object)componentInChildren2))
		{
			componentInChildren2.cameraController.UpdateLimits();
			AccessTools.Method(typeof(CampaignMapUI), "OnUpgradesClosed", (Type[])null, (Type[])null).Invoke(componentInChildren2, null);
			if (campaignSave.gameOver && ((Component)componentInChildren2).gameObject.activeInHierarchy && gameOverShown != campaignSave)
			{
				gameOverShown = campaignSave;
				AccessTools.Method(typeof(CampaignMapUI), "GameOver", (Type[])null, (Type[])null).Invoke(componentInChildren2, new object[1] { campaignSave.gameOverReason });
			}
		}
	}

	public static void PrepareClientMapForChange()
	{
		CampaignManager instance = Singleton<CampaignManager>.instance;
		CampaignMapUI val = (global::UnityObjectOps.Truthy((Object)(object)instance) ? ((Component)instance).GetComponentInChildren<CampaignMapUI>(true) : null);
		if (global::UnityObjectOps.Truthy((Object)(object)val))
		{
			((MonoBehaviour)val).StopAllCoroutines();
			AccessTools.Property(typeof(CampaignMapUI), "blockingInput").SetValue(val, false, null);
		}
	}

	private static void UpdateTexture(SavedTexture current, SavedTexture update)
	{
		current.tex.SetPixels32(update.tex.GetPixels32());
		current.tex.Apply();
		CopyFields(current, update);
	}
}
