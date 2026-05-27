using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AsphyxiaRebreathed
{
    public static class RealSmokeCompat
    {
        const string SmokeGasName = "smoke";
        const string CarbonMonoxideGasName = "carbonmonoxide";
        const string CarbonDioxideGasName = "carbondioxide";

        public static void TryPatch(Harmony harmony, ICoreAPI api)
        {
            if (!GasConfig.Loaded.RealSmokeCompatibility || !GasConfig.Loaded.DisableRealSmokeAsphyxiation) return;

            Type behaviorType = AccessTools.TypeByName("RealSmoke.EntityBehaviorAsphyxiate");
            MethodInfo tickMethod = AccessTools.Method(behaviorType, "OnGameTick");
            MethodInfo prefixMethod = AccessTools.Method(typeof(RealSmokeCompat), nameof(SkipRealSmokeAsphyxiation));

            if (tickMethod == null || prefixMethod == null) return;

            harmony.Patch(tickMethod, prefix: new HarmonyMethod(prefixMethod));
            api.Logger.Notification("Asphyxia: Rebreathed is using gas reports for Real Smoke asphyxiation.");
        }

        public static Dictionary<string, float> GetSmokeGasesAt(IWorldAccessor world, BlockPos pos)
        {
            if (!GasConfig.Loaded.RealSmokeCompatibility || world?.Config.GetBool("RealSmokeEnabled") != true) return null;

            Vec3d center = pos.ToVec3d().Add(0.5, 0.5, 0.5);
            Entity[] smokeEntities = world.GetEntitiesAround(center, 1.1f, 1.1f, IsRealSmokeEntity);
            if (smokeEntities == null || smokeEntities.Length == 0) return null;

            Dictionary<string, float> gases = new Dictionary<string, float>();

            foreach (Entity entity in smokeEntities)
            {
                float amount = entity.WatchedAttributes.GetInt("amount", 0) / 64f;
                if (amount <= 0) continue;

                float distance = (float)entity.Pos.XYZ.DistanceTo(center);
                float influence = GameMath.Clamp(1.1f - distance, 0f, 1f);
                if (influence <= 0) continue;

                float smokeDensity = amount * influence;
                float carbon = entity.WatchedAttributes.GetFloat("carbonContent", 0);
                float unburnt = entity.WatchedAttributes.GetFloat("unburntContent", 0);

                MergeGas(gases, SmokeGasName, smokeDensity * (0.5f + carbon * 0.5f));
                MergeGas(gases, CarbonMonoxideGasName, smokeDensity * (carbon * 0.2f + unburnt * 0.35f));
                MergeGas(gases, CarbonDioxideGasName, smokeDensity * (0.45f + (1f - unburnt) * 0.35f));
            }

            return gases.Count > 0 ? gases : null;
        }

        public static Dictionary<string, float> MergeGasReports(Dictionary<string, float> storedGases, Dictionary<string, float> smokeGases)
        {
            if (smokeGases == null || smokeGases.Count == 0) return storedGases;

            Dictionary<string, float> result = storedGases != null
                ? new Dictionary<string, float>(storedGases)
                : new Dictionary<string, float>();

            foreach (var gas in smokeGases)
            {
                MergeGas(result, gas.Key, gas.Value);
            }

            return result;
        }

        static bool SkipRealSmokeAsphyxiation()
        {
            return !GasConfig.Loaded.RealSmokeCompatibility || !GasConfig.Loaded.DisableRealSmokeAsphyxiation;
        }

        static bool IsRealSmokeEntity(Entity entity)
        {
            if (entity?.Code?.Domain != "realsmoke" || entity.Code.Path != "smoke") return false;
            if (entity.WatchedAttributes.GetBool("deactivated", false)) return false;
            if (entity.WatchedAttributes.GetBool("despawning", false)) return false;

            return true;
        }

        static void MergeGas(Dictionary<string, float> gases, string name, float amount)
        {
            if (amount <= 0) return;

            if (!gases.ContainsKey(name)) gases[name] = GameMath.Clamp(amount, 0, 1);
            else gases[name] = GameMath.Clamp(gases[name] + amount, 0, 1);
        }
    }
}
