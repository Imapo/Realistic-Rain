using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using WetnessMod.Common.Configs;

namespace WetnessMod.Common.GlobalItems
{
    /// <summary>
    /// Хранит влажность конкретного предмета (0..100) прямо на нём самом, а не в
    /// отдельном массиве по номеру слота. Это принципиально: раньше влажность была
    /// привязана к "слоту экипировки", и как только слот пустел, значение обнулялось -
    /// то есть вещь, которую сняли, мгновенно "высыхала". Теперь влажность - это
    /// свойство самого объекта Item, поэтому она сохраняется, куда бы предмет ни попал:
    /// в инвентарь, в сундук, обратно в слот экипировки.
    /// </summary>
    public class WetnessGlobalItem : GlobalItem
    {
        public override bool InstancePerEntity => true;

        public float Wetness;

        // Индивидуальный множитель скорости намокания/высыхания этого конкретного предмета
        // (0.75x..1.3x), выбирается один раз случайно и запоминается навсегда - чтобы вещи
        // одного типа не мокли строго идентично, а немного отличались друг от друга.
        // 0 означает "ещё не назначен" (см. GetRateMultiplier).
        public float RateMultiplier;

        // "Залипающий" флаг: вещь "сдалась" из-за сырости и перестала работать. Как только
        // он выставлен - вещь не работает вообще независимо от того, как дальше колеблется
        // Wetness, и сбрасывается только когда Wetness возвращается ровно к 0 (см.
        // WetnessPlayer.UpdateWetDisableState). Именно это и даёт задержку/гистерезис:
        // "закончился дождь -> вещь тут же снова заработала" больше не происходит.
        public bool DisabledByWetness;

        public float GetRateMultiplier()
        {
            if (RateMultiplier <= 0f)
            {
                RateMultiplier = Main.rand.NextFloat(0.75f, 1.3f);
            }
            return RateMultiplier;
        }

        public override GlobalItem Clone(Item item, Item itemClone)
        {
            WetnessGlobalItem clone = (WetnessGlobalItem)base.Clone(item, itemClone);
            clone.Wetness = Wetness;
            clone.RateMultiplier = RateMultiplier;
            clone.DisabledByWetness = DisabledByWetness;
            return clone;
        }

        public override void SaveData(Item item, TagCompound tag)
        {
            if (Wetness > 0.01f)
            {
                tag["wetness"] = Wetness;
            }
            if (RateMultiplier > 0f)
            {
                tag["wetnessRateMul"] = RateMultiplier;
            }
            if (DisabledByWetness)
            {
                tag["wetnessDisabled"] = true;
            }
        }

        public override void LoadData(Item item, TagCompound tag)
        {
            Wetness = tag.GetFloat("wetness");
            RateMultiplier = tag.GetFloat("wetnessRateMul");
            DisabledByWetness = tag.ContainsKey("wetnessDisabled");
        }

        public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
        {
            if (Wetness <= 0.5f && !DisabledByWetness)
            {
                return; // почти сухое и не отключено - не засоряем тултип
            }

            bool isArmorPiece = item.headSlot > -1 || item.bodySlot > -1 || item.legSlot > -1;

            string status;
            Color color;

            if (DisabledByWetness)
            {
                // Отдельный, самый заметный статус: вещь не просто "мокрая", а конкретно
                // "сдалась" и не заработает, пока не высохнет полностью - независимо от того,
                // сколько сейчас процентов влажности показывает счётчик.
                status = isArmorPiece
                    ? Language.GetTextValue("Mods.WetnessMod.TooltipStatuses.DisabledByWetness_Armor")
                    : Language.GetTextValue("Mods.WetnessMod.TooltipStatuses.DisabledByWetness_Accessory");
                color = new Color(120, 170, 255);
            }
            else if (Wetness >= 60f)
            {
                status = Language.GetTextValue("Mods.WetnessMod.TooltipStatuses.VeryWet");
                color = new Color(140, 190, 255);
            }
            else if (Wetness >= 25f)
            {
                status = Language.GetTextValue("Mods.WetnessMod.TooltipStatuses.Wet");
                color = new Color(170, 210, 255);
            }
            else
            {
                status = Language.GetTextValue("Mods.WetnessMod.TooltipStatuses.SlightlyWet");
                color = new Color(200, 225, 255);
            }

            TooltipLine line = new TooltipLine(Mod, "WetnessStatus", $"{status} ({Wetness:0}%)")
            {
                OverrideColor = color
            };
            tooltips.Add(line);
        }

