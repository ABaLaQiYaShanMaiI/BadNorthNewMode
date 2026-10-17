using HarmonyLib;
using Voxels.TowerDefense.UI;

namespace Together;

[HarmonyPatch(typeof(NewGameOptionsPopup), "NewGame")]
internal static class NewGameOptionsPatch
{
	private static bool Prefix()
	{
		if (!Session.IsClient)
		{
			return true;
		}
		Plugin.Log.LogWarning((object)"[战役] 客户端不能新建联机战役，请等待主机");
		return false;
	}
}
