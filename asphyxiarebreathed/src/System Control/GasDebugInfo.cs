using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Config;

namespace AsphyxiaRebreathed
{
    public static class GasDebugInfo
    {
        public static bool AppendGasList(StringBuilder dsc, string header, Dictionary<string, float> gases)
        {
            if (dsc == null || gases == null || gases.Count < 1) return false;

            dsc.AppendLine(header);

            foreach (var gas in gases)
            {
                string name = Lang.GetIfExists("asphyxiarebreathed:gas-" + gas.Key) ?? gas.Key;
                dsc.AppendLine(name + " : " + (gas.Value * 100).ToString("0.0") + "%");
            }

            return true;
        }
    }
}
