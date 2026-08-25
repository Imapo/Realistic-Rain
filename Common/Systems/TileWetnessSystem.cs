using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using WetnessMod.Common.Configs;

namespace WetnessMod.Common.Systems
{
    /// <summary>
    /// Отслеживает влажность блоков земли/травы в горизонтальной полосе +-N блоков вокруг
    /// живых игроков (по умолчанию 30), на глубину до 3 блоков вниз от поверхности.
    ///
    /// Ключевая идея, отличающая эту версию от первого прототипа: вместо того, чтобы
    /// каждый тик поливать ВСЕ подходящие блоки одинаково (отчего вся полоса превращалась
    /// в грязь почти одновременно), система каждый тик выбирает лишь небольшое случайное
    /// подмножество кандидатов и подмачивает только их — примерно так же, как в ванильной
    /// игре работает распространение травы/порчи/священного. За счёт этого превращение
    /// выглядит как расползающееся пятно, а не как мгновенная заливка.
    /// </summary>
    public class TileWetnessSystem : ModSystem
    {
        private const int MaxDepth = 3; // поверхность + 2 слоя вглубь

        // Ключ - координаты тайла. Значение - влажность 0..100.
        private readonly Dictionary<(int x, int y), float> tileMoisture = new();

        // Список кандидатов (тайлов, которые в принципе могут сейчас намокать/сохнуть)
        // пересобирается редко - это единственная "дорогая" операция.
        private List<(int x, int y, int depth)> candidates = new();
        private int candidateRebuildTimer = 0;
        private const int CandidateRebuildInterval = 40; // раз в ~0.66 сек

        public override void PostUpdateWorld()
        {
            // Состояние мира считаем только там, где это имеет смысл: одиночная игра
            // или сервер. Обычный клиент в мультиплеере просто получит синхронизированные
            // тайлы от сервера и не должен считать это сам.
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                return;
            }

            WetnessConfig config = ModContent.GetInstance<WetnessConfig>();

            candidateRebuildTimer--;
            if (candidateRebuildTimer <= 0)
            {
                RebuildCandidates(config);
                candidateRebuildTimer = CandidateRebuildInterval;
            }

            if (candidates.Count == 0)
            {
                return;
            }

            ProcessRandomAttempts(config, Main.raining);
        }

        /// <summary>
        /// Собирает список всех тайлов земли/травы/грязи в активной зоне (рядом с игроками),
        /// на глубину до MaxDepth от первого открытого (без крыши) твёрдого блока.
        /// Копка сквозь камень или другие блоки останавливает погружение вглубь -
        /// пропитывается только "почвенный" слой.
        /// 
        /// НОВОЕ: Исключает тайлы под деревьями (ствол и крона ±2 блока), чтобы не ломать деревья.
        /// </summary>
        private void RebuildCandidates(WetnessConfig config)
        {
            candidates.Clear();

            HashSet<int> activeColumns = new();
            foreach (Player player in Main.player)
            {
                if (player == null || !player.active)
                {
                    continue;
                }

                // Намокание/высыхание земли работает только в трёх биомах на поверхности:
                // лес, пустыня, океан. Во всех остальных (джунгли, снег, порча/кримсон/
                // святыня, грибной, подземелье и т.п.) земля не мокнет и не сохнет вообще -
                // например, в джунглях и так естественная влажность, которую эта система
                // не пытается моделировать отдельно.
                if (!IsAllowedBiome(player))
                {
                    continue;
                }

                int centerX = (int)(player.Center.X / 16f);
                for (int x = centerX - config.TileWetnessRangeX; x <= centerX + config.TileWetnessRangeX; x++)
                {
                    activeColumns.Add(x);
                }
            }

            foreach (int x in activeColumns)
            {
                if (x < 10 || x >= Main.maxTilesX - 10)
                {
                    continue;
                }

                // НОВОЕ: Проверяем, находится ли эта колонка под деревом
                if (IsUnderTree(x))
                {
                    continue; // Пропускаем колонки под деревьями
                }

                int surfaceY = FindOpenSurfaceTile(x);
                if (surfaceY < 0)
                {
                    continue;
                }

                for (int depth = 0; depth < MaxDepth; depth++)
                {
                    int y = surfaceY + depth;
                    if (y < 0 || y >= Main.maxTilesY)
                    {
                        break;
                    }

                    Tile tile = Main.tile[x, y];
                    if (tile == null || !tile.HasTile)
                    {
                        break;
                    }

                    if (!IsSoilTile(tile.TileType))
                    {
                        break; // упёрлись в камень/руду и т.п. - глубже почва не пропитывается
                    }

                    // ВАЖНО: если это уже готовая грязь, а мы её ещё не отслеживаем (например,
                    // это грязь, поставленная самим игроком, или грязь, оставшаяся с прошлой
                    // сессии - влажность между сессиями сознательно не сохраняется), нельзя
                    // просто оставить её без записи. Без этой строчки TryDryTile увидел бы
                    // "влажность по умолчанию 0" и мгновенно превратил бы её обратно в землю
                    // при заходе в мир - именно это и вызывало баг с мгновенным высыханием.
                    // Визуально это уже грязь, значит по определению она "полностью мокрая".
                    if (tile.TileType == TileID.Mud && !tileMoisture.ContainsKey((x, y)))
                    {
                        tileMoisture[(x, y)] = 100f;
                    }

                    candidates.Add((x, y, depth));
                }
            }
        }

