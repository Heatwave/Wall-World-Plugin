using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;

namespace WallWorldPlugin;

public partial class Plugin
{
    private static readonly Type ResourceHooverEntityType = AppDomain.CurrentDomain.GetAssemblies()
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
        .FirstOrDefault(type => type?.Name == "ResourceHooverEntity");
    private static readonly PropertyInfo ResourceHooverStorageSizeProperty = ResourceHooverEntityType?.GetProperty("StorageSize", AnyInstance);
    private static readonly PropertyInfo ResourceHooverStorageProperty = ResourceHooverEntityType?.GetProperty("Storage", AnyInstance);
    private static ConfigEntry<int> resourceCarryLimitOverride;

    private void InstallResourceHooverPatches()
    {
        if (ResourceHooverEntityType == null)
        {
            Logger.LogError("ResourceHooverEntity type not found. Resource carry limit patch was not installed.");
            return;
        }

        if (ResourceHooverStorageSizeProperty == null)
        {
            Logger.LogError("ResourceHooverEntity.StorageSize property not found. Resource carry limit patch was not installed.");
            return;
        }

        if (ResourceHooverStorageProperty == null)
        {
            Logger.LogError("ResourceHooverEntity.Storage property not found. Resource carry limit patch was not installed.");
            return;
        }

        MethodInfo postfix = typeof(Plugin).GetMethod(nameof(ResourceHooverPostfix), BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo storageSizeGetterPostfix = typeof(Plugin).GetMethod(nameof(ResourceHooverStorageSizeGetterPostfix), BindingFlags.Static | BindingFlags.NonPublic);

        MethodInfo storageSizeGetter = ResourceHooverStorageSizeProperty.GetGetMethod(true);
        if (storageSizeGetter == null)
        {
            Logger.LogError("ResourceHooverEntity.StorageSize getter not found. Resource carry limit override was not installed.");
            return;
        }

        harmony.Patch(storageSizeGetter, postfix: new HarmonyMethod(storageSizeGetterPostfix));
        Logger.LogInfo("Patched ResourceHooverEntity.StorageSize getter");

        foreach (string methodName in new[] { "Init", "AddItemToStorage", "ScrapStorage" })
        {
            MethodInfo target = ResourceHooverEntityType.GetMethod(methodName, AnyInstance);
            if (target == null)
            {
                Logger.LogWarning($"ResourceHooverEntity.{methodName} was not found, skipping patch.");
                continue;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            Logger.LogInfo($"Patched ResourceHooverEntity.{methodName}");
        }
    }

    private static void ResourceHooverPostfix(object __instance, MethodBase __originalMethod)
    {
        LogResourceCarryState(__instance, __originalMethod.Name);
    }

    private static void ResourceHooverStorageSizeGetterPostfix(ref int __result)
    {
        if (resourceCarryLimitOverride != null && resourceCarryLimitOverride.Value > 0)
        {
            __result = resourceCarryLimitOverride.Value;
        }
    }

    private static int GetResourceHooverStorageSize(object resourceHooverEntity)
    {
        if (resourceHooverEntity == null || ResourceHooverStorageSizeProperty == null)
        {
            return 0;
        }

        object rawValue = ResourceHooverStorageSizeProperty.GetValue(resourceHooverEntity, null);
        return rawValue == null ? 0 : Convert.ToInt32(rawValue);
    }

    private static int GetResourceHooverStorageCount(object resourceHooverEntity)
    {
        if (resourceHooverEntity == null || ResourceHooverStorageProperty == null)
        {
            return 0;
        }

        if (ResourceHooverStorageProperty.GetValue(resourceHooverEntity, null) is System.Collections.ICollection collection)
        {
            return collection.Count;
        }

        return 0;
    }

    private static void LogResourceCarryState(object resourceHooverEntity, string sourceMethod)
    {
        if (Logger == null || resourceHooverEntity == null)
        {
            return;
        }

        int currentStored = GetResourceHooverStorageCount(resourceHooverEntity);
        int carryLimit = GetResourceHooverStorageSize(resourceHooverEntity);
        Logger.LogInfo($"Resource carry limit ({sourceMethod}): currentStored={currentStored}, carryLimit={carryLimit}");
    }
}