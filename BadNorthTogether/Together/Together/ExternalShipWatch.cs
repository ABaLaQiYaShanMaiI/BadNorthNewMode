using UnityEngine;
using Object = UnityEngine.Object;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace Together;

/// <summary>BadNorthTogether（借鉴 BadNorthMP 的 CustomShipWatch）：给客机装配出来的船加一道保险。
/// 若 `onShipLaunch` 已触发、但船在 4 秒内仍处于 disabled（出发动画事件丢了/组件被停），
/// 强制调用 `LaunchAnimComplete()` 让船继续靠岸，避免"波开始了、船却停在海上"。</summary>
internal sealed class ExternalShipWatch : MonoBehaviour
{
	private Longship ship;

	private bool launched;

	private float launchedAt;

	private bool recovered;

	private void Awake()
	{
		ship = GetComponent<Longship>();
		if (global::UnityObjectOps.Truthy((Object)(object)ship))
		{
			ship.onShipLaunch += OnLaunch;
		}
	}

	private void OnDestroy()
	{
		if (global::UnityObjectOps.Truthy((Object)(object)ship))
		{
			ship.onShipLaunch -= OnLaunch;
		}
	}

	private void OnLaunch()
	{
		launched = true;
		launchedAt = Time.time;
	}

	private void Update()
	{
		if (!global::UnityObjectOps.Truthy((Object)(object)ship) || !launched || recovered || ship.enabled || Time.time - launchedAt < 4f)
		{
			return;
		}
		recovered = true;
		Plugin.Log.LogWarning((object)("[主机波清单] 船 " + ((Object)ship).name + " 出发后 4 秒仍未恢复，强制完成出发动画"));
		ship.LaunchAnimComplete();
	}
}
