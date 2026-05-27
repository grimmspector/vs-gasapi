using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace AsphyxiaRebreathed
{
    public static class GasSourceProperties
    {
        public static Dictionary<string, float> GetProduceGas(JsonObject properties, Block block)
        {
            Dictionary<string, float> produceGas = properties["produceGas"].AsObject(new Dictionary<string, float>());
            if (produceGas != null && produceGas.Count > 0) return produceGas;

            return GetProduceGasByType(properties, block);
        }

        public static Dictionary<string, float> GetSmolderGas(JsonObject properties, Block block)
        {
            Dictionary<string, float> smolderGas = properties["smolderGas"].AsObject(new Dictionary<string, float>());
            if (smolderGas != null && smolderGas.Count > 0) return smolderGas;

            return GetGasByType(properties, block, "smolderGasByType");
        }

        private static Dictionary<string, float> GetProduceGasByType(JsonObject properties, Block block)
        {
            return GetGasByType(properties, block, "produceGasByType");
        }

        private static Dictionary<string, float> GetGasByType(JsonObject properties, Block block, string propertyName)
        {
            Dictionary<string, Dictionary<string, float>> byType = properties[propertyName].AsObject(new Dictionary<string, Dictionary<string, float>>());
            if (byType == null || byType.Count < 1 || block?.Code == null) return new Dictionary<string, float>();

            string code = block.Code.ToString();

            foreach (var entry in byType)
            {
                if (WildcardUtil.Match(entry.Key, code)) return entry.Value;
            }

            return new Dictionary<string, float>();
        }
    }
}