        // --- Визуал: ЧБ + голубой оттенок для отключённых из-за влажности предметов ---

        // Настоящей десатурации через Color-тинт добиться нельзя (тинт красит, но не убирает
        // цвет), поэтому один раз на тип предмета конвертируем его иконку в оттенки серого
        // и подмешиваем холодный синий - и дальше просто переиспользуем готовую текстуру.
        private static readonly Dictionary<int, Texture2D> desaturatedIconCache = new();

        public override bool PreDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
            Color drawColor, Color itemColor, Vector2 origin, float scale)
        {
            if (!DisabledByWetness)
            {
                return true; // рисуем как обычно
            }

            Texture2D icon = GetOrCreateDesaturatedIcon(item.type);
            if (icon == null)
            {
                return true; // текстура ещё не загружена - в этот кадр рисуем как обычно
            }

            spriteBatch.Draw(icon, position, frame, drawColor, 0f, origin, scale, SpriteEffects.None, 0f);
            return false;
        }

        public override bool PreDrawInWorld(Item item, SpriteBatch spriteBatch, Color lightColor, Color alphaColor,
            ref float rotation, ref float scale, int whoAmI)
        {
            // Выброшенный на землю предмет тоже должен выглядеть "простуженным", если он
            // был отключён в момент, когда его выкинули - иначе игрок теряет подсказку,
            // подняв вещь обратно.
            if (!DisabledByWetness)
            {
                return true;
            }

            Texture2D icon = GetOrCreateDesaturatedIcon(item.type);
            if (icon == null)
            {
                return true;
            }

            // Упрощение специально: аксессуары/броня почти никогда не анимированы как предметы
            // на земле, поэтому не лезем в Main.itemAnimations и просто берём всю текстуру.
            Rectangle frame = icon.Frame();

            Vector2 origin = frame.Size() / 2f;
            Vector2 drawPos = item.position - Main.screenPosition + new Vector2(item.width / 2f, item.height - frame.Height * scale / 2f + 12f);

            spriteBatch.Draw(icon, drawPos, frame, alphaColor, rotation, origin, scale, SpriteEffects.None, 0f);
            return false;
        }

        private static Texture2D GetOrCreateDesaturatedIcon(int itemType)
        {
            if (desaturatedIconCache.TryGetValue(itemType, out Texture2D cached))
            {
                return cached;
            }

            Main.instance.LoadItem(itemType);
            var asset = TextureAssets.Item[itemType];
            if (!asset.IsLoaded)
            {
                return null;
            }

            Texture2D original = asset.Value;
            var pixels = new Color[original.Width * original.Height];
            original.GetData(pixels);

            // Холодный "простуженный" оттенок, к которому притягивается яркость каждого пикселя.
            const float tintR = 150f;
            const float tintG = 190f;
            const float tintB = 225f;

            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                if (c.A == 0)
                {
                    continue;
                }

                float luminance = (c.R * 0.299f + c.G * 0.587f + c.B * 0.114f) / 255f;
                pixels[i] = new Color(
                    (byte)(luminance * tintR),
                    (byte)(luminance * tintG),
                    (byte)(luminance * tintB),
                    c.A);
            }

            Texture2D desaturated = new Texture2D(Main.instance.GraphicsDevice, original.Width, original.Height);
            desaturated.SetData(pixels);

            desaturatedIconCache[itemType] = desaturated;
            return desaturated;
        }

        public override void Unload()
        {
            // Кэш держит GPU-текстуры - явно чистим при выгрузке мода/пересборке в режиме
            // разработки, чтобы не плодить висящие ссылки между перезагрузками.
            desaturatedIconCache.Clear();
        }
    }
}