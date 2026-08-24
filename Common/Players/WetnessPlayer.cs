using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using WetnessMod.Common.Configs;
using WetnessMod.Common.GlobalItems;
using WetnessMod.Common.Systems;

namespace WetnessMod.Common.Players
{
    /// <summary>
    /// Каждый тик обходит надетые предметы игрока (шлем/грудь/ноги + аксессуары,
    /// player.armor[0..9]) и обновляет влажность, которая хранится прямо на объекте
    /// каждого предмета (см. WetnessGlobalItem) — а не в отдельном массиве по номеру
    /// слота. Это важно: значение остаётся с вещью, даже если её снять и убрать
    /// в инвентарь — снятая мокрая вещь не станет сухой мгновенно.
    /// </summary>
    public class WetnessPlayer : ModPlayer
    {
        public const int TrackedSlots = 10;

        // Аксессуары, которые логично отключаются при намокании (ботинки, крылья, прыжки, рывки)
        // Используются ТОЛЬКО числовые ID для 100% гарантии компиляции в любой версии tModLoader 1.4
        // Аксессуары, которые влияют на передвижение игрока.
        // Крылья сюда НЕ входят — они определяются автоматически через item.wingSlot.
        private static readonly HashSet<int> MovementAccessories = new()
        {
            // ============================================================
            // БОТИНКИ / БЕГ / СКОРОСТЬ / ПОВЕРХНОСТИ
            // ============================================================

            54,     // Hermes Boots
            3200,   // Sailfish Boots
            1579,   // Flurry Boots
            4055,   // Dunerider Boots
            405,    // Spectre Boots
            898,    // Lightning Boots
            1862,   // Frostspark Boots
            5000,   // Terraspark Boots
            128,    // Rocket Boots

            212,    // Anklet of the Wind
            285,    // Aglet

            950,    // Ice Skates

            // Водные / лавовые поверхности
            863,    // Water Walking Boots
            907,    // Obsidian Water Walking Boots
            908,    // Lava Waders

            // ============================================================
            // ДВОЙНОЙ ПРЫЖОК / ПРЫЖОК
            // ============================================================

            53,     // Cloud in a Bottle
            987,    // Blizzard in a Bottle
            857,    // Sandstorm in a Bottle
            3201,   // Tsunami in a Bottle
            1724,   // Fart in a Jar

            // ============================================================
            // ШАРИКИ
            // ============================================================

            159,    // Shiny Red Balloon
            399,    // Cloud in a Balloon
            1163,   // Blizzard in a Balloon
            983,    // Sandstorm in a Balloon
            1863,   // Fart in a Balloon
            1249,   // Honey Balloon

            1164,   // Bundle of Balloons

            // Horseshoe Balloons
            1250,   // Blue Horseshoe Balloon
            1251,   // White Horseshoe Balloon
            1252,   // Yellow Horseshoe Balloon

            // Дополнительные варианты
            3250,   // Green Horseshoe Balloon
            3251,   // Amber Horseshoe Balloon
            3252,   // Pink Horseshoe Balloon

            3225,   // Balloon Pufferfish
            3241,   // Sharkron Balloon
            5331,   // Bundle of Horseshoe Balloons

            // ============================================================
            // ЛАЗАНИЕ
            // ============================================================

            953,    // Climbing Claws
            975,    // Shoe Spikes
            976,    // Tiger Climbing Gear
            977,    // Tabi
            984,    // Master Ninja Gear

            // ============================================================
            // РЫВОК
            // ============================================================

            3097,   // Shield of Cthulhu

            // ============================================================
            // БЕСКОНЕЧНЫЙ ПОЛЁТ
            // ============================================================

            4989,   // Soaring Insignia
        };

        /// <summary>
        /// Универсальная проверка крыльев.
        /// Работает для ванильных и модовых крыльев.
        /// </summary>
        private static bool IsWingAccessory(Item item)
        {
            return item != null
                && !item.IsAir
                && item.accessory
                && item.wingSlot >= 0;
        }