        /// <summary>
        /// Разрешённые биомы для намокания/высыхания земли: лес, пустыня, океан.
        /// "Лес" в терминах игры - это фактически ОТСУТСТВИЕ всех остальных особых
        /// биомов на поверхности (у него нет отдельного флага Player.ZoneForest).
        /// Джунгли, снег, порча/кримсон/священная роща, грибной биом, подземелье и
        /// подземная пустыня сюда намеренно не входят - там земля/грязь не мокнет
        /// и не сохнет от этой системы вообще.
        /// </summary>
        private static bool IsAllowedBiome(Player player)
        {
            if (player.ZoneDesert || player.ZoneBeach)
            {
                return true; // явно пустыня или океан/пляж
            }

            bool isOtherSpecialBiome = player.ZoneJungle
                || player.ZoneSnow
                || player.ZoneCorrupt
                || player.ZoneCrimson
                || player.ZoneHallow
                || player.ZoneGlowshroom
                || player.ZoneUndergroundDesert
                || player.ZoneDungeon;

            // "Лес" = обычная поверхность без каких-либо других особых биомов.
            return player.ZoneOverworldHeight && !isOtherSpecialBiome;
        }

        /// <summary>
        /// Проверяет, находится ли колонка X под деревом (ствол или крона).
        /// Дерево защищает землю от дождя в радиусе ±2 блока от ствола.
        /// </summary>
        private bool IsUnderTree(int x)
        {
            // Проверяем колонку X и соседние ±2 блока на наличие дерева
            for (int checkX = x - 2; checkX <= x + 2; checkX++)
            {
                if (checkX < 0 || checkX >= Main.maxTilesX)
                {
                    continue;
                }

                // Ищем дерево в этой колонке (обычно деревья начинаются от поверхности)
                int startY = (int)Main.worldSurface - 60;
                if (startY < 10)
                {
                    startY = 10;
                }
                int endY = (int)Main.worldSurface + 20;

                for (int y = startY; y < endY && y < Main.maxTilesY; y++)
                {
                    Tile tile = Main.tile[checkX, y];
                    if (tile != null && tile.HasTile && tile.TileType == TileID.Trees)
                    {
                        return true; // Нашли дерево в радиусе ±2 блока
                    }
                }
            }

            return false;
        }

