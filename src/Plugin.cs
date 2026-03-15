using BepInEx;
using BepInEx.Logging;
using System;
using UnityEngine;

namespace WallWorldPlugin;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public partial class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private GameObject diagnosticObject;
    private global::HarmonyLib.Harmony harmony;
    private int pluginUpdateCount;
    private float nextPluginHeartbeatTime;

    private void Awake()
    {
        Logger = base.Logger;
        InitializePlayerMovementConfiguration();
        resourceCarryLimitOverride = Config.Bind(
            "Resources",
            "CarryLimitOverride",
            999,
            "Overrides the player resource carry limit. Values greater than 0 replace the game's StorageSize; values less than or equal to 0 keep the original game value.");

        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        Logger.LogInfo($"Plugin GameObject: {gameObject.name}, activeInHierarchy={gameObject.activeInHierarchy}, enabled={enabled}");
        Logger.LogInfo(resourceCarryLimitOverride.Value > 0
            ? $"Configured resource carry limit override: {resourceCarryLimitOverride.Value}"
            : "Configured resource carry limit override: disabled (using game default).");

        diagnosticObject = new GameObject("WallWorldPlugin.Diagnostics");
        DontDestroyOnLoad(diagnosticObject);
        diagnosticObject.hideFlags = HideFlags.HideAndDontSave;

        DiagnosticProbe probe = diagnosticObject.AddComponent<DiagnosticProbe>();
        probe.Initialize(Logger);

        harmony = new global::HarmonyLib.Harmony(MyPluginInfo.PLUGIN_GUID);
        InstallCameraPatches();
        InstallResourceHooverPatches();
        InstallPlayerMovementPatches();

        Logger.LogInfo("Diagnostic probe GameObject created.");
        AnnounceCurrentPreset("Initial preset");
    }

    private void Start()
    {
        Logger.LogInfo("Plugin Start called.");
    }

    private void OnEnable()
    {
        if (Logger != null)
        {
            Logger.LogInfo("Plugin OnEnable called.");
        }
    }

    private void OnDisable()
    {
        if (Logger != null)
        {
            Logger.LogWarning("Plugin OnDisable called.");
        }
    }

    private void OnDestroy()
    {
        if (Logger != null)
        {
            Logger.LogWarning("Plugin OnDestroy called.");
        }

        harmony?.UnpatchSelf();
        CameraStates.Clear();
        DisposePlayerMovementHooks();

        if (diagnosticObject != null)
        {
            Destroy(diagnosticObject);
            diagnosticObject = null;
        }
    }

    private void Update()
    {
        pluginUpdateCount++;

        if (Input.GetKeyDown(KeyCode.F8))
        {
            CycleCameraPreset();
        }

        if (pluginUpdateCount <= 3 || Time.unscaledTime >= nextPluginHeartbeatTime)
        {
            Logger.LogInfo($"Plugin Update #{pluginUpdateCount}, activeInHierarchy={gameObject.activeInHierarchy}, enabled={enabled}, scene={gameObject.scene.name}");

            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                Logger.LogInfo("Camera.main is null.");
            }
            else
            {
                Logger.LogInfo($"Camera orthographic={mainCam.orthographic}, orthographicSize={mainCam.orthographicSize}");
            }

            nextPluginHeartbeatTime = Time.unscaledTime + 5f;
        }
    }

    private sealed class DiagnosticProbe : MonoBehaviour
    {
        private ManualLogSource logger;
        private int updateCount;
        private float nextHeartbeatTime;

        public void Initialize(ManualLogSource logSource)
        {
            logger = logSource;
        }

        private void Awake()
        {
            logger?.LogInfo($"DiagnosticProbe Awake on {gameObject.name}");
        }

        private void Start()
        {
            logger?.LogInfo("DiagnosticProbe Start called.");
        }

        private void OnEnable()
        {
            logger?.LogInfo("DiagnosticProbe OnEnable called.");
        }

        private void OnDisable()
        {
            logger?.LogWarning("DiagnosticProbe OnDisable called.");
        }

        private void OnDestroy()
        {
            logger?.LogWarning("DiagnosticProbe OnDestroy called.");
        }

        private void Update()
        {
            updateCount++;

            if (updateCount <= 3 || Time.unscaledTime >= nextHeartbeatTime)
            {
                logger?.LogInfo($"DiagnosticProbe Update #{updateCount}, activeInHierarchy={gameObject.activeInHierarchy}, enabled={enabled}, scene={gameObject.scene.name}");
                nextHeartbeatTime = Time.unscaledTime + 5f;
            }
        }
    }
}