        // Кешируем результат "открытого неба" раз в несколько тиков — это недешёвая проверка,
        // а погода не меняется мгновенно, так что легкая задержка незаметна.
        private int openSkyCheckCooldown = 0;
        private bool cachedOpenSky = false;

        private int campfireCheckCooldown = 0;
        private bool cachedNearCampfire = false;
        private bool cachedNearLitTorch = false;

        public override void PreUpdate()
        {
            WetnessConfig config = ModContent.GetInstance<WetnessConfig>();

            UpdateEnvironmentCache(config);

            bool inWater = Player.wet && !Player.lavaWet; // в лаве намокать нелогично
            bool raining = Main.raining && cachedOpenSky;
            bool inJungle = Player.ZoneJungle;

            // Вклад от погружения в воду и от дождя считаем ОТДЕЛЬНО.
            float waterContribution = inWater ? config.WaterWetRate : 0f;

            // --- НОВОЕ: Проверка активной защиты от зонта ---
            // Проверяем, держит ли игрок зонт в активной руке
            Item heldItem = Player.inventory[Player.selectedItem];
            bool holdingUmbrella = heldItem.type == ItemID.Umbrella || heldItem.type == ItemID.TragicUmbrella;
            
            // Проверяем, носит ли игрок шляпу-зонт в слоте тщеславия для головы (индекс 10)
            Item vanityHead = Player.armor[10];
            bool wearingUmbrellaHat = vanityHead != null && !vanityHead.IsAir && vanityHead.type == ItemID.UmbrellaHat;

            bool hasUmbrellaProtection = holdingUmbrella || wearingUmbrellaHat;
            // -------------------------------------------------

            // Если есть защита зонтом, вклад дождя равен 0 (100% защита от дождя).
            // Вода (погружение) при этом всё равно будет мочить, что логично.
            float rainContributionBase = (raining && !hasUmbrellaProtection) 
                ? config.RainWetRate * (inJungle ? 1.3f : 1f) 
                : 0f;

            // В зимнем биоме снег суше и рыхлее обычного дождя - меньше пропитывает одежду,
            // легче стряхивается. Раньше разницы между лесом и снежным биомом не было
            // вообще - теперь снег мочит в несколько раз МЕДЛЕННЕЕ, чем обычный дождь
            // (множитель по умолчанию 0.3, то есть в ~3 раза медленнее).
            if (rainContributionBase > 0f && Player.ZoneSnow)
            {
                rainContributionBase *= config.SnowWetRateMultiplier;
            }

            // Сколько из трёх слотов тщеславия (шлем/грудь/ноги) сейчас заняты непромокаемой
            // одеждой (дождевик, рыбацкий костюм) - эта логика остаётся на случай, 
            // если игрок убрал зонт, но оставил дождевик.
            int rainProofPieces = GetRainProofArmorPieceCount();
            float armorRainReduction = System.Math.Min(config.RainProtectionMaxArmor, config.RainProtectionPerPieceArmor * rainProofPieces);
            float accessoryRainReduction = System.Math.Min(config.RainProtectionMaxAccessory, config.RainProtectionPerPieceAccessory * rainProofPieces);

            float dryMultiplier = GetDryMultiplier(config);
            float dryDelta = config.BaseDryRate * dryMultiplier;

            for (int i = 0; i < TrackedSlots; i++)
            {
                Item equipped = Player.armor[i];
                if (equipped == null || equipped.IsAir)
                {
                    continue; // пустой слот — нечего обновлять
                }

                WetnessGlobalItem wetnessData = equipped.GetGlobalItem<WetnessGlobalItem>();
                float wet = wetnessData.Wetness;
                float itemMultiplier = wetnessData.GetRateMultiplier();

                // Небольшой случайный "шум" на каждый тик — чтобы даже у пары одинаковых
                // вещей намокание/высыхание ощущалось неравномерно, а не ровной линией,
                // и чтобы не совпадало один-в-один с персональным множителем предмета.
                float tickNoise = Main.rand.NextFloat(0.8f, 1.2f);

                bool isArmorSlot = i < 3;
                float rainReduction = isArmorSlot ? armorRainReduction : accessoryRainReduction;
                float rainContribution = rainContributionBase * (1f - rainReduction);
                float wetDelta = waterContribution + rainContribution;

                if (wetDelta > 0f)
                {
                    wet += wetDelta * itemMultiplier * tickNoise;
                }

                // Высыхание применяется, только если сейчас ничего не мочит вещь активно —
                // иначе дождь/вода "перекрывали" бы высыхание, а не наоборот. Непромокаемая
                // одежда на скорость высыхания сознательно никак не влияет.
                if (wetDelta <= 0f)
                {
                    wet -= dryDelta * itemMultiplier * tickNoise;
                }

                wet = MathHelper.Clamp(wet, 0f, 100f);
                wetnessData.Wetness = wet;

                UpdateWetDisableState(config, wetnessData, wet, equipped, i);
            }

            HideWetAccessories(config);
        }

