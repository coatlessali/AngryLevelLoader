﻿using HarmonyLib;
using RudeLevelScript;
using UnityEngine;
using UnityEngine.UI;

namespace AngryLevelLoader.patches
{
	[HarmonyPatch(typeof(FinalRank), nameof(FinalRank.Start))]
	class FinalRank_Start_Patch
	{
		[HarmonyPrefix]
		static bool Prefix(FinalRank __instance)
		{
			if (!Plugin.isInCustomScene)
				return true;

			bool secretLevel = __instance.transform.Find("Challenge") == null;

			if (secretLevel)
			{
				Transform titleTrans = __instance.transform.Find("Title/Text");
				if (titleTrans != null)
				{
					titleTrans.GetComponent<Text>().text = Plugin.currentLevelData.name;
				}
				else
				{
					Debug.LogWarning("Could not find title text under final canvas");
				}

				return true;
			}

			__instance.levelSecrets = StatsManager.instance.secretObjects;
			if (__instance.levelSecrets.Length != Plugin.currentLevelData.secretCount)
			{
				Debug.LogWarning($"Inconsistent secrets size, expected {Plugin.currentLevelData.secretCount}, found {__instance.levelSecrets.Length}");
				__instance.levelSecrets = new GameObject[Plugin.currentLevelData.secretCount];
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(FinalRank), nameof(FinalRank.CountSecrets))]
	class FinalRank_CountSecrets_Patch
	{
		static bool Prefix(FinalRank __instance)
		{
			if (!Plugin.isInCustomScene)
				return true;

			Plugin.currentLevelContainer.AssureSecretsSize();
			if (__instance.secretsCheckProgress >= Plugin.currentLevelData.secretCount)
			{
				__instance.Invoke("Appear", __instance.timeBetween);
				return false;
			}

			if (Plugin.currentLevelContainer.secrets.value[__instance.secretsCheckProgress] != 'T')
			{
				__instance.secretsInfo[__instance.secretsCheckProgress].color = Color.black;
				__instance.secretsCheckProgress += 1;

				if (__instance.secretsCheckProgress < __instance.levelSecrets.Length)
				{
					__instance.Invoke("CountSecrets", __instance.timeBetween);
					return false;
				}
				__instance.Invoke("Appear", __instance.timeBetween);
				return false;
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(FinalRank), nameof(FinalRank.LevelChange))]
	class FinalRank_LevelChange_Patch
	{
		[HarmonyPrefix]
		static bool Prefix()
		{
			if (!Plugin.isInCustomScene)
				return true;

			if (FinalPit_SendInfo_Patch.lastTarget != null && !string.IsNullOrEmpty(FinalPit_SendInfo_Patch.lastTarget.targetLevelUniqueId))
			{
				string idPath = FinalPit_SendInfo_Patch.lastTarget.targetLevelUniqueId;

				foreach (AngryBundleContainer container in Plugin.angryBundles.Values)
				{
					foreach (LevelContainer level in container.levels.Values)
					{
						if (level.data.uniqueIdentifier == idPath)
						{
							AngrySceneManager.LoadLevel(container, level, level.data, level.data.scenePath);
							return false;
						}
					}
				}

				Debug.LogWarning("Could not find target level id " + idPath);
				MonoSingleton<OptionsManager>.Instance.QuitMission();
				return false;
			}
			else
			{
				MonoSingleton<OptionsManager>.Instance.QuitMission();
			}

			return false;
		}
	}
}