        private static bool IsSoilTile(ushort type)
        {
            return type == TileID.Dirt || type == TileID.Grass || type == TileID.Mud;
        }

        /// <summary>
        /// Идёт вниз от уровня поверхности мира, ищет первый твёрдый тайл, над которым
        /// нет крыши (то есть его мочит дождь напрямую).
        /// </summary>
        private int FindOpenSurfaceTile(int x)
        {
            int startY = (int)Main.worldSurface - 60;
            if (startY < 10)
            {
                startY = 10;
            }
            int endY = (int)Main.worldSurface + 20;

            for (int y = startY; y < endY && y < Main.maxTilesY; y++)
            {
                Tile tile = Main.tile[x, y];
                if (tile == null || !tile.HasTile || !Main.tileSolid[tile.TileType])
                {
                    continue;
                }

                Tile above = Main.tile[x, y - 1];
                if (above != null && above.HasTile && Main.tileSolid[above.TileType])
                {
                    continue; // блок закрыт сверху другим блоком - не открытая поверхность
                }

                return y;
            }

            return -1;
        }

        /// <summary>
        /// Каждый тик выбирает ограниченное случайное число кандидатов и продвигает
        /// их влажность в ту или иную сторону. Именно эта случайность и ограниченность
        /// количества попыток создаёт эффект постепенного, неравномерного "расползания".
        /// </summary>
        private void ProcessRandomAttempts(WetnessConfig config, bool raining)
        {
            int attempts = System.Math.Min(config.TileMaxAttemptsPerTick, candidates.Count);

            for (int a = 0; a < attempts; a++)
            {
                var (x, y, depth) = candidates[Main.rand.Next(candidates.Count)];

                if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
                {
                    continue;
                }

                Tile tile = Main.tile[x, y];
                if (tile == null || !tile.HasTile || !IsSoilTile(tile.TileType))
                {
                    tileMoisture.Remove((x, y));
                    continue; // блок сломали/заменили - пропускаем
                }

                if (raining)
                {
                    TryWetTile(config, x, y, depth, tile);
                }
                else
                {
                    TryDryTile(config, x, y, tile);
                }
            }
        }

        private void TryWetTile(WetnessConfig config, int x, int y, int depth, Tile tile)
        {
            if (tile.TileType == TileID.Mud)
            {
                return; // уже грязь, мочить больше некуда
            }

            // Глубже поверхности блок начинает мокнуть только тогда, когда слой над ним
            // уже достаточно пропитался - вода должна сначала просочиться сверху.
            if (depth > 0)
            {
                tileMoisture.TryGetValue((x, y - 1), out float aboveMoisture);
                bool aboveIsMud = Main.tile[x, y - 1] != null && Main.tile[x, y - 1].HasTile
                    && Main.tile[x, y - 1].TileType == TileID.Mud;

                if (!aboveIsMud && aboveMoisture < config.TileSeepThreshold)
                {
                    return; // сверху ещё недостаточно мокро - ждём
                }
            }

            var key = (x, y);
            tileMoisture.TryGetValue(key, out float current);

            // Замедление с глубиной: каждый следующий слой мокнет медленнее предыдущего.
            float depthMultiplier = 1f;
            for (int i = 0; i < depth; i++)
            {
                depthMultiplier *= config.TileDepthRateMultiplier;
            }

            float rate = config.TileWetRate * depthMultiplier * Main.rand.NextFloat(0.7f, 1.3f);

            // Небольшой бонус, если сосед по горизонтали на той же глубине уже стал грязью -
            // создаёт эффект расползающегося пятна, а не разрозненных точек.
            if (HasMuddyNeighbor(x, y))
            {
                rate *= 1f + config.TileNeighborSpreadBonus;
            }

            current += rate;

            if (current >= 100f)
            {
                ConvertTile(x, y, TileID.Mud);
                tileMoisture[key] = 100f;

                if (depth == 0)
                {
                    // Лужа имеет смысл только на самой поверхности (depth==0) - там, где
                    // прямо над блоком есть открытый воздух, а не другой слой почвы.
                    TryCreatePuddle(config, x, y);
                }
            }
            else
            {
                tileMoisture[key] = current;
            }
        }