        /// <summary>
        /// Обновляет "залипающее" состояние отключения из-за влажности.
        /// Симметричная механика:
        /// - Выше порога (50%): шанс отключения растёт с влажностью
        /// - Ниже порога (50%): шанс включения растёт с уменьшением влажности
        /// </summary>
        private void UpdateWetDisableState(WetnessConfig config, WetnessGlobalItem data, float wetness, Item item, int slot)
        {
            // Для аксессуаров (слоты 3-9) проверяем уязвимость
            if (slot >= 3 && slot < TrackedSlots)
            {
                if (!IsAccessoryVulnerableToWetness(item, config))
                {
                    return; // Этот аксессуар не отключается от воды (например, щит или эмблема)
                }
            }

            float threshold = config.AccessoryDisableThreshold; // по умолчанию 50%
            float range = System.Math.Max(1f, 100f - threshold);

            if (data.DisabledByWetness)
            {
                // Вещь отключена — проверяем шанс включения
                if (wetness < threshold)
                {
                    // Чем ниже влажность, тем выше шанс включения
                    // При 0% влажности шанс максимальный, при 50% — минимальный
                    float fraction = (threshold - wetness) / threshold; // 0..1 (0 при 50%, 1 при 0%)
                    float chancePerTick = config.WetDisableChancePerSecond * fraction / 60f;

                    if (Main.rand.NextFloat() < chancePerTick)
                    {
                        data.DisabledByWetness = false;
                    }
                }
                return;
            }

            // Вещь работает — проверяем шанс отключения
            if (wetness <= threshold)
            {
                return; // ещё недостаточно мокрая, чтобы рисковать отключением
            }

            // Чем выше влажность, тем выше шанс отключения
            // При 100% влажности шанс максимальный, при 50% — минимальный
            float disableFraction = (wetness - threshold) / range; // 0..1 (0 при 50%, 1 при 100%)
            float disableChancePerTick = config.WetDisableChancePerSecond * disableFraction / 60f;

            if (Main.rand.NextFloat() < disableChancePerTick)
            {
                data.DisabledByWetness = true;
            }
        }

        /// <summary>
        /// Определяет, должен ли аксессуар отключаться при намокании.
        /// </summary>
        private bool IsAccessoryVulnerableToWetness(Item item, WetnessConfig config)
        {
            // Хардкорный режим: отключается вообще всё.
            if (config.HardcoreAccessoryWetness)
                return true;

            if (item == null || item.IsAir || !item.accessory)
                return false;

            // Любые крылья (включая модовые).
            if (IsWingAccessory(item))
                return true;

            // Остальные аксессуары передвижения.
            return MovementAccessories.Contains(item.type);
        }

        // Сюда на время прячутся мокрые аксессуары, чтобы ванильный код их "не увидел"
        // и не применил эффект в этот тик (см. HideWetAccessories/RestoreHiddenAccessories).
        private readonly Item[] hiddenAccessories = new Item[TrackedSlots];
        private readonly Item[] hidingPlaceholders = new Item[TrackedSlots];

