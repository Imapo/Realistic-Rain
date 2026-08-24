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
        [Tooltip("Скорость намокания под дождём (% в тик) - снижена, чтобы одежда мокла заметно дольше")]
        public float RainWetRate;

        [Range(0f, 5f)]
        [DefaultValue(0.6f)]
        [Tooltip("Скорость намокания при погружении в воду (% в тик) - тоже снижена, хотя погружение всё ещё мочит быстрее дождя")]
        public float WaterWetRate;

        [Range(0.05f, 1f)]
        [DefaultValue(0.001f)]
        [Tooltip("Во сколько раз медленнее мокнет одежда под снегом в зимнем биоме по сравнению с обычным дождём (0.3 = в ~3 раза медленнее) - сухой снег стряхивается с одежды, а не сразу пропитывает её, в отличие от дождя")]
        public float SnowWetRateMultiplier;

        [Header("Drying")]

        [Range(0f, 5f)]
        [DefaultValue(0.018f)]
        [Tooltip("Базовая скорость высыхания (% в тик), дальше умножается на модификаторы погоды/места. Специально снижена, чтобы полное высыхание ощутимо занимало время, а не пару секунд")]
        public float BaseDryRate;

        [Range(0.1f, 10f)]
        [DefaultValue(2.5f)]
        [Tooltip("Множитель высыхания на открытом небе, ясно, днём, без дождя - самый быстрый вариант")]
        public float SunnyDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(0.5f)]
        [Tooltip("Множитель высыхания на открытом небе, но ночью/пасмурно (солнца нет, но воздух свежий)")]
        public float CloudyOrNightDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(0.2f)]
        [Tooltip("Множитель высыхания в помещении/под крышей на поверхности (нет открытого неба, нет костра рядом, но и не глубоко под землёй)")]
        public float ShadeDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(0.08f)]
        [Tooltip("Множитель высыхания глубоко под землёй без костра рядом - самый медленный вариант из всех (застоявшийся сырой воздух)")]
        public float UndergroundDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(3f)]
        [Tooltip("Множитель высыхания рядом с зажжённым костром/кузницей/хеллфорджем - перебивает все остальные условия")]
        public float CampfireDryMultiplier;

        [Range(0.1f, 10f)]
        [DefaultValue(1.6f)]
        [Tooltip("Множитель высыхания рядом с горящим факелом - слабее костра, но помогает там, где без него было бы медленно (в помещении/под землёй). Если обычные условия и так лучше (например, солнце на улице), факел ничего не меняет")]
        public float TorchDryMultiplier;

        [Range(1, 100)]
        [DefaultValue(10)]
        [Tooltip("Радиус в блоках, в котором ищутся горящие костры/факелы/кузницы для ускорения высыхания. Тайл дополнительно должен быть в прямой видимости - через стену не считается")]
        public int FireWarmthDetectionRadius;

        [Range(0.1f, 10f)]
        [DefaultValue(3f)]
        [Tooltip("Множитель высыхания в аду (там дождя не бывает, а жара сушит быстро)")]
        public float UnderworldDryMultiplier;

        [Header("EquipmentEffects")]

        [Range(0f, 1f)]
        [DefaultValue(0.4f)]
        [Tooltip("Максимальная доля защиты, которую теряет броня, если она 'сдалась' из-за влажности (см. WetDisableChancePerSecond)")]
        public float MaxArmorDefenseLossFraction;

        [Range(1f, 99f)]
        [DefaultValue(50f)]
        [Tooltip("Влажность (%), после которой у брони/аксессуара начинает появляться шанс полностью отключиться из-за сырости. Ниже этого порога вещь работает как обычно")]
        public float AccessoryDisableThreshold;

        [Range(0f, 1f)]
        [DefaultValue(0.15f)]
        [Tooltip("Шанс 'сдаться' и отключиться из-за влажности за одну секунду при 100% влажности. У порога (AccessoryDisableThreshold) шанс почти нулевой и линейно растёт до этого значения к 100% влажности. Как только вещь отключилась - она не заработает обратно, пока не высохнет полностью (0%), даже если влажность потом снова упадёт ниже порога")]
        public float WetDisableChancePerSecond;

        [Header("RainProtection")]

        [Range(0f, 1f)]
        [DefaultValue(0.33f)]
        [Tooltip("Насколько снижается вклад дождя в намокание БРОНИ за каждую надетую непромокаемую вещь (Rain Hat/Coat, Angler Hat/Vest/Pants и т.п.) в слотах шлема/тела/ног")]
        public float RainProtectionPerPieceArmor;

        [Range(0f, 1f)]
        [DefaultValue(0.99f)]
        [Tooltip("Максимальное суммарное снижение вклада дождя в намокание брони (при всех 3 непромокаемых предметах)")]
        public float RainProtectionMaxArmor;

        [Range(0f, 1f)]
        [DefaultValue(0.30f)]
        [Tooltip("Насколько снижается вклад дождя в намокание АКСЕССУАРОВ за каждую надетую непромокаемую вещь в слотах брони")]
        public float RainProtectionPerPieceAccessory;

        [Range(0f, 1f)]
        [DefaultValue(0.90f)]
        [Tooltip("Максимальное суммарное снижение вклада дождя в намокание аксессуаров (при всех 3 непромокаемых предметах)")]
        public float RainProtectionMaxAccessory;

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

        [Header("Puddles")]

        [DefaultValue(true)]
        [Tooltip("Класть немного настоящей воды поверх свежепоявившейся грязи - настоящая жидкость в игре уже умеет отражать небо и покачиваться, так что получается честный эффект блестящей лужи без всяких шейдеров")]
        public bool PuddleEnabled;

        [Range(0f, 1f)]
        [DefaultValue(0.5f)]
        [Tooltip("Шанс, что на конкретном свежем блоке грязи появится лужа (не на каждом подряд, иначе будет выглядеть как сплошное болото)")]
        public float PuddleChance;

        [Range(1, 255)]
        [DefaultValue(40)]
        [Tooltip("Минимальное количество жидкости в луже (0-255, как в ванильной игре) - маленькие значения дают мелкую, едва заметную лужицу")]
        public int PuddleMinLiquidAmount;

        [Range(1, 255)]
        [DefaultValue(110)]
        [Tooltip("Максимальное количество жидкости в луже (0-255) - помните, что 255 - это уже полноценный блок воды, а не мелкая лужа")]
        public int PuddleMaxLiquidAmount;

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

        [Header("VisualEffects")]

        [DefaultValue(true)]
        [Tooltip("Показывать капли воды с мокрой брони/аксессуаров")]
        public bool WaterDripEnabled;

        [DefaultValue(false)]
        [Tooltip("Показывать морозный блеск на месте слота предмета, который прямо сейчас отключён из-за влажности (DisabledByWetness). По умолчанию выключено - можно включить, если захочется более явную индикацию отказавших вещей")]
        public bool DisabledItemSparkleEnabled;
    }
}
