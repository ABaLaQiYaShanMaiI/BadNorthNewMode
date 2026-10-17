using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Voxels;
using Voxels.TowerDefense;
using Voxels.TowerDefense.CampaignGeneration;
using Voxels.TowerDefense.ProfileInternals;

namespace Together;

internal sealed class CampaignIslandCache : IDisposable
{
	private readonly IslandGenerator generator;

	private static readonly FieldInfo queueField = AccessTools.Field(typeof(IslandGenerator), "queue");

	private static readonly FieldInfo currentField = AccessTools.Field(typeof(IslandGenerator), "currentIslandProxy");

	private static readonly FieldInfo timerField = AccessTools.Field(typeof(IslandGenerator), "timerCoroutine");

	private static readonly FieldInfo[] queueLists = new FieldInfo[3]
	{
		AccessTools.Field(typeof(IslandGeneratorQueue), "threadQueue"),
		AccessTools.Field(typeof(IslandGeneratorQueue), "threads"),
		AccessTools.Field(typeof(IslandGeneratorQueue), "buildQueue")
	};

	private static readonly MethodInfo infinite = AccessTools.Method(typeof(IslandGenerator), "InfiniteEnumerator", (Type[])null, (Type[])null);

	private static readonly MethodInfo timer = AccessTools.Method(AccessTools.TypeByName("CoroutineUtils"), "GenerateTimer", new Type[2]
	{
		typeof(float),
		typeof(IEnumerator<GenInfo>)
	}, (Type[])null);

	public CampaignIslandCache(IslandGenerator generator)
	{
		this.generator = generator;
		IslandGenerator.AddBlocker((object)this, (object)this);
	}

	public void InvalidateChanged(Campaign campaign, CampaignSave update)
	{
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		for (int i = 0; i < campaign.levels.Count; i++)
		{
			LevelNode val = campaign.levels[i];
			if (CampaignProgress.GenerationChanged(val.levelState, update.levelStates[i]))
			{
				IslandProxy islandProxy = val.islandProxy;
				Plugin.Log.LogInfo((object)("[战役缓存] 岛屿 " + i + " 状态=" + ((object)islandProxy.state/*cast due to constrained. prefix*/).ToString() + "，" + CampaignProgress.GenerationDifference(val.levelState, update.levelStates[i])));
				Invalidate(islandProxy);
			}
		}
	}

	internal void Invalidate(IslandProxy proxy)
	{
		if (proxy.thread != null && proxy.thread.IsRunning())
		{
			proxy.thread.Abort(true);
		}
		object value = queueField.GetValue(generator);
		FieldInfo[] array = queueLists;
		for (int i = 0; i < array.Length; i++)
		{
			((List<IslandProxy>)array[i].GetValue(value)).Remove(proxy);
		}
		if (currentField.GetValue(generator) == proxy)
		{
			currentField.SetValue(generator, null);
			timerField.SetValue(generator, timer.Invoke(null, new object[2]
			{
				2.5f,
				infinite.Invoke(generator, null)
			}));
		}
		proxy.CancelGeneration();
	}

	public void Dispose()
	{
		IslandGenerator.RemoveBlocker((object)this, (object)this);
	}
}
