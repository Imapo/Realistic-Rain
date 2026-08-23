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

        // Кешируем результат "открытого неба" раз в несколько тиков — это недешёвая проверка,
        // а погода не меняется мгновенно, так что легкая задержка незаметна.
        private int openSkyCheckCooldown = 0;
        private bool cachedOpenSky = false;

        private int campfireCheckCooldown = 0;
        private bool cachedNearCampfire = false;

        public override void PreUpdate()
        {
            WetnessConfig config = ModContent.GetInstance<WetnessConfig>();

            UpdateEnvironmentCache();

            bool inWater = Player.wet && !Player.lavaWet; // в лаве намокать нелогично
            bool raining = Main.raining && cachedOpenSky;
            bool inJungle = Player.ZoneJungle;

            // Вклад от погружения в воду и от дождя считаем ОТДЕЛЬНО: непромокаемая одежда
            // (плащ, рыбацкий костюм и т.д.) защищает именно от дождя, а не от того, что
            // персонаж с головой залез в озеро - защита от дождя на подводное намокание
            // не распространяется.
            float waterContribution = inWater ? config.WaterWetRate : 0f;
            float rainContributionBase = raining ? config.RainWetRate * (inJungle ? 1.3f : 1f) : 0f;

            // Сколько из трёх слотов брони (шлем/грудь/ноги) сейчас заняты непромокаемой
            // одеждой - от этого зависит, насколько сильно ослабляется вклад дождя отдельно
            // для брони и отдельно для аксессуаров (см. GetRainProofArmorPieceCount).
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

                // Фоновая влажность джунглей действует независимо от дождя, но имеет потолок
                // (непромокаемая одежда от дождя не защищает от общей сырости джунглей)
                if (inJungle && !raining && !inWater)
                {
                    if (wet < config.JungleAmbientWetCap)
                    {
                        wet += config.JungleAmbientWetRate * itemMultiplier * tickNoise;
                        if (wet > config.JungleAmbientWetCap)
                        {
                            wet = config.JungleAmbientWetCap;
                        }
                    }
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

                UpdateWetDisableState(config, wetnessData, wet);
            }

            HideWetAccessories(config);
        }

        /// <summary>
        /// Обновляет "залипающее" состояние отключения из-за влажности (гистерезис).
        /// Раньше вещь считалась отключённой ровно тогда, когда текущая влажность >= порога,
        /// и поэтому включалась обратно мгновенно, как только влажность опускалась чуть ниже —
        /// в частности, сразу же после конца дождя, что и было основной жалобой на старое
        /// поведение. Теперь это два разных события:
        ///
        /// 1) Пока вещь ещё не отключена и её влажность выше AccessoryDisableThreshold (по
        ///    умолчанию 50%), каждую секунду есть шанс, что она "сдастся" и перестанет
        ///    работать. Чем ближе влажность к 100%, тем этот шанс выше (линейно от 0 у порога
        ///    до WetDisableChancePerSecond у 100%).
        /// 2) Если вещь уже отключена, она остаётся отключённой независимо от того, как
        ///    дальше колеблется текущая влажность (в том числе если дождь кончился и
        ///    влажность пошла вниз) — и включается обратно только тогда, когда высохнет
        ///    полностью, то есть влажность дойдёт до 0.
        /// </summary>
        private void UpdateWetDisableState(WetnessConfig config, WetnessGlobalItem data, float wetness)
        {
            if (data.DisabledByWetness)
            {
                if (wetness <= 0.01f)
                {
                    data.DisabledByWetness = false;
                }
                return; // пока не высохло полностью — не включаем обратно
            }

            if (wetness <= config.AccessoryDisableThreshold)
            {
                return; // ещё недостаточно мокрая, чтобы вообще рисковать отключением
            }

            float range = System.Math.Max(1f, 100f - config.AccessoryDisableThreshold);
            float fraction = (wetness - config.AccessoryDisableThreshold) / range; // 0..1
            float chancePerTick = config.WetDisableChancePerSecond * fraction / 60f; // тиков в секунде

            if (Main.rand.NextFloat() < chancePerTick)
            {
                data.DisabledByWetness = true;
            }
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

        private void UpdateEnvironmentCache()
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
                cachedNearCampfire = IsNearCampfireOrWarmth();
                campfireCheckCooldown = 30;
            }
            else
            {
                campfireCheckCooldown--;
            }
        }

        /// <summary>
        /// Грубая, но дешёвая проверка "есть ли открытое небо над игроком":
        /// идём вверх по колонке тайлов от позиции игрока и ищем твёрдый блок.
        /// Не учитывает ширину крыши (как в ванильном дожде), но для механики намокания
        /// этого достаточно и гораздо дешевле по производительности.
        /// </summary>
        private bool IsUnderOpenSky()
        {
            if (Player.ZoneUnderworldHeight)
            {
                return false; // в аду дождя нет в принципе
            }

            int tileX = (int)(Player.Center.X / 16f);
            int tileY = (int)(Player.position.Y / 16f);

            for (int y = tileY; y >= 10; y--)
            {
                if (tileX < 0 || tileX >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
                {
                    continue;
                }

                Tile tile = Main.tile[tileX, y];
                if (tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
                {
                    return false; // нашли крышу/потолок над головой
                }
            }

            return true;
        }

        /// <summary>
        /// Ищет зажжённый костёр (или похожий источник тепла) в радиусе вокруг игрока.
        /// Используется как "укрытие для сушки".
        /// </summary>
        private bool IsNearCampfireOrWarmth()
        {
            int radiusTiles = 8;
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

                    // Раньше здесь была проверка tile.TileFrameX < 66 как признак "костёр горит" -
                    // это было непроверенное предположение о формате кадров спрайта, и оно
                    // могло быть попросту неверным. Правильный способ узнать, что конкретный
                    // костёр сейчас потушен - спросить у FireExtinguishSystem, которая явно
                    // отслеживает потушенные ею тайлы через штатный игровой API переключения.
                    if (tile.TileType == TileID.Campfire && !FireExtinguishSystem.IsExtinguishedByUs(x, y))
                    {
                        return true;
                    }

                    // Хефай / кузница тоже источник тепла, чтобы сушиться можно было и в базе
                    if (tile.TileType == TileID.Hellforge || tile.TileType == TileID.AdamantiteForge)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Итоговый множитель скорости высыхания зависит от места, где сейчас находится игрок.
        /// Порядок проверок важен: сначала самые "сильные" условия (ад, костёр рядом), потом
        /// открытое небо (солнечно/пасмурно), и только затем — закрытые пространства, где
        /// высыхание идёт заметно медленнее всего, особенно глубоко под землёй без костра.
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

            if (cachedOpenSky)
            {
                // Main.cloudBGActive - это float (сила альфа-канала облачного фона), а не bool,
                // поэтому сравниваем с порогом, а не отрицаем напрямую.
                bool sunnyOutside = !Main.raining && Main.dayTime && Main.cloudBGActive <= 0f;
                return sunnyOutside ? config.SunnyDryMultiplier : config.CloudyOrNightDryMultiplier;
            }

            // Нет открытого неба и нет костра рядом - это самые медленные варианты.
            // Глубоко под землёй воздух застойный и сырой, поэтому сохнет ещё медленнее,
            // чем просто в помещении/под крышей на поверхности.
            bool underground = Player.position.Y > Main.worldSurface * 16.0;
            return underground ? config.UndergroundDryMultiplier : config.ShadeDryMultiplier;
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
        /// Чисто визуальный бонус: с намокшей брони капает вода. Работает только на клиенте.
        /// </summary>
        public override void PostUpdate()
        {
            if (Main.dedServ)
            {
                return;
            }

            float maxWet = 0f;
            for (int i = 0; i < 3; i++)
            {
                Item armorPiece = Player.armor[i];
                if (armorPiece == null || armorPiece.IsAir)
                {
                    continue;
                }

                float wet = armorPiece.GetGlobalItem<WetnessGlobalItem>().Wetness;
                if (wet > maxWet)
                {
                    maxWet = wet;
                }
            }

            if (maxWet < 40f)
            {
                return;
            }

            // Чем мокрее броня, тем чаще падают капли. При 100% — почти каждый кадр есть шанс.
            float dropChance = (maxWet - 40f) / 60f; // 0..1
            if (Main.rand.NextFloat() < dropChance * 0.06f)
            {
                Dust drop = Dust.NewDustDirect(
                    Player.position,
                    Player.width,
                    Player.height,
                    DustID.Water,
                    0f, 1.5f,
                    100,
                    default,
                    1f
                );
                drop.noGravity = false;
                drop.velocity.Y += 1f;
            }
        }
    }
}
