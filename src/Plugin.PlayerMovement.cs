using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WallWorldPlugin;

public partial class Plugin
{
    private static readonly Type PlayerFactoryType = AppDomain.CurrentDomain.GetAssemblies()
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
        .FirstOrDefault(type => type?.Name == "PlayerFactory");
    private static readonly Type UnitEntityType = AppDomain.CurrentDomain.GetAssemblies()
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
        .FirstOrDefault(type => type?.Name == "UnitEntity");
    private static readonly FieldInfo UnitEntitySpeedStatField = UnitEntityType?.GetField("SpeedStat", AnyInstance);
    private static readonly Type SpeedStatType = UnitEntitySpeedStatField?.FieldType;
    private static readonly ConstructorInfo SpeedStatConstructor = SpeedStatType?.GetConstructor(new[] { typeof(float) });
    private static readonly MethodInfo SpeedStatGetTotalValueMethod = SpeedStatType?.GetMethod("GetTotalValue", AnyInstance);
    private static readonly EventInfo SpeedStatOnValueChangedEvent = SpeedStatType?.GetEvent("OnValueChanged", AnyInstance);
    private static readonly Dictionary<object, Action<float>> PlayerSpeedStatHandlers = new();
    private static ConfigEntry<float> playerMovementSpeedOverride;

    private void InitializePlayerMovementConfiguration()
    {
        playerMovementSpeedOverride = Config.Bind(
            "PlayerMovement",
            "SpeedOverride",
            10f,
            "Overrides the player's base movement speed. Values greater than 0 replace the player's initial speed; values less than or equal to 0 keep the original game value.");

        Logger.LogInfo(playerMovementSpeedOverride.Value > 0f
            ? $"Configured player movement speed override: {playerMovementSpeedOverride.Value:0.##}"
            : "Configured player movement speed override: disabled (using game default).");
    }

    private void InstallPlayerMovementPatches()
    {
        if (PlayerFactoryType == null)
        {
            Logger.LogError("PlayerFactory type not found. Player movement speed patch was not installed.");
            return;
        }

        if (UnitEntitySpeedStatField == null)
        {
            Logger.LogError("UnitEntity.SpeedStat field not found. Player movement speed patch was not installed.");
            return;
        }

        if (SpeedStatConstructor == null || SpeedStatGetTotalValueMethod == null)
        {
            Logger.LogError("SpeedStat members were not found. Player movement speed patch was not installed.");
            return;
        }

        MethodInfo postfix = typeof(Plugin).GetMethod(nameof(PlayerFactoryCreatePostfix), BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo target = PlayerFactoryType.GetMethod("Create", AnyInstance);
        if (target == null)
        {
            Logger.LogError("PlayerFactory.Create was not found. Player movement speed patch was not installed.");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        Logger.LogInfo("Patched PlayerFactory.Create");
    }

    private static void PlayerFactoryCreatePostfix(object __result)
    {
        ApplyPlayerMovementOverride(__result);
        AttachPlayerMovementLogging(__result);
        LogCurrentPlayerMovementSpeed(__result, "PlayerFactory.Create");
    }

    private static void ApplyPlayerMovementOverride(object playerEntity)
    {
        if (playerEntity == null || UnitEntitySpeedStatField == null || SpeedStatConstructor == null)
        {
            return;
        }

        float configuredSpeed = playerMovementSpeedOverride?.Value ?? -1f;
        if (configuredSpeed <= 0f)
        {
            return;
        }

        if (UnitEntitySpeedStatField.GetValue(playerEntity) is IDisposable existingSpeedStat)
        {
            existingSpeedStat.Dispose();
        }

        object overriddenSpeedStat = SpeedStatConstructor.Invoke(new object[] { configuredSpeed });
        UnitEntitySpeedStatField.SetValue(playerEntity, overriddenSpeedStat);

        Logger?.LogInfo($"Applied player movement speed override: {configuredSpeed:0.##}");
    }

    private static void AttachPlayerMovementLogging(object playerEntity)
    {
        if (playerEntity == null || UnitEntitySpeedStatField == null || SpeedStatOnValueChangedEvent == null)
        {
            return;
        }

        object speedStat = UnitEntitySpeedStatField.GetValue(playerEntity);
        if (speedStat == null || PlayerSpeedStatHandlers.ContainsKey(speedStat))
        {
            return;
        }

        Action<float> handler = value => Logger?.LogInfo($"Player movement speed updated: {value:0.##}");
        SpeedStatOnValueChangedEvent.AddEventHandler(speedStat, handler);
        PlayerSpeedStatHandlers[speedStat] = handler;
    }

    private static void LogCurrentPlayerMovementSpeed(object playerEntity, string sourceMethod)
    {
        if (Logger == null || playerEntity == null || UnitEntitySpeedStatField == null || SpeedStatGetTotalValueMethod == null)
        {
            return;
        }

        object speedStat = UnitEntitySpeedStatField.GetValue(playerEntity);
        if (speedStat == null)
        {
            return;
        }

        object rawValue = SpeedStatGetTotalValueMethod.Invoke(speedStat, null);
        float currentSpeed = rawValue == null ? 0f : Convert.ToSingle(rawValue);
        Logger.LogInfo($"Player movement speed ({sourceMethod}): currentSpeed={currentSpeed:0.##}");
    }

    private void DisposePlayerMovementHooks()
    {
        if (SpeedStatOnValueChangedEvent == null)
        {
            PlayerSpeedStatHandlers.Clear();
            return;
        }

        foreach (KeyValuePair<object, Action<float>> entry in PlayerSpeedStatHandlers)
        {
            try
            {
                SpeedStatOnValueChangedEvent.RemoveEventHandler(entry.Key, entry.Value);
            }
            catch
            {
            }
        }

        PlayerSpeedStatHandlers.Clear();
    }
}