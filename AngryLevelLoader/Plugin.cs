﻿using BepInEx;
using HarmonyLib;
using PluginConfig.API;
using PluginConfig.API.Decorators;
using PluginConfig.API.Fields;
using PluginConfig.API.Functionals;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.Audio;
using RudeLevelScript;
using PluginConfig;
using BepInEx.Bootstrap;

namespace AngryLevelLoader
{
	public class SpaceField : CustomConfigField
    {
        public SpaceField(ConfigPanel parentPanel, float space) : base(parentPanel, 60, space)
        {

        }
    }

	[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
	[BepInDependency(PluginConfig.PluginConfiguratorController.PLUGIN_GUID, "1.6.0")]
	[BepInDependency(Ultrapain.Plugin.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.heaven.orhell", BepInDependency.DependencyFlags.SoftDependency)]
	public class Plugin : BaseUnityPlugin
	{
		public const bool devMode = true;

        public const string PLUGIN_NAME = "AngryLevelLoader";
        public const string PLUGIN_GUID = "com.eternalUnion.angryLevelLoader";
        public const string PLUGIN_VERSION = "2.3.2";
		// This is the path addressable remote load path uses
		// {AngryLevelLoader.Plugin.tempFolderPath}\\{guid}
		public static string tempFolderPath;
		public static string dataPath;
        public static string levelsPath;
        public static Plugin instance;
		
		public static PluginConfigurator internalConfig;
		public static StringField lastVersion;
		public static BoolField ignoreUpdates;
		public static StringField configDataPath;

		public static bool ultrapainLoaded = false;
		public static bool heavenOrHellLoaded = false;

		public static Dictionary<string, RudeLevelData> idDictionary = new Dictionary<string, RudeLevelData>();
		public static Dictionary<string, AngryBundleContainer> angryBundles = new Dictionary<string, AngryBundleContainer>();
		public static Dictionary<string, long> lastPlayed = new Dictionary<string, long>();

		public static void LoadLastPlayedMap()
		{
			lastPlayed.Clear();

			string path = Path.Combine(workingDir, "lastPlayedMap.txt");
			if (!File.Exists(path))
				return;

			using (StreamReader reader = new StreamReader(File.Open(path, FileMode.Open, FileAccess.Read)))
			{
				while (!reader.EndOfStream)
				{
					string key = reader.ReadLine();
					if (reader.EndOfStream)
					{
						Debug.LogWarning("Invalid end of last played map file");
						break;
					}

					string value = reader.ReadLine();
					if (long.TryParse(value, out long seconds))
					{
						lastPlayed[key] = seconds;
					}
					else
					{
						Debug.Log($"Invalid last played time '{value}'");
					}
				}
			}
		}

		public static void UpdateLastPlayed(AngryBundleContainer bundle)
		{
			string guid = bundle.guid;
			if (guid.Length != 32)
				return;

			if (bundleSortingMode.value == BundleSorting.LastPlayed)
				bundle.rootPanel.siblingIndex = 0;
			long secondsNow = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
			lastPlayed[guid] = secondsNow;

			string path = Path.Combine(workingDir, "lastPlayedMap.txt");
			using (StreamWriter writer = new StreamWriter(File.Open(path, FileMode.OpenOrCreate, FileAccess.Write)))
			{
				writer.BaseStream.Seek(0, SeekOrigin.Begin);
				writer.BaseStream.SetLength(0);
				foreach (var pair in lastPlayed)
				{
					writer.WriteLine(pair.Key);
					writer.WriteLine(pair.Value.ToString());
				}
			}
		}

		public static AngryBundleContainer GetAngryBundleByGuid(string guid)
		{
			return angryBundles.Values.Where(bundle => bundle.guid == guid).FirstOrDefault();
		}

		// This does NOT reload the files, only
		// loads newly added angry levels
		public static void ScanForLevels()
        {
            errorText.text = "";
			string bundlePath = levelsPath;
            if (!Directory.Exists(bundlePath))
            {
                Debug.LogWarning("Could not find the Levels folder at " + bundlePath);
				errorText.text = "<color=red>Error: </color>Levels folder not found";
				return;
            }

			foreach (string path in Directory.GetFiles(bundlePath))
			{
				if (angryBundles.TryGetValue(path, out AngryBundleContainer levelAsset))
				{
					levelAsset.rootPanel.interactable = true;
					levelAsset.rootPanel.hidden = false;
					continue;
				}

				AngryBundleContainer level = new AngryBundleContainer(path);
				angryBundles[path] = level;
				try
				{
					level.UpdateScenes(false, true);
				}
				catch (Exception e)
				{
					Debug.LogWarning($"Exception thrown while loading level bundle: {e}");
					if (!string.IsNullOrEmpty(errorText.text))
						errorText.text += '\n';
					errorText.text += $"<color=red>Error loading {Path.GetFileNameWithoutExtension(path)}</color>. Check the logs for more information";
				}
			}
		}

		public static void SortBundles()
		{
			int i = 0;
			if (bundleSortingMode.value == BundleSorting.Alphabetically)
			{
				foreach (var bundle in angryBundles.Values.OrderBy(b => b.name))
					bundle.rootPanel.siblingIndex = i++;
			}
			else if (bundleSortingMode.value == BundleSorting.Author)
			{
				foreach (var bundle in angryBundles.Values.OrderBy(b => b.author))
					bundle.rootPanel.siblingIndex = i++;
			}
			else if (bundleSortingMode.value == BundleSorting.LastPlayed)
			{
				foreach (var bundle in angryBundles.Values.OrderByDescending((b) => {
					if (lastPlayed.TryGetValue(b.guid, out long time))
						return time;
					return 0;
				}))
				{
					bundle.rootPanel.siblingIndex = i++;
				}
			}
		}

		public static LevelContainer GetLevel(string id)
		{
			foreach (AngryBundleContainer container in angryBundles.Values)
			{
				foreach (LevelContainer level in container.levels.Values)
				{
					if (level.field.data.uniqueIdentifier == id)
						return level;
				}
			}

			return null;
		}

		public static void UpdateAllUI()
		{
			foreach (AngryBundleContainer angryBundle in  angryBundles.Values)
			{
				foreach (LevelContainer level in angryBundle.levels.Values)
				{
					level.UpdateUI();
				}
			}
		}

        public static bool LoadEssentialScripts()
        {
			bool loaded = true;

			var res = AttemptLoadScriptWithCertificate("AngryLoaderAPI.dll");
			if (res == LoadScriptResult.NotFound)
			{
				Debug.LogError("Required script AngryLoaderAPI.dll not found");
				loaded = false;
			}
			else if (res == LoadScriptResult.NoCertificate)
			{
				Debug.LogError("Required script AngryLoaderAPI.dll has a missing certificate");
				loaded = false;
			}
			else if (res == LoadScriptResult.InvalidCertificate)
			{
				Debug.LogError("Required script AngryLoaderAPI.dll has an invalid certificate");
				loaded = false;
			}

			res = AttemptLoadScriptWithCertificate("RudeLevelScripts.dll");
			if (res == LoadScriptResult.NotFound)
			{
				Debug.LogError("Required script RudeLevelScripts.dll not found");
				loaded = false;
			}
			else if (res == LoadScriptResult.NoCertificate)
			{
				Debug.LogError("Required script RudeLevelScripts.dll has a missing certificate");
				loaded = false;
			}
			else if (res == LoadScriptResult.InvalidCertificate)
			{
				Debug.LogError("Required script RudeLevelScripts.dll has an invalid certificate");
				loaded = false;
			}

			return loaded;
		}

        // Game assets
        public static Font gameFont;
		public static Sprite notPlayedPreview;
		public static Sprite lockedPreview;

        public static bool isInCustomScene = false;
        public static RudeLevelData currentLevelData;
        public static LevelContainer currentLevelContainer;
		public static AngryBundleContainer currentBundleContainer;
		public static int selectedDifficulty;

		internal static PropertyInfo SceneHelper_CurrentScene = typeof(SceneHelper).GetProperty(nameof(SceneHelper.CurrentScene));
        public static void CheckIsInCustomScene(Scene current)
        {
			foreach (AngryBundleContainer container in angryBundles.Values)
			{
				if (container.GetAllScenePaths().Contains(current.path))
				{
					isInCustomScene = true;
					currentLevelData = container.GetAllLevelData().Where(data => data.scenePath == current.path).First();
					currentBundleContainer = container;
					currentLevelContainer = container.levels[container.GetAllLevelData().Where(data => data.scenePath == current.path).First().uniqueIdentifier];
					currentLevelContainer.discovered.value = true;
					currentLevelContainer.UpdateUI();
					SceneHelper_CurrentScene.SetValue(null, currentLevelData.uniqueIdentifier);
					config.presetButtonInteractable = false;

					return;
				}
			}

			isInCustomScene = false;
			currentBundleContainer = null;
			currentLevelData = null;
			currentLevelContainer = null;
			config.presetButtonInteractable = true;
		}

        public static Harmony harmony;
        
		public static PluginConfigurator config;
		public static ConfigHeader levelUpdateNotifier;
		public static ConfigHeader newLevelNotifier;
		public static StringField newLevelNotifierLevels;
		public static BoolField newLevelToggle;
        public static ConfigHeader errorText;
		public static ConfigDivision bundleDivision;

		public static KeyCodeField reloadFileKeybind;
		public static BoolField refreshCatalogOnBoot;
		public static BoolField checkForUpdates;
		public static BoolField levelUpdateNotifierToggle;
		public static BoolField levelUpdateIgnoreCustomBuilds;
		public static BoolField newLevelNotifierToggle;
		public static List<string> scriptCertificateIgnore = new List<string>();
		public static StringMultilineField scriptCertificateIgnoreField;
		public static BoolField useDevelopmentBranch;
		public static BoolField scriptUpdateIgnoreCustom;
		public enum BundleSorting
		{
			Alphabetically,
			Author,
			LastPlayed
		}
		public static EnumField<BundleSorting> bundleSortingMode;

		private static List<string> difficultyList = new List<string> { "HARMLESS", "LENIENT", "STANDARD", "VIOLENT" };

		public static string workingDir;

		private static List<string> loadedScripts = new List<string>();
		public enum LoadScriptResult
		{
			Loaded,
			NotFound,
			NoCertificate,
			InvalidCertificate,
		}
		
		public static LoadScriptResult AttemptLoadScriptWithCertificate(string scriptName)
		{
			if (loadedScripts.Contains(scriptName))
				return LoadScriptResult.Loaded;
			
			string scriptPath = Path.Combine(workingDir, "Scripts", scriptName);
			if (!File.Exists(scriptPath))
				return LoadScriptResult.NotFound;
			if (!File.Exists(scriptPath + ".cert"))
				return LoadScriptResult.NoCertificate;

			if (!CryptographyUtils.VerifyFileCertificate(scriptPath, scriptPath + ".cert"))
				return LoadScriptResult.InvalidCertificate;

			Assembly a = Assembly.Load(File.ReadAllBytes(scriptPath));
			loadedScripts.Add(scriptName);
			return LoadScriptResult.Loaded;
		}

		public static void ForceLoadScript(string scriptName)
		{
			string scriptPath = Path.Combine(workingDir, "Scripts", scriptName);
			Assembly.Load(File.ReadAllBytes(scriptPath));
			loadedScripts.Add(scriptName);
		}

		public static bool ScriptLoaded(string scriptName)
		{
			return loadedScripts.Contains(scriptName);
		}

		public static bool ScriptExists(string scriptName)
		{
			return File.Exists(Path.Combine(workingDir, "Scripts", scriptName));
		}

		public static ButtonField changelog;

        private void Awake()
		{
			// Plugin startup logic
			instance = this;

			internalConfig = PluginConfigurator.Create("Angry Level Loader (INTERNAL)" ,PLUGIN_GUID + "_internal");
			internalConfig.hidden = true;
			internalConfig.interactable = false;
			internalConfig.presetButtonHidden = true;
			internalConfig.presetButtonInteractable = false;

            lastVersion = new StringField(internalConfig.rootPanel, "lastPluginVersion", "lastPluginVersion", "", true);
            ignoreUpdates = new BoolField(internalConfig.rootPanel, "ignoreUpdate", "ignoreUpdate", false);
			configDataPath = new StringField(internalConfig.rootPanel, "dataPath", "dataPath", Path.Combine(IOUtils.AppData, "AngryLevelLoader"));

            workingDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			dataPath = configDataPath.value;
			IOUtils.TryCreateDirectory(dataPath);
			levelsPath = Path.Combine(dataPath, "Levels");
            IOUtils.TryCreateDirectory(levelsPath);
            tempFolderPath = Path.Combine(dataPath, "LevelsUnpacked");
            IOUtils.TryCreateDirectory(tempFolderPath);

            Addressables.InitializeAsync().WaitForCompletion();

			if (!LoadEssentialScripts())
			{
				Debug.LogError("Disabling AngryLevelLoader because one or more of its dependencies have failed to load");
				enabled = false;
				return;
			}

			LoadLastPlayedMap();

			harmony = new Harmony(PLUGIN_GUID);
            harmony.PatchAll();

            SceneManager.activeSceneChanged += (before, after) =>
            {
                CheckIsInCustomScene(after);
				if (isInCustomScene)
					AngrySceneManager.PostSceneLoad();
			};

            gameFont = Addressables.LoadAssetAsync<Font>("Assets/Fonts/VCR_OSD_MONO_1.001.ttf").WaitForCompletion();
			notPlayedPreview = Addressables.LoadAssetAsync<Sprite>("Assets/Textures/UI/Level Thumbnails/Locked3.png").WaitForCompletion();
			lockedPreview = Addressables.LoadAssetAsync<Sprite>("Assets/Textures/UI/Level Thumbnails/Locked.png").WaitForCompletion();

			if (Chainloader.PluginInfos.ContainsKey(Ultrapain.Plugin.PLUGIN_GUID))
			{
				ultrapainLoaded = true;
				difficultyList.Add("ULTRAPAIN");
			}
			if (Chainloader.PluginInfos.ContainsKey("com.heaven.orhell"))
			{
				heavenOrHellLoaded = true;
				difficultyList.Add("HEAVEN OR HELL");
			}

			config = PluginConfigurator.Create("Angry Level Loader", PLUGIN_GUID);
			config.postConfigChange += UpdateAllUI;
			// Silly #3, fixes the plugin config thumbnail - coatlessali
			config.SetIconWithURL(Path.Combine("file://" + workingDir, "plugin-icon.png"));
			newLevelToggle = new BoolField(config.rootPanel, "", "v_newLevelToggle", false);
			newLevelToggle.hidden = true;
			config.rootPanel.onPannelOpenEvent += (external) =>
			{
				if (newLevelToggle.value)
				{
					newLevelNotifier.text = string.Join("\n", Plugin.newLevelNotifierLevels.value.Split('`').Where(level => !string.IsNullOrEmpty(level)).Select(name => $"<color=lime>New level: {name}</color>"));
					newLevelNotifier.hidden = false;
					Plugin.newLevelNotifierLevels.value = "";
				}
				newLevelToggle.value = false;
			};

			newLevelNotifier = new ConfigHeader(config.rootPanel, "<color=lime>New levels are available!</color>", 16);
			newLevelNotifier.hidden = true;
			levelUpdateNotifier = new ConfigHeader(config.rootPanel, "<color=lime>Level updates available!</color>", 16);
			levelUpdateNotifier.hidden = true;
			OnlineLevelsManager.onlineLevelsPanel = new ConfigPanel(config.rootPanel, "Online Levels", "b_onlineLevels", ConfigPanel.PanelFieldType.StandardWithIcon);
			// Silly #4, fixes the online button - coatlessali
			OnlineLevelsManager.onlineLevelsPanel.SetIconWithURL("file://" + Path.Combine(workingDir, "online-icon.png"));
			OnlineLevelsManager.onlineLevelsPanel.onPannelOpenEvent += (e) =>
			{
				newLevelNotifier.hidden = true;
			};
			OnlineLevelsManager.Init();

			StringListField difficultySelect = new StringListField(config.rootPanel, "Difficulty", "difficultySelect", difficultyList.ToArray(), "VIOLENT");
            difficultySelect.onValueChange += (e) =>
            {
                selectedDifficulty = Array.IndexOf(difficultyList.ToArray(), e.value);
                if (selectedDifficulty == -1)
                {
                    Debug.LogWarning("Invalid difficulty, setting to violent");
                    selectedDifficulty = 3;
					e.value = "VIOLENT";
                }
				else
				{
					if (e.value == "ULTRAPAIN")
						selectedDifficulty = 4;
					else if (e.value == "HEAVEN OR HELL")
						selectedDifficulty = 5;
				}
            };
            difficultySelect.TriggerValueChangeEvent();

			ConfigPanel settingsPanel = new ConfigPanel(config.rootPanel, "Settings", "p_settings", ConfigPanel.PanelFieldType.Standard);

			ButtonField openLevels = new ButtonField(settingsPanel, "Open Levels Folder", "openLevelsButton");
			openLevels.onClick += () => Application.OpenURL(levelsPath);
			changelog = new ButtonField(settingsPanel, "Changelog", "changelogButton");
			changelog.onClick += () =>
			{
				changelog.interactable = false;
				OnlineLevelsManager.instance.StartCoroutine(PluginUpdateHandler.CheckPluginUpdate());
			};

			new SpaceField(settingsPanel, 5);
			StringField dataPathInput = new StringField(settingsPanel, "Data Path", "s_dataPathInput", dataPath, false, false);
			ButtonField changeDataPath = new ButtonField(settingsPanel, "Move Data", "s_changeDataPath");
			ConfigHeader dataInfo = new ConfigHeader(settingsPanel, "<color=red>RESTART REQUIRED</color>", 18);
            new SpaceField(settingsPanel, 5);
            dataInfo.hidden = true;
			changeDataPath.onClick += () =>
			{
				string newPath = dataPathInput.value;
				if (newPath == configDataPath.value)
					return;

				if (!Directory.Exists(newPath))
				{
					dataInfo.text = "<color=red>Could not find the directory</color>";
					dataInfo.hidden = false;
					return;
				}

				string newLevelsFolder = Path.Combine(newPath, "Levels");
				IOUtils.TryCreateDirectory(newLevelsFolder);
				foreach (string levelFile in Directory.GetFiles(levelsPath))
				{
					File.Copy(levelFile, Path.Combine(newLevelsFolder, Path.GetFileName(levelFile)), true);
					File.Delete(levelFile);
				}
				Directory.Delete(levelsPath, true);
				levelsPath = newLevelsFolder;

                string newLevelsUnpackedFolder = Path.Combine(newPath, "LevelsUnpacked");
                IOUtils.TryCreateDirectory(newLevelsUnpackedFolder);
                foreach (string unpackedLevelFolder in Directory.GetDirectories(tempFolderPath))
                {
					string dest = Path.Combine(newLevelsUnpackedFolder, Path.GetFileName(unpackedLevelFolder));
					if (Directory.Exists(dest))
						Directory.Delete(dest, true);

					IOUtils.DirectoryCopy(unpackedLevelFolder, dest, true, true);
                }
                Directory.Delete(tempFolderPath, true);
                tempFolderPath = newLevelsUnpackedFolder;

                dataInfo.text = "<color=red>RESTART REQUIRED</color>";
                dataInfo.hidden = false;
				configDataPath.value = newPath;
            };

            reloadFileKeybind = new KeyCodeField(settingsPanel, "Reload File", "f_reloadFile", KeyCode.None);
			settingsPanel.hidden = true;
			bundleSortingMode = new EnumField<BundleSorting>(settingsPanel, "Bundle sorting", "s_bundleSortingMode", BundleSorting.Alphabetically);
			bundleSortingMode.onValueChange += (e) =>
			{
				bundleSortingMode.value = e.value;
				SortBundles();
			};

			new ConfigHeader(settingsPanel, "Online");
			new ConfigHeader(settingsPanel, "Online level catalog and thumbnails are cached, if there are no updates only 64 bytes of data is downloaded per refresh", 12, TextAnchor.UpperLeft);
			refreshCatalogOnBoot = new BoolField(settingsPanel, "Refresh online catalog on boot", "s_refreshCatalogBoot", true);
			checkForUpdates = new BoolField(settingsPanel, "Check for updates on boot", "s_checkForUpdates", true);
			useDevelopmentBranch = new BoolField(settingsPanel, "Use development chanel", "s_useDevChannel", false);
			if (!devMode)
				useDevelopmentBranch.hidden = true;
			levelUpdateNotifierToggle = new BoolField(settingsPanel, "Notify on level updates", "s_levelUpdateNofify", true);
			levelUpdateNotifierToggle.onValueChange += (e) =>
			{
				levelUpdateNotifierToggle.value = e.value;
				OnlineLevelsManager.CheckLevelUpdateText();
			};
			levelUpdateIgnoreCustomBuilds = new BoolField(settingsPanel, "Ignore updates for custom build", "s_levelUpdateIgnoreCustomBuilds", false);
			levelUpdateIgnoreCustomBuilds.onValueChange += (e) =>
			{
				levelUpdateIgnoreCustomBuilds.value = e.value;
				OnlineLevelsManager.CheckLevelUpdateText();
			};
			newLevelNotifierLevels = new StringField(settingsPanel, "h_New levels", "s_newLevelNotifierLevels", "", true);
			newLevelNotifierLevels.hidden = true;
			newLevelNotifierToggle = new BoolField(settingsPanel, "Notify on new level release", "s_newLevelNotiftToggle", true);
			newLevelNotifierToggle.onValueChange += (e) =>
			{
				newLevelNotifierToggle.value = e.value;
				if (!e.value)
					newLevelNotifier.hidden = true;
			};
			new ConfigHeader(settingsPanel, "Scripts");
			scriptUpdateIgnoreCustom = new BoolField(settingsPanel, "Ignore updates for custom builds", "s_scriptUpdateIgnoreCustom", false);
			scriptCertificateIgnoreField = new StringMultilineField(settingsPanel, "Certificate ignore", "s_scriptCertificateIgnore", "", true);
			scriptCertificateIgnore = scriptCertificateIgnoreField.value.Split('\n').ToList();
			
			ButtonArrayField settingsAndReload = new ButtonArrayField(config.rootPanel, "settingsAndReload", 2, new float[] { 0.5f, 0.5f }, new string[] { "Settings", "Scan For Levels" });
			settingsAndReload.OnClickEventHandler(0).onClick += () =>
			{
				settingsPanel.OpenPanel();
			};
			settingsAndReload.OnClickEventHandler(1).onClick += () =>
			{
				ScanForLevels();
			};

			errorText = new ConfigHeader(config.rootPanel, "", 16, TextAnchor.UpperLeft); ;

			new ConfigHeader(config.rootPanel, "Level Bundles");
			bundleDivision = new ConfigDivision(config.rootPanel, "div_bundles");
			
			// TODO: Investigate further on this issue:
			//
			// if I don't do that, when I load an addressable scene (custom level)
			// it results in whatever this is. I guess it doesn't load the dependencies
			// but I am not too sure. Same thing happens when I load trough asset bundles
			// instead and everything is white unless I load a prefab which creates a chain
			// reaction of texture, material, shader dependency loads. Though it MIGHT be incorrect,
			// and I am not sure of the actual origin of the issue (because when I check the loaded
			// bundles every addressable bundle is already in the memory like what?)
			Addressables.LoadAssetAsync<GameObject>("Assets/Prefabs/Attacks and Projectiles/Projectile Decorative.prefab");

			PluginUpdateHandler.Check();

            ScanForLevels();
			if (refreshCatalogOnBoot.value)
				OnlineLevelsManager.RefreshAsync();

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

		float lastPress = 0;
		private void OnGUI()
		{
			if (reloadFileKeybind.value == KeyCode.None)
				return;

			if (!isInCustomScene)
				return;

			Event current = Event.current;
			KeyCode keyCode = KeyCode.None;
			if (current.keyCode == KeyCode.Escape)
			{
				return;
			}
			if (current.isKey || current.isMouse || current.button > 2 || current.shift)
			{
				if (current.isKey)
				{
					keyCode = current.keyCode;
				}
				else if (Input.GetKey(KeyCode.LeftShift))
				{
					keyCode = KeyCode.LeftShift;
				}
				else if (Input.GetKey(KeyCode.RightShift))
				{
					keyCode = KeyCode.RightShift;
				}
				else if (current.button <= 6)
				{
					keyCode = KeyCode.Mouse0 + current.button;
				}
			}
			else if (Input.GetKey(KeyCode.Mouse3) || Input.GetKey(KeyCode.Mouse4) || Input.GetKey(KeyCode.Mouse5) || Input.GetKey(KeyCode.Mouse6))
			{
				keyCode = KeyCode.Mouse3;
				if (Input.GetKey(KeyCode.Mouse4))
				{
					keyCode = KeyCode.Mouse4;
				}
				else if (Input.GetKey(KeyCode.Mouse5))
				{
					keyCode = KeyCode.Mouse5;
				}
				else if (Input.GetKey(KeyCode.Mouse6))
				{
					keyCode = KeyCode.Mouse6;
				}
			}
			else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
			{
				keyCode = KeyCode.LeftShift;
				if (Input.GetKey(KeyCode.RightShift))
				{
					keyCode = KeyCode.RightShift;
				}
			}
			
			if (keyCode == reloadFileKeybind.value)
			{
				if (Time.time - lastPress < 3)
					return;

				lastPress = Time.time;

				if (NotificationPanel.CurrentNotificationCount() == 0)
					ReloadFileKeyPressed();
			}
		}
	
		private void ReloadFileKeyPressed()
		{
			if (currentBundleContainer != null)
				currentBundleContainer.UpdateScenes(false, false);
		}
	}

    public static class RudeLevelInterface
    {
		public static char INCOMPLETE_LEVEL_CHAR = '-';
		public static char GetLevelRank(string levelId)
        {
			LevelContainer level = Plugin.GetLevel(levelId);
			if (level == null)
				return INCOMPLETE_LEVEL_CHAR;
			return level.finalRank.value[0];
		}
	
        public static bool GetLevelChallenge(string levelId)
		{
			LevelContainer level = Plugin.GetLevel(levelId);
			if (level == null)
				return false;
			return level.challenge.value;
		}

		public static bool GetLevelSecret(string levelId, int secretIndex)
		{
			if (secretIndex < 0)
				return false;

			LevelContainer level = Plugin.GetLevel(levelId);
			if (level == null)
				return false;

			level.AssureSecretsSize();
			if (secretIndex >= level.field.data.secretCount)
				return false;
			return level.secrets.value[secretIndex] == 'T';
		}

        public static string GetCurrentLevelId()
        {
            return Plugin.isInCustomScene ? Plugin.currentLevelData.uniqueIdentifier : "";
        }
    }

	public static class RudeBundleInterface
	{
		public static bool BundleExists(string bundleGuid)
		{
			return Plugin.angryBundles.Values.Where(bundle => bundle.guid == bundleGuid).FirstOrDefault() != null;
		}

		public static string GetBundleBuildHash(string bundleGuid)
		{
			var bundle = Plugin.angryBundles.Values.Where(bundle => bundle.guid == bundleGuid).FirstOrDefault();
			return bundle == null ? "" : bundle.hash;
		}
    }
}
