using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using WetnessMod.Common.Configs;

namespace WetnessMod.Common.Systems
{
    /// <summary>
    /// Отслеживает влажность только тех блоков земли, что находятся в горизонтальной
    /// полосе +-N блоков вокруг живых игроков (по умолчанию 30) — а не по всей карте.
    /// Как только блок вышел из этой зоны надолго, он просто перестаёт отслеживаться:
    /// если он не успел стать грязью, ничего не происходит; если стал — он останется
    /// грязью, пока рядом снова кто-то не окажется (грязь тогда досохнет обратно).
    /// Это дешёвый компромисс между реализмом и производительностью на больших картах.
    /// </summary>
    public class TileWetnessSystem : ModSystem
    {
        // Ключ - упакованные координаты тайла (x,y). Значение - влажность 0..100.
        private readonly Dictionary<(int x, int y), float> tileMoisture = new();

        // Чтобы не сканировать тонны тайлов каждый тик, распределяем работу по времени.
        private int updateTimer = 0;
        private const int UpdateInterval = 5; // раз в 5 тиков (~12 раз/сек)

        public override void PostUpdateWorld()
        {
            if (Main.dedServ == false && Main.netMode == NetmodeID.MultiplayerClient)
            {
                // Превращение блоков — это состояние мира, должно считаться на сервере
                // (или в одиночной игре, где клиент = сервер). Обычный клиент в мультиплеере
                // только визуально получит изменения через синхронизацию тайлов.
                return;
            }

            updateTimer++;
            if (updateTimer < UpdateInterval)
            {
                return;
            }
            updateTimer = 0;

            WetnessConfig config = ModContent.GetInstance<WetnessConfig>();
            bool raining = Main.raining;

            // 1) Собираем набор колонок X, которые сейчас "активны" (рядом с игроком)
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

            if (activeColumns.Count == 0)
            {
                return;
            }

            // 2) Если идёт дождь — пытаемся намочить открытую землю в активных колонках
            if (raining)
            {
                foreach (int x in activeColumns)
                {
                    TryWetSurfaceDirt(x, config);
                }
            }

            // 3) Обновляем уже отслеживаемые тайлы (намокание -> грязь, высыхание -> земля обратно)
            ProcessTrackedTiles(config, raining);
        }

        /// <summary>
        /// Находит верхний открытый (без крыши) блок земли в колонке x и копит в нём влагу.
        /// </summary>
        private void TryWetSurfaceDirt(int x, WetnessConfig config)
        {
            if (x < 10 || x >= Main.maxTilesX - 10)
            {
                return;
            }

            int surfaceY = FindOpenSurfaceTile(x);
            if (surfaceY < 0)
            {
                return;
            }

            Tile tile = Main.tile[x, surfaceY];
            if (tile.TileType != TileID.Dirt)
            {
                return; // мочим только обычную землю, не траву/камень и т.д.
            }

            var key = (x, surfaceY);
            tileMoisture.TryGetValue(key, out float current);
            current += config.TileWetRate;

            if (current >= 100f)
            {
                ConvertTile(x, surfaceY, TileID.Mud);
                tileMoisture[key] = 100f; // остаётся в словаре как "мокрая грязь", чтобы потом досохла обратно
            }
            else
            {
                tileMoisture[key] = current;
            }
        }

        /// <summary>
        /// Идёт вниз от уровня поверхности мира, ищет первый твёрдый тайл без блока прямо
        /// над ним (то есть его "мочит" дождь напрямую).
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
                if (tile == null || !tile.HasTile)
                {
                    continue;
                }

                if (!Main.tileSolid[tile.TileType])
                {
                    continue;
                }

                // Тайл прямо над найденным блоком должен быть пустым - иначе он под крышей/другим блоком
                Tile above = Main.tile[x, y - 1];
                if (above != null && above.HasTile && Main.tileSolid[above.TileType])
                {
                    continue;
                }

                return y;
            }

            return -1;
        }

        private void ProcessTrackedTiles(WetnessConfig config, bool raining)
        {
            if (tileMoisture.Count == 0)
            {
                return;
            }

            List<(int x, int y)> toRemove = null;

            foreach (var kvp in new List<KeyValuePair<(int x, int y), float>>(tileMoisture))
            {
                (int x, int y) = kvp.Key;
                float moisture = kvp.Value;

                if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
                {
                    (toRemove ??= new List<(int, int)>()).Add(kvp.Key);
                    continue;
                }

                Tile tile = Main.tile[x, y];
                if (tile == null || !tile.HasTile)
                {
                    (toRemove ??= new List<(int, int)>()).Add(kvp.Key);
                    continue;
                }

                if (tile.TileType == TileID.Mud && !raining)
                {
                    moisture -= config.TileDryRate;
                    if (moisture <= 0f)
                    {
                        ConvertTile(x, y, TileID.Dirt);
                        (toRemove ??= new List<(int, int)>()).Add(kvp.Key);
                    }
                    else
                    {
                        tileMoisture[kvp.Key] = moisture;
                    }
                }
                else if (tile.TileType == TileID.Dirt && !raining)
                {
                    // Земля, которая не успела стать грязью, а дождь уже кончился —
                    // потихоньку "забываем" накопленную влагу.
                    moisture -= config.TileDryRate;
                    if (moisture <= 0f)
                    {
                        (toRemove ??= new List<(int, int)>()).Add(kvp.Key);
                    }
                    else
                    {
                        tileMoisture[kvp.Key] = moisture;
                    }
                }
                else if (tile.TileType != TileID.Dirt && tile.TileType != TileID.Mud)
                {
                    // Блок был сломан/заменён игроком - прекращаем отслеживать
                    (toRemove ??= new List<(int, int)>()).Add(kvp.Key);
                }
            }

            if (toRemove != null)
            {
                foreach (var key in toRemove)
                {
                    tileMoisture.Remove(key);
                }
            }
        }

        private void ConvertTile(int x, int y, ushort newType)
        {
            Main.tile[x, y].TileType = newType;
            WorldGen.TileFrame(x, y, true);

            // Синхронизация с клиентами в мультиплеере
            if (Main.netMode == NetmodeID.Server)
            {
                NetMessage.SendTileSquare(-1, x, y, 1);
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
            for (int i = 0; i < 3; i++)
            {
                Dust.NewDust(new Microsoft.Xna.Framework.Vector2(x * 16, y * 16), 16, 16, dustType, 0f, -1f, 100, default, 1f);
            }
        }

        public override void ClearWorld()
        {
            tileMoisture.Clear();
        }

        public override void SaveWorldData(Terraria.ModLoader.IO.TagCompound tag)
        {
            // Сознательно не сохраняем словарь влажности между сессиями:
            // это лёгкое кэш-состояние, зависящее от текущей погоды и позиции игроков,
            // и его безопасно "обнулять" при загрузке мира - грязь на карте, если она уже
            // была создана, останется грязью (это уже часть тайлов мира), а счётчики влаги
            // просто пересоберутся заново по мере игры.
        }

        public override void LoadWorldData(Terraria.ModLoader.IO.TagCompound tag)
        {
            // Пара к SaveWorldData - tModLoader требует переопределять оба метода вместе,
            // даже если сохранять/загружать реально нечего.
        }
    }
}
