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
        [Tooltip("Скорость накопления влаги в блоке земли под дождём за одну попытку (% за попытку)")]
        public float TileWetRate;

        [Range(0f, 10f)]
        [DefaultValue(0.6f)]
        [Tooltip("Скорость высыхания грязи, когда дождь прекратился (% в тик) - специально равна скорости намокания, чтобы сохнуть примерно так же долго, как мокнет")]
        public float TileDryRate;

        [Range(1, 200)]
        [DefaultValue(18)]
        [Tooltip("Сколько случайных блоков за тик пытается промокнуть/просохнуть система (меньше = медленнее и естественнее)")]
        public int TileMaxAttemptsPerTick;

        [Range(0f, 100f)]
        [DefaultValue(50f)]
        [Tooltip("Насколько должен промокнуть верхний слой земли, прежде чем начнёт мокнуть слой под ним")]
        public float TileSeepThreshold;

        [Range(0.1f, 1f)]
        [DefaultValue(0.55f)]
        [Tooltip("Во сколько раз медленнее мокнет каждый следующий слой земли вглубь (макс. глубина - 3 блока)")]
        public float TileDepthRateMultiplier;

        [Range(0f, 2f)]
        [DefaultValue(0.4f)]
        [Tooltip("Бонус к скорости намокания, если соседний блок на той же глубине уже стал грязью (для эффекта расползающегося пятна)")]
        public float TileNeighborSpreadBonus;

        [Header("FireExtinguish")]

        [DefaultValue(true)]
        [Tooltip("Включает тушение факелов и костров под открытым дождём")]
        public bool FireExtinguishEnabled;

        [Range(1, 200)]
        [DefaultValue(30)]
        [Tooltip("Радиус в блоках вокруг игрока (и по X, и по Y), в котором отслеживаются факелы/костры")]
        public int FireExtinguishRangeX;

        [Range(1, 200)]
        [DefaultValue(6)]
        [Tooltip("Сколько случайных факелов/костров за тик пытается потушить система")]
        public int FireMaxAttemptsPerTick;

        [Range(1, 200)]
        [DefaultValue(40)]
        [Tooltip("На сколько блоков вверх проверяется наличие крыши над факелом/костром (больше = точнее, но дороже)")]
        public int FireMaxSkyCheckHeight;

        [Range(0f, 10f)]
        [DefaultValue(0.15f)]
        [Tooltip("Сколько 'прогресса тушения' (0-100) набегает за одну успешную попытку при максимальной силе дождя - меньше значение = дольше и разнообразнее по времени гаснут факелы/костры")]
        public float FireExtinguishRate;

        [Range(0f, 10f)]
        [DefaultValue(0.4f)]
        [Tooltip("Как быстро остывает накопленный прогресс тушения, если дождь прекратился или тайл оказался под крышей, не успев погаснуть")]
        public float FireExtinguishCooldownRate;

        [DefaultValue(true)]
        [Tooltip("Пускать дым/пар при тушении и по пути к нему")]
        public bool FireSpawnDust;
    }
}
