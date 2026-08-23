using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using WetnessMod.Common.Configs;

namespace WetnessMod.Common.Systems
{
    /// <summary>
    /// Гасит факелы и костры, оказавшиеся под открытым дождём. Переключение состояния
    /// идёт через штатный игровой механизм (Terraria.Wiring.ToggleTorch / ToggleCampFire) -
    /// тот же самый, которым их переключают провода, и на котором основано тушение огня
    /// дождём в специальном зерне мира "The Constant". Мы не ломаем и не удаляем тайл,
    /// только переключаем его состояние.
    ///
    /// Важно: потушенный тайл НЕ разгорается сам. Разжечь его заново может только игрок
    /// (например, через провода) - это осознанное решение, а не недоработка.
    ///
    /// Чтобы тушение не выглядело как мгновенный "бросок кубика", вместо одной проверки
    /// шанса используется постепенное накопление "намокания огня" (как у брони и у земли):
    /// каждая случайная попытка добавляет немного прогресса, и только когда он доходит
    /// до 100 - тайл гаснет. Это даёт больше времени и больше разброса между разными
    /// факелами, вместо того чтобы половина костров тухла в первую же секунду дождя.
    /// </summary>
    public class FireExtinguishSystem : ModSystem
    {
        private readonly List<(int x, int y, ushort type)> candidates = new();
        private int candidateRebuildTimer = 0;
        private const int CandidateRebuildInterval = 60; // раз в ~1 сек

        // Прогресс тушения конкретного тайла: 0..100. Как только доходит до 100 - тайл гаснет
        // и запись удаляется. Если дождь/открытое небо пропадают раньше - прогресс медленно
        // "остывает" обратно к нулю, а не сбрасывается мгновенно.
        private readonly Dictionary<(int x, int y), float> extinguishProgress = new();

        // Тайлы, которые погасила именно эта система - чтобы не путать их с теми, что
        // выключил сам игрок через провода, и чтобы не трогать/не разжигать их автоматически.
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

            // Забываем про тайлы вне активной зоны, чтобы не копить мусор в словарях.
            // Потушенные тайлы (extinguishedByUs) при этом НЕ трогаем - они остаются
            // потушенными до тех пор, пока их не зажжёт сам игрок, независимо от того,
            // в зоне отслеживания они сейчас или нет.
            if (extinguishProgress.Count > 0)
            {
                List<(int, int)> stale = null;
                foreach (var key in extinguishProgress.Keys)
                {
                    if (!seen.Contains(key))
                    {
                        (stale ??= new List<(int, int)>()).Add(key);
                    }
                }
                if (stale != null)
                {
                    foreach (var key in stale)
                    {
                        extinguishProgress.Remove(key);
                    }
                }
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
                    // Блок сломали/заменили с тех пор, как собирали кандидатов.
                    extinguishProgress.Remove((x, y));
                    extinguishedByUs.Remove((x, y));
                    continue;
                }

                var key = (x, y);

                if (extinguishedByUs.Contains(key))
                {
                    continue; // уже потушен нами - ждём, пока игрок сам его зажжёт
                }

                bool exposed = IsExposedToOpenSky(x, y, config.FireMaxSkyCheckHeight);

                if (raining && exposed)
                {
                    extinguishProgress.TryGetValue(key, out float progress);

                    // Случайный множитель даёт разброс между отдельными факелами - одни
                    // погаснут раньше, другие позже, даже под одним и тем же дождём.
                    float rate = config.FireExtinguishRate * Main.maxRaining * Main.rand.NextFloat(0.5f, 1.5f);
                    progress += rate;

                    if (progress >= 100f)
                    {
                        SetLit(x, y, tile, type, false);
                        extinguishedByUs.Add(key);
                        extinguishProgress.Remove(key);

                        if (config.FireSpawnDust)
                        {
                            SpawnDust(x, y, DustID.Smoke, 4, -1.5f);
                        }
                    }
                    else
                    {
                        extinguishProgress[key] = progress;

                        // Лёгкое шипение по пути к тушению - просто красивая обратная связь,
                        // не влияет на сам расчёт.
                        if (config.FireSpawnDust && Main.rand.NextFloat() < 0.15f)
                        {
                            SpawnDust(x, y, DustID.Smoke, 1, -0.8f);
                        }
                    }
                }
                else if (extinguishProgress.TryGetValue(key, out float coolingProgress))
                {
                    // Дождь прекратился или тайл оказался под крышей - прогресс тушения
                    // постепенно "остывает" обратно, а не сбрасывается мгновенно.
                    coolingProgress -= config.FireExtinguishCooldownRate * Main.rand.NextFloat(0.5f, 1.5f);
                    if (coolingProgress <= 0f)
                    {
                        extinguishProgress.Remove(key);
                    }
                    else
                    {
                        extinguishProgress[key] = coolingProgress;
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
        /// посчитаться "открытым" - осознанный компромисс между точностью и скоростью.
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

        private void SpawnDust(int x, int y, int dustType, int count, float speedY)
        {
            if (Main.dedServ)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Dust.NewDust(new Vector2(x * 16, y * 16), 16, 16, dustType, 0f, speedY, 100, default, 1f);
            }
        }

        public override void ClearWorld()
        {
            candidates.Clear();
            extinguishProgress.Clear();
            extinguishedByUs.Clear();
        }
    }
}