        /// <summary>
        /// Настоящее (а не выборочное) отключение эффекта мокрого аксессуара. Идея: ванильные
        /// и модовые эффекты аксессуаров применяются где-то между PreUpdate и PostUpdateEquips
        /// (именно тогда игра проходит по player.armor и вызывает UpdateAccessory/UpdateEquip
        /// у каждого предмета). Если непосредственно перед этим временно подменить слот на
        /// пустой предмет, игра для этого тика попросту не увидит аксессуар — а значит не
        /// применит вообще никакой его эффект, будь то полёт, ускорение или что угодно ещё,
        /// без необходимости вручную перечислять и сбрасывать поля Player по одному.
        /// Сразу после (в PostUpdateEquips) настоящий предмет возвращается на место, поэтому
        /// визуально, в тултипах и в инвентаре ничего не меняется — вещь просто "не работает"
        /// этот тик, но остаётся на месте.
        /// </summary>
        private void HideWetAccessories(WetnessConfig config)
        {
            for (int i = 3; i < TrackedSlots; i++)
            {
                Item item = Player.armor[i];
                if (item == null || item.IsAir)
                {
                    continue;
                }

                if (!IsItemDisabledByWetness(item))
                {
                    continue;
                }

                Item placeholder = new Item(); // пустышка вместо предмета на время этого тика
                hiddenAccessories[i] = item;
                hidingPlaceholders[i] = placeholder;
                Player.armor[i] = placeholder;
            }
        }

        /// <summary>
        /// Возвращает спрятанные аксессуары на место сразу после того, как ванильная игра
        /// применила эффекты экипировки. Проверяем по ссылке, что слот всё ещё содержит
        /// именно нашу пустышку — если что-то другое успело туда записаться (крайне редкий
        /// случай стороннего мода/интерфейса), не перетираем это чужое изменение, а возвращаем
        /// предмет игроку в инвентарь, чтобы он точно не потерялся.
        /// </summary>
        private void RestoreHiddenAccessories()
        {
            for (int i = 3; i < TrackedSlots; i++)
            {
                Item hidden = hiddenAccessories[i];
                if (hidden == null)
                {
                    continue;
                }

                if (Player.armor[i] == hidingPlaceholders[i])
                {
                    Player.armor[i] = hidden;
                }
                else
                {
                    // Слот успели изменить снаружи, пока предмет был спрятан - не перетираем,
                    // а подстраховываемся и просто отдаём вещь игроку, чтобы она не пропала.
                    Player.QuickSpawnItem(Player.GetSource_Misc("WetnessMod_RestoreAccessory"), hidden);
                }

                hiddenAccessories[i] = null;
                hidingPlaceholders[i] = null;
            }
        }

        // --- Непромокаемая одежда ---
        //
        // Список специально основан на реальных вещах из игры, которые тематически
        // "не промокают": дождевой костюм (Rain Hat/Rain Coat) и рыбацкий костюм от
        // Рыбака (Angler Hat/Vest/Pants - три предмета ровно на все три слота).
        // Зонтичная шляпа (Umbrella Hat) тоже добавлена в головной убор - тематически
        // подходит, хотя это чисто косметический предмет без защиты.
        //
        // Проверяются именно СЛОТЫ ТЩЕСЛАВИЯ (player.armor[10]=голова, [11]=тело, [12]=ноги),
        // а не боевая броня. Это принципиально: никто не станет снимать нормальную броню
        // с реальной защитой только ради того, чтобы не намокнуть под дождём - а слоты
        // тщеславия для того и существуют, чтобы носить что-то "для вида" поверх настоящей
        // экипировки без потери характеристик. Здесь этот же принцип используется для
        // получения защиты от дождя без каких-либо жертв в боевой броне.
        private static readonly System.Collections.Generic.HashSet<int> RainProofHeadItems = new()
        {
            ItemID.RainHat,
            ItemID.UmbrellaHat,
            ItemID.AnglerHat,
        };

        private static readonly System.Collections.Generic.HashSet<int> RainProofBodyItems = new()
        {
            ItemID.RainCoat,
            ItemID.AnglerVest,
        };

        private static readonly System.Collections.Generic.HashSet<int> RainProofLegItems = new()
        {
            ItemID.AnglerPants,
        };

