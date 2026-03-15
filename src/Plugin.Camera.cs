using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace WallWorldPlugin;

public partial class Plugin
{
    private static readonly CameraPreset[] CameraPresets =
    {
        new("Default", 3f, 4f, 1f, 1f),
        new("Spider", 4f, 10f, 5f, 5f),
    };

    private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type CameraEntityType = AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(assembly =>
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        })
        .FirstOrDefault(type => type?.Name == "CameraEntity");
    private static readonly FieldInfo CameraEntitySizeField = CameraEntityType?.GetField("Size", AnyInstance);
    private static readonly PropertyInfo CameraEntityCameraProperty = CameraEntityType?.GetProperty("Camera", AnyInstance);
    private static readonly PropertyInfo CameraEntityCurrentDescriptionProperty = CameraEntityType?.GetProperty("CurrentDescription", AnyInstance);
    private static readonly Dictionary<object, CameraSizeState> CameraStates = new();
    private static int currentPresetIndex;
    private static string overlayMessage = string.Empty;
    private static float overlayVisibleUntil;
    private static GUIStyle overlayStyle;

    private void OnGUI()
    {
        if (Time.unscaledTime > overlayVisibleUntil)
        {
            return;
        }

        overlayStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 16,
            richText = false,
            wordWrap = true,
            padding = new RectOffset(12, 12, 8, 8)
        };

        Rect rect = new Rect(20f, 20f, 420f, 44f);
        GUI.Box(rect, overlayMessage, overlayStyle);
    }

    private void CycleCameraPreset()
    {
        currentPresetIndex = (currentPresetIndex + 1) % CameraPresets.Length;
        AnnounceCurrentPreset("Switched preset");
    }

    private void AnnounceCurrentPreset(string reason)
    {
        CameraPreset preset = CurrentPreset;
        string description = $"{preset.Name}: player x{preset.PlayerMultiplier:0.##}, spider x{preset.SpiderMultiplier:0.##}, point x{preset.PointMultiplier:0.##}, other x{preset.OtherMultiplier:0.##}";
        overlayMessage = $"Camera preset [{currentPresetIndex + 1}/{CameraPresets.Length}] {description}";
        overlayVisibleUntil = Time.unscaledTime + 4f;
        Logger.LogInfo($"{reason}: {description}. Press F8 to cycle.");
    }

    private static CameraPreset CurrentPreset => CameraPresets[currentPresetIndex];

    private void InstallCameraPatches()
    {
        if (CameraEntityType == null)
        {
            Logger.LogError("CameraEntity type not found. Camera zoom patch was not installed.");
            return;
        }

        if (CameraEntitySizeField == null)
        {
            Logger.LogError("CameraEntity.Size field not found. Camera zoom patch was not installed.");
            return;
        }

        if (CameraEntityCurrentDescriptionProperty == null)
        {
            Logger.LogError("CameraEntity.CurrentDescription property not found. Camera zoom patch was not installed.");
            return;
        }

        if (CameraEntityCameraProperty == null)
        {
            Logger.LogWarning("CameraEntity.Camera property not found. Logical camera size will still be patched.");
        }

        MethodInfo prefix = typeof(Plugin).GetMethod(nameof(CameraEntityPrefix), BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo postfix = typeof(Plugin).GetMethod(nameof(CameraEntityPostfix), BindingFlags.Static | BindingFlags.NonPublic);

        foreach (string methodName in new[] { "PostInit", "ForceSetCamera", "Tick" })
        {
            MethodInfo target = CameraEntityType.GetMethod(methodName, AnyInstance);
            if (target == null)
            {
                Logger.LogWarning($"CameraEntity.{methodName} was not found, skipping patch.");
                continue;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
            Logger.LogInfo($"Patched CameraEntity.{methodName}");
        }
    }

    private static void CameraEntityPrefix(object __instance)
    {
        RestoreBaseCameraSize(__instance);
    }

    private static void CameraEntityPostfix(object __instance, MethodBase __originalMethod)
    {
        FinalizeScaledCameraSize(__instance, __originalMethod.Name);
    }

    private static CameraSizeState GetCameraState(object cameraEntity)
    {
        if (!CameraStates.TryGetValue(cameraEntity, out CameraSizeState state))
        {
            state = new CameraSizeState();
            CameraStates[cameraEntity] = state;
        }

        return state;
    }

    private static float GetCameraEntitySize(object cameraEntity)
    {
        if (cameraEntity == null || CameraEntitySizeField == null)
        {
            return 0f;
        }

        object rawValue = CameraEntitySizeField.GetValue(cameraEntity);
        return rawValue == null ? 0f : Convert.ToSingle(rawValue);
    }

    private static void SetCameraEntitySize(object cameraEntity, float size)
    {
        CameraEntitySizeField?.SetValue(cameraEntity, size);
    }

    private static Camera GetCameraComponent(object cameraEntity)
    {
        return CameraEntityCameraProperty?.GetValue(cameraEntity, null) as Camera;
    }

    private static object GetCurrentDescription(object cameraEntity)
    {
        return CameraEntityCurrentDescriptionProperty?.GetValue(cameraEntity, null);
    }

    private static void RestoreBaseCameraSize(object cameraEntity)
    {
        CameraSizeState state = GetCameraState(cameraEntity);
        if (!state.HasAppliedScale)
        {
            return;
        }

        SetCameraEntitySize(cameraEntity, state.LastBaseSize);

        Camera unityCamera = GetCameraComponent(cameraEntity);
        if (unityCamera != null && unityCamera.orthographic)
        {
            unityCamera.orthographicSize = state.LastBaseSize;
        }

        state.HasAppliedScale = false;
    }

    private static float GetDescriptionMultiplier(string descriptionType)
    {
        CameraPreset preset = CurrentPreset;

        return descriptionType switch
        {
            "PlayerCameraDescription" => preset.PlayerMultiplier,
            "SpiderCameraDescription" => preset.SpiderMultiplier,
            "PointCameraDescription" => preset.PointMultiplier,
            _ => preset.OtherMultiplier,
        };
    }

    private static void FinalizeScaledCameraSize(object cameraEntity, string sourceMethod)
    {
        if (cameraEntity == null)
        {
            return;
        }

        CameraSizeState state = GetCameraState(cameraEntity);
        state.LastBaseSize = GetCameraEntitySize(cameraEntity);

        object description = GetCurrentDescription(cameraEntity);
        state.LastDescriptionType = description?.GetType().Name ?? "<null>";

        float baseSize = state.LastBaseSize;
        float scaledSize = baseSize;
        float multiplier = GetDescriptionMultiplier(state.LastDescriptionType);
        bool shouldScale = baseSize > 0f && !Mathf.Approximately(multiplier, 1f);

        if (shouldScale)
        {
            scaledSize = baseSize * multiplier;
            SetCameraEntitySize(cameraEntity, scaledSize);
        }
        else if (baseSize > 0f)
        {
            SetCameraEntitySize(cameraEntity, baseSize);
        }

        Camera unityCamera = GetCameraComponent(cameraEntity);
        if (unityCamera != null && unityCamera.orthographic)
        {
            unityCamera.orthographicSize = scaledSize;
        }

        state.HasAppliedScale = true;

        if (Logger != null && (state.LogCount < 3 || Time.unscaledTime >= state.NextLogTime))
        {
            Logger.LogInfo($"CameraEntity {sourceMethod}: preset={CurrentPreset.Name}, description={state.LastDescriptionType}, baseSize={baseSize}, scaledSize={scaledSize}, multiplier={multiplier:0.##}, scaled={shouldScale}");
            state.LogCount++;
            state.NextLogTime = Time.unscaledTime + 5f;
        }

        state.HasAppliedScale = shouldScale;
    }

    private sealed class CameraSizeState
    {
        public bool HasAppliedScale;
        public float LastBaseSize;
        public string LastDescriptionType;
        public int LogCount;
        public float NextLogTime;
    }

    private readonly struct CameraPreset
    {
        public CameraPreset(string name, float playerMultiplier, float spiderMultiplier, float pointMultiplier, float otherMultiplier)
        {
            Name = name;
            PlayerMultiplier = playerMultiplier;
            SpiderMultiplier = spiderMultiplier;
            PointMultiplier = pointMultiplier;
            OtherMultiplier = otherMultiplier;
        }

        public string Name { get; }

        public float PlayerMultiplier { get; }

        public float SpiderMultiplier { get; }

        public float PointMultiplier { get; }

        public float OtherMultiplier { get; }
    }
}
