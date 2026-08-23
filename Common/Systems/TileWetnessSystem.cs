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

                    candidates.Add((x, y, depth));
                }
            }
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