        // Индексы слотов тщеславия в player.armor: 10=голова, 11=тело, 12=ноги
        // (0-2 - боевая броня, 3-9 - аксессуары, 10-12 - тщеславие-броня, 13-19 - тщеславие-аксессуары).
        private const int VanityHeadSlot = 10;
        private const int VanityBodySlot = 11;
        private const int VanityLegSlot = 12;

        /// <summary>
        /// Считает, сколько из трёх слотов ТЩЕСЛАВИЯ (голова/тело/ноги) сейчас заняты
        /// непромокаемой одеждой из соответствующего списка. Возвращает 0..3.
        /// </summary>
        private int GetRainProofArmorPieceCount()
        {
            int count = 0;

            Item head = Player.armor[VanityHeadSlot];
            if (head != null && !head.IsAir && RainProofHeadItems.Contains(head.type))
            {
                count++;
            }

            Item body = Player.armor[VanityBodySlot];
            if (body != null && !body.IsAir && RainProofBodyItems.Contains(body.type))
            {
                count++;
            }

            Item legs = Player.armor[VanityLegSlot];
            if (legs != null && !legs.IsAir && RainProofLegItems.Contains(legs.type))
            {
                count++;
            }

            return count;
        }

        private void UpdateEnvironmentCache(WetnessConfig config)
        {
            if (openSkyCheckCooldown <= 0)
            {
                cachedOpenSky = IsUnderOpenSky();
                openSkyCheckCooldown = 30; // проверяем раз в полсекунды
            }
            else
            {
                openSkyCheckCooldown--;
            }

            if (campfireCheckCooldown <= 0)
            {
                ScanForNearbyFireSources(config, out cachedNearCampfire, out cachedNearLitTorch);
                campfireCheckCooldown = 30;
            }
            else
            {
                campfireCheckCooldown--;
            }
        }

