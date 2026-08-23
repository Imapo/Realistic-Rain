using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
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

        public override GlobalItem Clone(Item item, Item itemClone)
        {
            WetnessGlobalItem clone = (WetnessGlobalItem)base.Clone(item, itemClone);
            clone.Wetness = Wetness;
            return clone;
        }

        public override void SaveData(Item item, TagCompound tag)
        {
            if (Wetness > 0.01f)
            {
                tag["wetness"] = Wetness;
            }
        }

        public override void LoadData(Item item, TagCompound tag)
        {
            Wetness = tag.GetFloat("wetness");
        }

        public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
        {
            if (Wetness <= 0.5f)
            {
                return; // почти сухое - не засоряем тултип
            }

            WetnessConfig config = ModContent.GetInstance<WetnessConfig>();
            bool isArmorPiece = item.headSlot > -1 || item.bodySlot > -1 || item.legSlot > -1;

            string status;
            Color color;

            if (Wetness >= 100f)
            {
                status = isArmorPiece
                    ? "Полностью промокло: защита сильно снижена"
                    : "Полностью промокло: эффект отключён, пока не высохнет";
                color = new Color(120, 170, 255);
            }
            else if (Wetness >= 60f)
            {
                status = "Сильно мокрое";
                color = new Color(140, 190, 255);
            }
            else if (Wetness >= 25f)
            {
                status = "Влажное";
                color = new Color(170, 210, 255);
            }
            else
            {
                status = "Слегка влажное";
                color = new Color(200, 225, 255);
            }

            TooltipLine line = new TooltipLine(Mod, "WetnessStatus", $"{status} ({Wetness:0}%)")
            {
                OverrideColor = color
            };
            tooltips.Add(line);
        }
    }
}
