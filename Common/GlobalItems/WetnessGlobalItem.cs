using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using WetnessMod.Common.Players;

namespace WetnessMod.Common.GlobalItems
{
    public class WetnessGlobalItem : GlobalItem
    {
        public override bool InstancePerEntity => false;

        public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
        {
            Player player = Main.LocalPlayer;
            if (player == null)
            {
                return;
            }

            WetnessPlayer wetnessPlayer = player.GetModPlayer<WetnessPlayer>();

            int slot = FindEquippedSlot(player, item);
            if (slot < 0)
            {
                return; // предмет не надет прямо сейчас — не показываем влажность
            }

            float wet = wetnessPlayer.Wetness[slot];
            if (wet <= 0.5f)
            {
                return; // почти сухое — не засоряем тултип
            }

            string status;
            Color color;

            if (wet >= 100f)
            {
                status = slot < 3
                    ? "Полностью промокло: защита сильно снижена"
                    : "Полностью промокло: эффект отключён";
                color = new Color(120, 170, 255);
            }
            else if (wet >= 60f)
            {
                status = "Сильно мокрое";
                color = new Color(140, 190, 255);
            }
            else if (wet >= 25f)
            {
                status = "Влажное";
                color = new Color(170, 210, 255);
            }
            else
            {
                status = "Слегка влажное";
                color = new Color(200, 225, 255);
            }

            TooltipLine line = new TooltipLine(Mod, "WetnessStatus", $"{status} ({wet:0}%)")
            {
                OverrideColor = color
            };
            tooltips.Add(line);
        }

        private int FindEquippedSlot(Player player, Item item)
        {
            for (int i = 0; i < WetnessPlayer.TrackedSlots; i++)
            {
                if (player.armor[i] == item)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
