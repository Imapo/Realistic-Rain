using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace WetnessMod.Common.Configs
{
    public class WetnessConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("Wetting")]

        [Range(0f, 5f)]
        [DefaultValue(0.12f)]
        [Tooltip("Скорость намокания под дождём (% в тик)")]
        public float RainWetRate;

        [Range(0f, 5f)]
        [DefaultValue(1.2f)]
        [Tooltip("Скорость намокания при погружении в воду (% в тик)")]
        public float WaterWetRate;

        [Range(0f, 5f)]
        [DefaultValue(0.03f)]
        [Tooltip("Скорость фонового намокания в джунглях без дождя (% в тик)")]
        public float JungleAmbientWetRate;

        [DefaultValue(40f)]
        [Range(0f, 100f)]
        [Tooltip("Максимальная влажность от одних только джунглей без дождя")]
        public float JungleAmbientWetCap;

        [Header("Drying")]

        [Range(0f, 5f)]
        [DefaultValue(0.05f)]
        [Tooltip("Базовая скорость высыхания (% в тик), дальше умножается на модификаторы погоды/места")]
        public float BaseDryRate;

        [Range(0.1f, 10f)]
        [DefaultValue(3f)]
        public float SunnyDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(0.4f)]
        public float CloudyOrNightDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(0.15f)]
        public float UndergroundDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(3f)]
        public float CampfireDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(3f)]
        public float UnderworldDryMultiplier;

        [Header("EquipmentEffects")]

        [Range(0f, 1f)]
        [DefaultValue(0.4f)]
        [Tooltip("Максимальная доля защиты, которую теряет полностью мокрая броня")]
        public float MaxArmorDefenseLossFraction;

        [Range(1f, 100f)]
        [DefaultValue(100f)]
        [Tooltip("Порог влажности, при котором аксессуар отключается")]
        public float AccessoryDisableThreshold;

        [Header("SoilToMud")]

        [Range(1, 200)]
        [DefaultValue(30)]
        [Tooltip("Радиус в блоках влево/вправо от игрока, в котором отслеживается намокание земли")]
        public int TileWetnessRangeX;

        [Range(0f, 10f)]
        [DefaultValue(0.6f)]
        [Tooltip("Скорость накопления влаги в блоке земли под дождём (% в тик)")]
        public float TileWetRate;

        [Range(0f, 10f)]
        [DefaultValue(0.25f)]
        [Tooltip("Скорость высыхания грязи, когда дождь прекратился (% в тик)")]
        public float TileDryRate;
    }
}
