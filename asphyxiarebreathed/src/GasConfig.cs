namespace AsphyxiaRebreathed
{
    public class GasConfig
    {
        public const string ConfigPath = "asphyxiarebreathed.json";
        public const string LegacyConfigPath = "GasConfig.json";

        public static GasConfig Loaded { get; set; } = new GasConfig();

        //Gas settings

        public bool Explosions { get; set; } = true;

        public bool FlammableGas { get; set; } = true;

        public double PickaxeExplosionChance { get; set; } = 0.25;

        public bool ContainerBonus { get; set; } = true;

        public bool Smoke { get; set; } = true;

        public bool Acid { get; set; } = true;      

        public bool Exhaling { get; set; } = true;

        public bool LitBackpackSmolderEmits { get; set; } = false;

        public bool OreSeepsEnabled { get; set; } = false;

        public float OreSeepChance { get; set; } = 0.05f;

        public float OreSeepAmountMultiplier { get; set; } = 0.05f;

        public float OreSeepBreakMultiplier { get; set; } = 4f;

        public double OreSeepUpdateHours { get; set; } = 1;

        public int DefaultSpreadRadius { get; set; } = 7;

        public float SpreadGasOnBreakChance { get; set; } = 1;

        public float SpreadGasOnPlaceChance { get; set; } = 0;

        public float UpdateSpreadGasChance { get; set; } = 0.01f;

        //Compatibility Settings

        public bool RealSmokeCompatibility { get; set; } = true;

        public bool DisableRealSmokeAsphyxiation { get; set; } = true;

        //Breathing Settings

        public bool AllowScuba { get; set; } = true;

        public bool AllowMasks { get; set; } = true;

        public bool ToxicEffects { get; set; } = true;

        #region Control Content

        public bool GasesEnabled { get; set; } = true;

        public bool GasesDebugEnabled { get; set; } = false;

        public bool BreathingEnabled { get; set; } = true;

        public bool PlayerBreathingEnabled { get; set; } = true;
        #endregion
    }
}
