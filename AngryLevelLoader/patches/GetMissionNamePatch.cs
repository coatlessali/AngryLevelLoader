﻿using HarmonyLib;

namespace AngryLevelLoader.patches
{
	[HarmonyPatch(typeof(GetMissionName), nameof(GetMissionName.GetMission))]
	class GetMissionName_Patch
	{
		[HarmonyPrefix]
		static bool Prefix(ref string __result)
		{
			if (!Plugin.isInCustomScene)
				return true;

			__result = Plugin.currentLevelData.levelName;
			return false;
		}
	}
}
