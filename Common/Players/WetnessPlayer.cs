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

            float wetDelta = 0f;
            if (inWater)
            {
                wetDelta += config.WaterWetRate;
            }
            if (raining)
            {
                // Дождь в джунглях мочит немного сильнее — гуще листва, крупнее капли
                wetDelta += config.RainWetRate * (inJungle ? 1.3f : 1f);
            }

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

                if (wetDelta > 0f)
                {
                    wet += wetDelta;
                }

                // Фоновая влажность джунглей действует независимо от дождя, но имеет потолок
                if (inJungle && !raining && !inWater)
                {
                    if (wet < config.JungleAmbientWetCap)
                    {
                        wet += config.JungleAmbientWetRate;
                        if (wet > config.JungleAmbientWetCap)
                        {
                            wet = config.JungleAmbientWetCap;
                        }
                    }
                }

                // Высыхание применяется, только если сейчас ничего не мочит вещь активно —
                // иначе дождь/вода "перекрывали" бы высыхание, а не наоборот.
                if (wetDelta <= 0f)
                {
                    wet -= dryDelta;
                }

                wetnessData.Wetness = MathHelper.Clamp(wet, 0f, 100f);
            }
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

            bool underground = !cachedOpenSky && Player.position.Y > Main.worldSurface * 16.0;
            if (underground)
            {
                return config.UndergroundDryMultiplier;
            }

            // Main.cloudBGActive - это float (сила альфа-канала облачного фона), а не bool,
            // поэтому сравниваем с порогом, а не отрицаем напрямую.
            bool sunnyOutside = cachedOpenSky && !Main.raining && Main.dayTime && Main.cloudBGActive <= 0f;
            if (sunnyOutside)
            {
                return config.SunnyDryMultiplier;
            }

            return config.CloudyOrNightDryMultiplier;
        }

        /// <summary>
        /// Штраф к защите от мокрой брони. Применяется здесь, а не в PreUpdate,
        /// чтобы точно сработать после того, как ванильная броня уже посчитала базовую защиту.
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

                float wetFraction = armorPiece.GetGlobalItem<WetnessGlobalItem>().Wetness / 100f;
                int loss = (int)(armorPiece.defense * wetFraction * config.MaxArmorDefenseLossFraction);
                if (loss > 0)
                {
                    Player.statDefense -= loss;
                }
            }
        }

        /// <summary>
        /// Проверка "предмет сейчас отключён из-за влажности".
        /// Полноценно генерически отключить эффект ЛЮБОГО ванильного аксессуара средствами
        /// tModLoader нельзя — их эффекты применяются напрямую в коде самой игры через
        /// публичные поля Player (canFly, waterWalk, doubleJump и т.д.), и tModLoader не даёт
        /// единого хука "отмени эффект этого предмета". Поэтому:
        ///  - для СВОИХ будущих аксессуаров мод-предметов — проверяйте этот метод прямо в их
        ///    UpdateAccessory(Player player, bool hideVisual) и просто не применяйте эффект;
        ///  - для ключевых ванильных аксессуаров ниже реализован набор "ручных" сбросов полей
        ///    как рабочий пример, который можно расширять.
        /// </summary>
        public static bool IsItemDisabledByWetness(Item item)
        {
            WetnessConfig config = ModContent.GetInstance<WetnessConfig>();
            return item.GetGlobalItem<WetnessGlobalItem>().Wetness >= config.AccessoryDisableThreshold;
        }

        /// <summary>
        /// Пример ручного отключения эффекта нескольких известных ванильных аксессуаров,
        /// когда они полностью намокли. Список специально небольшой и служит образцом —
        /// расширяйте его по мере необходимости своими айтемами/полями.
        /// </summary>
        public override void PostUpdateMiscEffects()
        {
            for (int i = 3; i < TrackedSlots; i++)
            {
                Item accessory = Player.armor[i];
                if (accessory == null || accessory.IsAir)
                {
                    continue;
                }

                if (!IsItemDisabledByWetness(accessory))
                {
                    continue;
                }

                if (accessory.type == ItemID.CloudinaBottle || accessory.type == ItemID.CloudinaBalloon)
                {
                    Player.jumpBoost = false;
                    Player.extraFall = 0;
                }
                else if (accessory.type == ItemID.HermesBoots)
                {
                    Player.moveSpeed -= 0.15f; // грубая компенсация ускорения от ботинок
                }
                else if (accessory.type == ItemID.ShinyRedBalloon)
                {
                    Player.jumpSpeedBoost -= 0.5f;
                }
                // Дальше можно добавлять другие аксессуары по такому же принципу.
            }
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
