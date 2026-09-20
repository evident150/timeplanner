using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TimePlanner.Core
{
    /// <summary>全局配色与字体（圣旨特别版：木牍 + 明黄绢面 + 朱砂金线）。</summary>
    public static class Theme
    {
        public static Color Bg, BgAlt, Panel, PanelHi, PanelSoft, Border, BorderHi;
        public static Color Text, TextMuted, TextFaint;
        public static Color Accent, AccentSoft, AccentDeep, OnAccent;
        public static Color Success, Warning, Danger, Purple;
        public static FontFamily Font, FontTitle;

        // 圣旨专用色
        public static Color Silk, SilkDeep, SilkEdge, Wood, WoodHi, Brocade, Gold, GoldSoft, Ink, Seal;
        public static Color TextOnWood, TextOnWoodFaint;

        // 小人对话框：宣纸底 + 金褐描边 + 朱字
        public static Color Ivory, IvoryLine, IvoryInk;

        static readonly Dictionary<uint, SolidColorBrush> Cache = new Dictionary<uint, SolidColorBrush>();

        public static readonly string[] AccentKeys = { "rose", "blue", "green", "amber", "violet", "cyan" };

        static Theme()
        {
            Font = new FontFamily("楷体, KaiTi, STKaiti, 华文楷体, Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
            FontTitle = new FontFamily("华文新魏, STXinwei, 隶书, LiSu, STLiti, 楷体, KaiTi, Microsoft YaHei UI");

            // 窗口最外的深色木框
            Bg = C("#26170F");
            BgAlt = C("#3A2415");
            Wood = C("#3A2415");
            WoodHi = C("#4E3320");
            Brocade = C("#6E2C1F");

            // 明黄绢面：内容区与卡片
            Silk = C("#F4E6BC");
            SilkDeep = C("#EBD9A6");
            SilkEdge = C("#E0C98E");
            Panel = C("#F9EECD");
            PanelHi = C("#F1E0B2");
            PanelSoft = C("#F3E5BC");

            // 金线与墨色
            Gold = C("#C9A227");
            GoldSoft = C("#E4C87E");
            Border = C("#CBA75E");
            BorderHi = C("#E4C87E");
            Ink = C("#3A2A17");
            Seal = C("#A62824");

            Ivory = C("#FDF6E3");
            IvoryLine = C("#C0A46B");
            IvoryInk = C("#8B1A1A");

            Text = C("#3A2A17");
            TextMuted = C("#7C6641");
            TextFaint = C("#A48E63");
            TextOnWood = C("#E6D4A6");
            TextOnWoodFaint = C("#B49C6E");
            OnAccent = C("#FFF7E4");
            Success = C("#3E7A4A");
            Warning = C("#BE8A2C");
            Danger = C("#A62824");
            Purple = C("#6E4B7C");
            SetAccent("rose");
        }

        public static string AccentLabel(string key)
        {
            switch (key)
            {
                case "blue": return "石青";
                case "cyan": return "靛青";
                case "green": return "黛绿";
                case "amber": return "藤黄";
                case "violet": return "紫檀";
                default: return "朱砂";
            }
        }

        public static void SetAccent(string key)
        {
            string hex = "#B23A2A";                                  // 朱砂
            switch (key)
            {
                case "blue": hex = "#2F5A7A"; break;                 // 石青
                case "cyan": hex = "#2E6E78"; break;                 // 靛青
                case "green": hex = "#3E7A4A"; break;                // 黛绿
                case "amber": hex = "#BE8A2C"; break;                // 藤黄
                case "violet": hex = "#7A4B6B"; break;               // 紫檀
            }
            Accent = C(hex);
            AccentDeep = Blend(Accent, Bg, 0.30);
            AccentSoft = Blend(Accent, Panel, 0.86);
            Cache.Clear();
        }

        public static Color PriorityColor(int p)
        {
            if (p >= PriorityLevel.Urgent) return Danger;
            if (p == PriorityLevel.Important) return Warning;
            return TextFaint;
        }

        public static Color C(string hex)
        {
            hex = hex.Replace("#", "");
            byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
            return Color.FromRgb(r, g, b);
        }

        public static Color Blend(Color a, Color b, double t)
        {
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, double a)
        {
            return Color.FromArgb((byte)Math.Max(0, Math.Min(255, Math.Round(a * 255))), c.R, c.G, c.B);
        }

        public static SolidColorBrush B(Color c)
        {
            uint key = ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
            SolidColorBrush br;
            if (Cache.TryGetValue(key, out br)) return br;
            br = new SolidColorBrush(c);
            br.Freeze();
            if (Cache.Count < 512) Cache[key] = br;
            return br;
        }

        public static SolidColorBrush B(string hex) { return B(C(hex)); }

        /// <summary>透明但可交互的画笔。</summary>
        public static SolidColorBrush Transparent { get { return B(Colors.Transparent); } }
    }
}