        private void TryDryTile(WetnessConfig config, int x, int y, Tile tile)
        {
            var key = (x, y);

            if (tile.TileType == TileID.Mud)
            {
                tileMoisture.TryGetValue(key, out float moisture);
                moisture -= config.TileDryRate * Main.rand.NextFloat(0.7f, 1.3f);

                if (moisture <= 0f)
                {
                    ConvertTile(x, y, TileID.Dirt);
                    tileMoisture.Remove(key);
                    RemovePuddle(x, y);
                }
                else
                {
                    tileMoisture[key] = moisture;
                }
            }
            else if (tileMoisture.TryGetValue(key, out float partialMoisture))
            {
                // Блок ещё не стал грязью, а дождь уже кончился - забываем накопленную влагу.
                partialMoisture -= config.TileDryRate * Main.rand.NextFloat(0.7f, 1.3f);
                if (partialMoisture <= 0f)
                {
                    tileMoisture.Remove(key);
                }
                else
                {
                    tileMoisture[key] = partialMoisture;
                }
            }
        }

        private bool HasMuddyNeighbor(int x, int y)
        {
            // В современных версиях tModLoader Tile - структура (value type), поэтому
            // её нельзя сравнивать с null - вместо этого сперва проверяем границы карты.
            bool leftMud = false;
            if (x - 1 >= 0)
            {
                Tile left = Main.tile[x - 1, y];
                leftMud = left.HasTile && left.TileType == TileID.Mud;
            }

            bool rightMud = false;
            if (x + 1 < Main.maxTilesX)
            {
                Tile right = Main.tile[x + 1, y];
                rightMud = right.HasTile && right.TileType == TileID.Mud;
            }

            return leftMud || rightMud;
        }

        // --- Лужи ---
        //
        // Ключ - координаты тайла с ЖИДКОСТЬЮ (то есть на один блок выше только что
        // образовавшейся грязи), а не координаты самой грязи. Храним только те лужи,
        // которые создала эта система - чтобы при высыхании убирать именно свою воду,
        // а не случайно осушить настоящий пруд игрока, который просто оказался рядом.
        private readonly HashSet<(int x, int y)> puddleTiles = new();

        /// <summary>
        /// Пытается положить немного настоящей воды поверх только что образовавшейся
        /// грязи - реальная жидкость в Terraria уже умеет отражать небо/фон и красиво
        /// покачиваться, так что это даёт честный эффект "блестящей лужи" бесплатно,
        /// без единого кастомного шейдера.
        ///
        /// Лужа появляется не на каждом блоке грязи подряд (иначе выглядело бы как
        /// сплошное болото), а с шансом (PuddleChance) и только там, где это похоже на
        /// естественное углубление или ровный участок - если соседние колонки заметно
        /// выше текущей точки, вода бы просто стекла со склона, а не осталась лужей.
        /// </summary>
        private void TryCreatePuddle(WetnessConfig config, int x, int y)
        {
            if (!config.PuddleEnabled)
            {
                return;
            }

            if (Main.rand.NextFloat() >= config.PuddleChance)
            {
                return;
            }

            int aboveY = y - 1;
            if (aboveY < 0)
            {
                return;
            }

            Tile above = Main.tile[x, aboveY];
            if (above.HasTile)
            {
                return; // сверху что-то стоит - луже там не место
            }

            if (above.LiquidAmount > 10)
            {
                return; // там и так уже заметно много жидкости - не трогаем чужую воду
            }

            // Эвристика "естественного углубления": лужа появляется только там, где
            // соседние колонки на поверхности НЕ выше текущей точки (то есть это
            // локальная низина или ровный участок, а не склон/вершина холма).
            int leftSurface = FindOpenSurfaceTile(x - 1);
            int rightSurface = FindOpenSurfaceTile(x + 1);
            bool isDepressionOrFlat = (leftSurface < 0 || leftSurface <= y) && (rightSurface < 0 || rightSurface <= y);
            if (!isDepressionOrFlat)
            {
                return;
            }

            byte amount = (byte)Main.rand.Next(config.PuddleMinLiquidAmount, config.PuddleMaxLiquidAmount + 1);
            above.LiquidType = LiquidID.Water;
            above.LiquidAmount = amount;

            // Ставим тайл в очередь на обработку ванильной физикой жидкостей - это даёт
            // настоящую симуляцию (растекание по низине, покачивание, отражение), а не
            // просто статичную картинку.
            Liquid.AddWater(x, aboveY);

            puddleTiles.Add((x, aboveY));

            if (Main.netMode == NetmodeID.Server)
            {
                NetMessage.SendData(MessageID.LiquidUpdate, -1, -1, null, x, aboveY);
            }
        }

