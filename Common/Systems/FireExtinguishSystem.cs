using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using WetnessMod.Common.Configs;

namespace WetnessMod.Common.Systems
{
    /// <summary>
    /// Гасит факелы и костры, оказавшиеся под открытым дождём. Работает через штатный
    /// игровой механизм включения/выключения этих тайлов - тот же самый, которым их
    /// переключают провода (Terraria.Wiring.ToggleTorch / ToggleCampFire), и на котором
    /// основано тушение огня дождём в специальном зерне мира "The Constant".
    ///
    /// Важно: мы НЕ ломаем и не удаляем тайл, только переключаем его состояние. Это и
    /// безопаснее (не нужно гадать про формат кадров спрайта, не теряется предмет),
    /// и логичнее по смыслу - "факел потух", а не "факел сгорел".
    /// </summary>
    public class FireExtinguishSystem : ModSystem
    {
        // Список тайлов-кандидатов (факелы/костры рядом с игроками) обновляется редко -
        // такие блоки не появляются и не исчезают ежесекундно.
        private readonly List<(int x, int y, ushort type)> candidates = new();
        private int candidateRebuildTimer = 0;
        private const int CandidateRebuildInterval = 60; // раз в ~1 сек

        // Помним, какие тайлы потушили именно мы, чтобы потом их же и разжечь обратно,
        // а не трогать факелы, которые игрок выключил сам через провода.
        private static readonly HashSet<(int x, int y)> extinguishedByUs = new();

        /// <summary>
        /// Позволяет другим системам (например, проверке "есть ли рядом тёплый костёр"
        /// в WetnessPlayer) узнать, потушен ли конкретный тайл именно этой системой.
        /// </summary>
        public static bool IsExtinguishedByUs(int x, int y)
        {
            return extinguishedByUs.Contains((x, y));
        }

        public override void PostUpdateWorld()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                return; // состояние мира считаем на сервере/в одиночной игре
            }

            WetnessConfig config = ModContent.GetInstance<WetnessConfig>();
            if (!config.FireExtinguishEnabled)
            {
                return;
            }

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

            ProcessRandomAttempts(config);
        }

        private void RebuildCandidates(WetnessConfig config)
        {
            candidates.Clear();

            HashSet<(int, int)> seen = new();

            foreach (Player player in Main.player)
            {
                if (player == null || !player.active)
                {
                    continue;
                }

                int centerX = (int)(player.Center.X / 16f);
                int centerY = (int)(player.Center.Y / 16f);

                int minX = System.Math.Max(10, centerX - config.FireExtinguishRangeX);
                int maxX = System.Math.Min(Main.maxTilesX - 10, centerX + config.FireExtinguishRangeX);
                int minY = System.Math.Max(10, centerY - config.FireExtinguishRangeX);
                int maxY = System.Math.Min(Main.maxTilesY - 10, centerY + config.FireExtinguishRangeX);

                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        Tile tile = Main.tile[x, y];
                        if (!tile.HasTile)
                        {
                            continue;
                        }

                        if (tile.TileType != TileID.Torches && tile.TileType != TileID.Campfire)
                        {
                            continue;
                        }

                        if (seen.Add((x, y)))
                        {
                            candidates.Add((x, y, tile.TileType));
                        }
                    }
                }
            }

            // Забываем про потушенные тайлы, которые больше не входят ни в чью активную зону -
            // если игрок вернётся, они снова начнут отслеживаться как обычные (уже незажжённые).
            if (extinguishedByUs.Count > 0)
            {
                extinguishedByUs.RemoveWhere(pos => !seen.Contains(pos));
            }
        }

        private void ProcessRandomAttempts(WetnessConfig config)
        {
            int attempts = System.Math.Min(config.FireMaxAttemptsPerTick, candidates.Count);
            bool raining = Main.raining;

            for (int a = 0; a < attempts; a++)
            {
                var (x, y, type) = candidates[Main.rand.Next(candidates.Count)];

                if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
                {
                    continue;
                }

                Tile tile = Main.tile[x, y];
                if (!tile.HasTile || tile.TileType != type)
                {
                    extinguishedByUs.Remove((x, y));
                    continue; // блок сломали/заменили с тех пор, как собирали кандидатов
                }

                bool currentlyOffByUs = extinguishedByUs.Contains((x, y));
                bool exposed = IsExposedToOpenSky(x, y, config.FireMaxSkyCheckHeight);

                if (raining && exposed && !currentlyOffByUs)
                {
                    float chance = config.FireBaseExtinguishChance * Main.maxRaining;
                    if (Main.rand.NextFloat() < chance)
                    {
                        SetLit(x, y, tile, type, false);
                        extinguishedByUs.Add((x, y));

                        if (config.FireSpawnDust)
                        {
                            SpawnHissDust(x, y);
                        }
                    }
                }
                else if (currentlyOffByUs && config.FireAutoRelight && (!raining || !exposed))
                {
                    if (Main.rand.NextFloat() < config.FireRelightChance)
                    {
                        SetLit(x, y, tile, type, true);
                        extinguishedByUs.Remove((x, y));
                    }
                }
            }
        }

        /// <summary>
        /// Переключает тайл через штатный игровой механизм (тот же, что используют провода),
        /// а не через ручную правку кадров спрайта - так безопаснее и совместимо с любыми
        /// цветовыми вариантами факелов/костров.
        /// </summary>
        private void SetLit(int x, int y, Tile tile, ushort type, bool lit)
        {
            if (type == TileID.Torches)
            {
                Wiring.ToggleTorch(x, y, tile, lit);
            }
            else if (type == TileID.Campfire)
            {
                Wiring.ToggleCampFire(x, y, tile, lit, doSkipWires: true);
            }

            if (Main.netMode == NetmodeID.Server)
            {
                NetMessage.SendTileSquare(-1, x, y, 1);
            }
        }

        /// <summary>
        /// Смотрит вверх от тайла на ограниченную высоту (для производительности) в поисках
        /// крыши. Если крыша дальше, чем FireMaxSkyCheckHeight блоков, тайл может по ошибке
        /// посчитаться "открытым" - осознанный компромисс между точностью и скоростью,
        /// особенно важный здесь, так как факелов в активной зоне обычно гораздо больше,
        /// чем поверхностных колонок земли.
        /// </summary>
        private bool IsExposedToOpenSky(int x, int y, int maxHeight)
        {
            int top = System.Math.Max(0, y - maxHeight);
            for (int checkY = y - 1; checkY >= top; checkY--)
            {
                Tile t = Main.tile[x, checkY];
                if (t.HasTile && Main.tileSolid[t.TileType] && !Main.tileSolidTop[t.TileType])
                {
                    return false;
                }
            }
            return true;
        }

        private void SpawnHissDust(int x, int y)
        {
            if (Main.dedServ)
            {
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                Dust.NewDust(new Vector2(x * 16, y * 16), 16, 16, DustID.Smoke, 0f, -1.5f, 100, default, 1f);
            }
        }

        public override void ClearWorld()
        {
            candidates.Clear();
            extinguishedByUs.Clear();
        }
    }
}
