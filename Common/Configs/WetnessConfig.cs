using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace WetnessMod.Common.Configs
{
    public class WetnessConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("Wetting")]

        [Range(0f, 5f)]
        [DefaultValue(0.003f)]
        public float RainWetRate;

        [Range(0f, 5f)]
        [DefaultValue(0.6f)]
        public float WaterWetRate;

        [Range(0.05f, 1f)]
        [DefaultValue(0.001f)]
        public float SnowWetRateMultiplier;

        [Header("Drying")]

        [Range(0f, 5f)]
        [DefaultValue(0.018f)]
        public float BaseDryRate;

        [Range(0.1f, 10f)]
        [DefaultValue(2.5f)]
        public float SunnyDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(0.5f)]
        public float CloudyOrNightDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(0.2f)]
        public float ShadeDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(0.08f)]
        public float UndergroundDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(3f)]
        public float CampfireDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(1.6f)]
        public float TorchDryMultiplier;

        [Range(1, 100)]
        [DefaultValue(10)]
        public int FireWarmthDetectionRadius;

        [Range(0.1f, 10f)]
        [DefaultValue(3f)]
        public float UnderworldDryMultiplier;

        [Header("EquipmentEffects")]

        [Range(0f, 1f)]
        [DefaultValue(0.4f)]
        public float MaxArmorDefenseLossFraction;

        [Range(1f, 99f)]
        [DefaultValue(50f)]
        public float AccessoryDisableThreshold;

        [Range(0f, 1f)]
        [DefaultValue(0.15f)]
        public float WetDisableChancePerSecond;

        [DefaultValue(false)]
        public bool HardcoreAccessoryWetness;

        [Header("RainProtection")]

        [Range(0f, 1f)]
        [DefaultValue(0.33f)]
        public float RainProtectionPerPieceArmor;

        [Range(0f, 1f)]
        [DefaultValue(0.99f)]
        public float RainProtectionMaxArmor;

        [Range(0f, 1f)]
        [DefaultValue(0.30f)]
        public float RainProtectionPerPieceAccessory;

        [Range(0f, 1f)]
        [DefaultValue(0.90f)]
        public float RainProtectionMaxAccessory;

        [Header("SoilToMud")]

        [Range(1, 200)]
        [DefaultValue(30)]
        public int TileWetnessRangeX;

        [Range(0f, 10f)]
        [DefaultValue(0.6f)]
        public float TileWetRate;

        [Range(0f, 10f)]
        [DefaultValue(0.6f)]
        public float TileDryRate;

        [Range(1, 200)]
        [DefaultValue(18)]
        public int TileMaxAttemptsPerTick;

        [Range(0f, 100f)]
        [DefaultValue(50f)]
        public float TileSeepThreshold;

        [Range(0.1f, 1f)]
        [DefaultValue(0.55f)]
        public float TileDepthRateMultiplier;

        [Range(0f, 2f)]
        [DefaultValue(0.4f)]
        public float TileNeighborSpreadBonus;

        [Header("Puddles")]

        [DefaultValue(true)]
        public bool PuddleEnabled;

        [Range(0f, 1f)]
        [DefaultValue(0.5f)]
        public float PuddleChance;

        [Range(1, 255)]
        [DefaultValue(40)]
        public int PuddleMinLiquidAmount;

        [Range(1, 255)]
        [DefaultValue(110)]
        public int PuddleMaxLiquidAmount;

        [Header("FireExtinguish")]

        [DefaultValue(true)]
        public bool FireExtinguishEnabled;

        [Range(1, 200)]
        [DefaultValue(30)]
        public int FireExtinguishRangeX;

        [Range(1, 200)]
        [DefaultValue(6)]
        public int FireMaxAttemptsPerTick;

        [Range(1, 200)]
        [DefaultValue(40)]
        public int FireMaxSkyCheckHeight;

        [Range(0f, 10f)]
        [DefaultValue(0.15f)]
        public float FireExtinguishRate;

        [Range(0f, 10f)]
        [DefaultValue(0.4f)]
        public float FireExtinguishCooldownRate;

        [DefaultValue(true)]
        public bool FireSpawnDust;

        [Header("VisualEffects")]

        [DefaultValue(true)]
        public bool WaterDripEnabled;

        [DefaultValue(false)]
        public bool DisabledItemSparkleEnabled;
    }
}