        /// <summary>
        /// Убирает лужу, которую создала именно эта система, когда грязь под ней высохла
        /// обратно в землю. Если на этом месте лужи нет (например, блок стал грязью без
        /// лужи из-за проверки на углубление) - ничего не делает.
        /// </summary>
        private void RemovePuddle(int x, int y)
        {
            int aboveY = y - 1;
            var key = (x, aboveY);
            if (!puddleTiles.Contains(key))
            {
                return; // это не наша лужа - не трогаем (могла быть настоящая вода игрока)
            }

            if (aboveY >= 0)
            {
                Tile above = Main.tile[x, aboveY];
                above.LiquidAmount = 0;
                Liquid.AddWater(x, aboveY);

                if (Main.netMode == NetmodeID.Server)
                {
                    NetMessage.SendData(MessageID.LiquidUpdate, -1, -1, null, x, aboveY);
                }
            }

            puddleTiles.Remove(key);
        }

        private void ConvertTile(int x, int y, ushort newType)
        {
            Main.tile[x, y].TileType = newType;
            // Важно использовать именно SquareTileFrame, а не TileFrame: обычный TileFrame
            // пересчитывает "картинку" только у самого изменённого блока, из-за чего у его
            // соседей остаётся неправильная стыковка краёв (видимая рамка). SquareTileFrame
            // пересчитывает сам блок и его соседей одним вызовом.
            WorldGen.SquareTileFrame(x, y, true);

            if (Main.netMode == NetmodeID.Server)
            {
                // Размер синхронизации увеличен до 3x3, чтобы соседние клиенты тоже
                // получили обновлённые фреймы соседних блоков, а не только целевого.
                NetMessage.SendTileSquare(-1, x, y, 3);
            }

            SpawnConvertDust(x, y, newType);
        }

        private void SpawnConvertDust(int x, int y, ushort newType)
        {
            if (Main.dedServ)
            {
                return;
            }

            int dustType = newType == TileID.Mud ? DustID.Mud : DustID.Dirt;
            for (int i = 0; i < 2; i++)
            {
                Dust.NewDust(new Microsoft.Xna.Framework.Vector2(x * 16, y * 16), 16, 16, dustType, 0f, -1f, 150, default, 0.8f);
            }
        }

        public override void ClearWorld()
        {
            tileMoisture.Clear();
            candidates.Clear();
            puddleTiles.Clear();
        }

        public override void SaveWorldData(Terraria.ModLoader.IO.TagCompound tag)
        {
            // Сознательно не сохраняем словарь влажности между сессиями - это лёгкое
            // кэш-состояние, которое безопасно пересобрать заново по ходу игры.
        }

        public override void LoadWorldData(Terraria.ModLoader.IO.TagCompound tag)
        {
        }
    }
}
