using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TimePlanner.Core
{
    /// <summary>全局配色与字体（深色主题）。</summary>
    public static class Theme
    {
        public static Color Bg, BgAlt, Panel, PanelHi, PanelSoft, Border, BorderHi;
        public static Color Text, TextMuted, TextFaint;
        public static Color Accent, AccentSoft, AccentDeep, OnAccent;
        public static Color Success, Warning, Danger, Purple;
        public static FontFamily Font;

        static readonly Dictionary<uint, SolidColorBrush> Cache = new Dictionary<uint, SolidColorBrush>();

        public static readonly string[] AccentKeys = { "blue", "violet", "cyan", "green", "amber", "rose" };

        static Theme()
        {
            Font = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI, Arial");
            Bg = C("#0D0F14");
            BgAlt = C("#12151D");
            Panel = C("#171B24");
            PanelHi = C("#20252F");
            PanelSoft = C("#141821");
            Border = C("#262B37");
            BorderHi = C("#39404F");
            Text = C("#EAEDF5");
            TextMuted = C("#96A0B5");
            TextFaint = C("#616B80");
            OnAccent = C("#0B0E14");
            Success = C("#35C48D");
            Warning = C("#F2A33C");
            Danger = C("#F0576B");
            Purple = C("#A77BFF");
            SetAccent("blue");
        }

        public static string AccentLabel(string key)
        {
            switch (key)
            {
                case "violet": return "紫罗兰";
                case "cyan": return "青蓝";
                case "green": return "薄荷";
                case "amber": return "琥珀";
                case "rose": return "玫红";
                default: return "晶蓝";
            }
        }

        public static void SetAccent(string key)
        {
            string hex = "#5B8CFF";
            switch (key)
            {
                case "violet": hex = "#A77BFF"; break;
                case "cyan": hex = "#3FC5DD"; break;
                case "green": hex = "#3FCB93"; break;
                case "amber": hex = "#F0A73E"; break;
                case "rose": hex = "#FF7391"; break;
            }
            Accent = C(hex);
            AccentDeep = Blend(Accent, Bg, 0.34);
            AccentSoft = Blend(Accent, Panel, 0.80);
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