        /// <summary>
        /// Проверяет, находится ли игрок под открытым небом (дождь может его намочить).
        /// Если игрок под землёй (ниже поверхности мира) - дождь его не достанет,
        /// даже если над ним есть пещеры с открытым потолком.
        /// </summary>
        private bool IsUnderOpenSky()
        {
            if (Player.ZoneUnderworldHeight)
            {
                return false; // в аду дождя нет в принципе
            }

            int tileX = (int)(Player.Center.X / 16f);
            int tileY = (int)(Player.position.Y / 16f);

            // Проверяем, находится ли игрок ниже поверхности мира
            // Если да - дождь не может его намочить, даже если над ним пещера
            double surfaceLevel = Main.worldSurface;
            if (tileY > surfaceLevel + 10) // +10 для небольшого буфера
            {
                return false; // игрок под землёй, дождь не достанет
            }

            // Игрок на поверхности - проверяем наличие крыши над головой
            for (int y = tileY; y >= 10; y--)
            {
                if (tileX < 0 || tileX >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
                {
                    continue;
                }

                Tile tile = Main.tile[tileX, y];
                if (tile == null)
                {
                    continue;
                }

                if (tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
                {
                    return false; // нашли крышу/потолок над головой
                }
            }

            return true;
        }

        /// <summary>
        /// Ищет реально горящие костры/кузницы и факелы рядом с игроком. В отличие от
        /// прежней версии (которая полагалась на ванильный бафф "Уютный огонь" - тот
        /// срабатывает от костра почти в любой точке экрана, даже далеко или за стеной),
        /// здесь используются два честных условия одновременно:
        ///  1) реальное расстояние в блоках (FireWarmthDetectionRadius),
        ///  2) прямая видимость без препятствий (Collision.CanHitLine) - костёр за стеной
        ///     не считается, даже если он в радиусе.
        /// А "горит ли конкретно этот тайл прямо сейчас" проверяется через
        /// FireExtinguishSystem.IsExtinguishedByUs - то есть костёр/факел, потушенный
        /// дождём или проводкой, греть не будет, даже если стоит совсем рядом.
        /// Кузница/хефай считаются всегда "включёнными", так как у них нет состояния
        /// вкл/выкл.
        /// </summary>
        private void ScanForNearbyFireSources(WetnessConfig config, out bool nearCampfire, out bool nearLitTorch)
        {
            nearCampfire = false;
            nearLitTorch = false;

            int radiusTiles = config.FireWarmthDetectionRadius;
            int tileX = (int)(Player.Center.X / 16f);
            int tileY = (int)(Player.Center.Y / 16f);

            for (int x = tileX - radiusTiles; x <= tileX + radiusTiles; x++)
            {
                for (int y = tileY - radiusTiles; y <= tileY + radiusTiles; y++)
                {
                    if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
                    {
                        continue;
                    }

                    Tile tile = Main.tile[x, y];
                    if (!tile.HasTile)
                    {
                        continue;
                    }

                    bool isCampfireTile = tile.TileType == TileID.Campfire;
                    bool isForgeTile = tile.TileType == TileID.Hellforge || tile.TileType == TileID.AdamantiteForge;
                    bool isTorchTile = tile.TileType == TileID.Torches;

                    if (!isCampfireTile && !isForgeTile && !isTorchTile)
                    {
                        continue;
                    }

                    // Кузница/хефай всегда "горят" - у них нет состояния вкл/выкл.
                    // Костёр и факел можно потушить (дождём или проводкой) - тогда греть не будут.
                    bool consideredLit = isForgeTile || !FireExtinguishSystem.IsExtinguishedByUs(x, y);
                    if (!consideredLit)
                    {
                        continue;
                    }

                    Vector2 tileCenter = new Vector2(x * 16 + 8, y * 16 + 8);
                    if (!Collision.CanHitLine(Player.Center, 1, 1, tileCenter, 1, 1))
                    {
                        continue; // перекрыто стеной - не считается, даже если в радиусе
                    }

                    if (isCampfireTile || isForgeTile)
                    {
                        nearCampfire = true;
                    }
                    else
                    {
                        nearLitTorch = true;
                    }

                    if (nearCampfire && nearLitTorch)
                    {
                        return; // нашли всё, что нужно - дальше сканировать незачем
                    }
                }
            }
        }

        /// <summary>
        /// Итоговый множитель скорости высыхания зависит от места, где сейчас находится игрок.
        /// Порядок проверок важен: сначала самые "сильные" условия (ад, костёр рядом), потом
        /// открытое небо (солнечно/пасмурно) или закрытое помещение/подземелье, и только в
        /// конце учитывается факел рядом - но лишь как "не хуже, чем сейчас": если на улице
        /// и так солнечно (уже быстрее, чем от факела), факел ничего не меняет; а вот в
        /// помещении или под землёй он даёт заметный плюс к тому, что было бы без него.
        /// </summary>
        private float GetDryMultiplier(WetnessConfig config)
        {
            if (Player.ZoneUnderworldHeight)
            {
                return config.UnderworldDryMultiplier;
            }

            if (cachedNearCampfire)
            {
                return config.CampfireDryMultiplier;
            }

            float baseMultiplier;
            if (cachedOpenSky)
            {
                // Main.cloudBGActive - это float (сила альфа-канала облачного фона), а не bool,
                // поэтому сравниваем с порогом, а не отрицаем напрямую.
                bool sunnyOutside = !Main.raining && Main.dayTime && Main.cloudBGActive <= 0f;
                baseMultiplier = sunnyOutside ? config.SunnyDryMultiplier : config.CloudyOrNightDryMultiplier;
            }
            else
            {
                // Нет открытого неба и нет костра рядом - это самые медленные варианты.
                // Глубоко под землёй воздух застойный и сырой, поэтому сохнет ещё медленнее,
                // чем просто в помещении/под крышей на поверхности.
                bool underground = Player.position.Y > Main.worldSurface * 16.0;
                baseMultiplier = underground ? config.UndergroundDryMultiplier : config.ShadeDryMultiplier;
            }

            if (cachedNearLitTorch && config.TorchDryMultiplier > baseMultiplier)
            {
                return config.TorchDryMultiplier;
            }

            return baseMultiplier;
        }

        /// <summary>
        /// Штраф к защите от мокрой брони + возврат спрятанных мокрых аксессуаров на место.
        /// Применяется здесь, а не в PreUpdate, чтобы точно сработать после того, как
        /// ванильная броня/аксессуары уже применили свои эффекты за этот тик.
        /// </summary>
        public override void PostUpdateEquips()
        {
            WetnessConfig config = ModContent.GetInstance<WetnessConfig>();

            for (int i = 0; i < 3; i++) // только слоты брони: шлем, грудь, ноги
            {
                Item armorPiece = Player.armor[i];
                if (armorPiece == null || armorPiece.IsAir || armorPiece.defense <= 0)
                {
                    continue;
                }

                WetnessGlobalItem data = armorPiece.GetGlobalItem<WetnessGlobalItem>();

                int loss;
                if (data.DisabledByWetness)
                {
                    // Броня "сдалась" из-за сырости - штраф закреплён на максимуме и не
                    // плавает вместе с текущей влажностью, пока вещь не высохнет полностью
                    // (см. UpdateWetDisableState) - точно так же, как отключённый аксессуар.
                    loss = (int)(armorPiece.defense * config.MaxArmorDefenseLossFraction);
                }
                else
                {
                    // Пока порог не пройден или броне "повезло" не сдаться - штраф плавно
                    // растёт вместе с текущей влажностью, как и раньше.
                    float wetFraction = data.Wetness / 100f;
                    loss = (int)(armorPiece.defense * wetFraction * config.MaxArmorDefenseLossFraction);
                }

                if (loss > 0)
                {
                    Player.statDefense -= loss;
                }
            }

            RestoreHiddenAccessories();
        }

        /// <summary>
        /// Проверка "предмет сейчас отключён из-за влажности". Используется и для решения
        /// прятать ли аксессуар в HideWetAccessories, и для показа статуса в тултипе/иконке.
        /// Это больше не прямое сравнение "Wetness >= порог" - см. UpdateWetDisableState.
        /// </summary>
        public static bool IsItemDisabledByWetness(Item item)
        {
            return item.GetGlobalItem<WetnessGlobalItem>().DisabledByWetness;
        }

        /// <summary>
        /// Небольшой бонус сверху задания: во время дождя грязь под ногами немного
        /// замедляет — чтобы превращение земли в грязь ощущалось не только визуально.
        /// </summary>
        public override void PostUpdateRunSpeeds()
        {
            if (!Main.raining)
            {
                return;
            }

            int tileX = (int)(Player.Center.X / 16f);
            int tileY = (int)((Player.position.Y + Player.height + 2) / 16f);

            if (tileX < 0 || tileX >= Main.maxTilesX || tileY < 0 || tileY >= Main.maxTilesY)
            {
                return;
            }

            Tile below = Main.tile[tileX, tileY];
            if (below.HasTile && below.TileType == TileID.Mud)
            {
                Player.moveSpeed *= 0.85f;
                Player.maxRunSpeed *= 0.85f;
            }
        }

        /// <summary>
        /// Визуальный эффект: капли стекают с мокрых предметов экипировки.
        /// Чем сильнее промокла вещь, тем больше с неё капает.
        /// Работает только на клиенте.
        /// </summary>
        public override void PostUpdate()
        {
            if (Main.dedServ)
            {
                return;
            }

            WetnessConfig config = ModContent.GetInstance<WetnessConfig>();

            // Собираем информацию о всех мокрых предметах (броня + аксессуары)
            List<(int slot, float wetness)> wetItems = new List<(int, float)>();
            
            for (int i = 0; i < TrackedSlots; i++)
            {
                Item item = Player.armor[i];
                if (item == null || item.IsAir)
                {
                    continue;
                }

                float wet = item.GetGlobalItem<WetnessGlobalItem>().Wetness;
                if (wet > 20f) // Начинаем показывать капли только при заметной влажности
                {
                    wetItems.Add((i, wet));
                }
            }

            if (wetItems.Count == 0)
            {
                return;
            }

            if (config.WaterDripEnabled)
            {
                SpawnWaterDrips(wetItems);
            }

            if (config.DisabledItemSparkleEnabled)
            {
                SpawnDisabledItemSparkles();
            }
        }

        private void SpawnWaterDrips(List<(int slot, float wetness)> wetItems)
        {
            // Ограничиваем количество капель за тик
            int maxDropsPerTick = 2;
            int dropsSpawned = 0;

            foreach (var (slot, wetness) in wetItems)
            {
                if (dropsSpawned >= maxDropsPerTick)
                {
                    break;
                }

                // Шанс капли пропорционален влажности
                float dropChance = (wetness - 20f) / 80f;
                
                if (Main.rand.NextFloat() >= dropChance * 0.08f) // Ещё реже (было 0.12f)
                {
                    continue;
                }

                Vector2 dropPosition = GetDropPosition(slot);

                // Маленькие, медленные капли
                Dust drop = Dust.NewDustDirect(
                    dropPosition,
                    2, 2, // Маленький размер (было 6x6)
                    DustID.Water,
                    0f, 0.3f, // Очень медленное падение (было 0.8f)
                    180,
                    new Color(120, 180, 255),
                    0.6f // Маленький масштаб (было 1.2f)
                );
                
                drop.noGravity = false;
                drop.velocity.Y = 0.3f; // Фиксированная медленная скорость
                drop.velocity.X = Main.rand.NextFloat(-0.1f, 0.1f); // Почти без отклонения
                
                dropsSpawned++;
            }
        }

        /// <summary>
        /// Морозный блеск на месте слота реально отключённого предмета - можно полностью
        /// выключить через WetnessConfig.DisabledItemSparkleEnabled, если эффект не нравится
        /// (обычные капли воды от WaterDripEnabled это не затрагивает).
        /// </summary>
        private void SpawnDisabledItemSparkles()
        {
            for (int i = 0; i < TrackedSlots; i++)
            {
                Item item = Player.armor[i];
                if (item == null || item.IsAir)
                {
                    continue;
                }

                if (!item.GetGlobalItem<WetnessGlobalItem>().DisabledByWetness)
                {
                    continue;
                }

                if (Main.rand.NextFloat() >= 0.1f)
                {
                    continue;
                }

                Vector2 pos = GetDropPosition(i);
                Dust malfunctionDust = Dust.NewDustDirect(
                    pos,
                    6, 6,
                    DustID.Frost,
                    0f, -0.6f,
                    150,
                    default,
                    1.3f
                );
                malfunctionDust.noGravity = true;
                malfunctionDust.velocity *= 0.3f;
                malfunctionDust.fadeIn = 1.2f;
            }
        }

        /// <summary>
        /// Возвращает позицию для капли в зависимости от слота экипировки.
        /// Капли стекают с конкретных частей тела, где надеты мокрые предметы.
        /// </summary>
        private Vector2 GetDropPosition(int slot)
        {
            Vector2 basePos = Player.position;
            
            switch (slot)
            {
                case 0: // Шлем - капли с головы
                    return new Vector2(basePos.X + Player.width / 2f - 2f, basePos.Y - 2f);
                case 1: // Нагрудник - капли с торса
                    return new Vector2(basePos.X + Player.width / 2f - 2f, basePos.Y + 12f);
                case 2: // Поножи - капли с ног
                    return new Vector2(basePos.X + Player.width / 2f - 2f, basePos.Y + 26f);
                case 3:
                case 4: // Аксессуары слева - капли с левой стороны
                    return new Vector2(basePos.X + Player.width / 2f - 10f, basePos.Y + 16f);
                case 5:
                case 6: // Аксессуары справа - капли с правой стороны
                    return new Vector2(basePos.X + Player.width / 2f + 6f, basePos.Y + 16f);
                case 7:
                case 8:
                case 9: // Остальные аксессуары - капли снизу
                    return new Vector2(basePos.X + Player.width / 2f - 2f, basePos.Y + 22f);
                default:
                    return new Vector2(basePos.X + Player.width / 2f - 2f, basePos.Y + 10f);
            }
        }
    }
}
