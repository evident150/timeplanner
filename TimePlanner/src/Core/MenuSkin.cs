using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TimePlanner.Core
{
    /// <summary>右键菜单皮肤：系统默认的是浅色 Aero 菜单，和圣旨版的木牍界面完全不搭，这里整块自绘。</summary>
    public static class MenuSkin
    {
        static Style _menuStyle;
        static ControlTemplate _itemTemplate;

        const string ItemHot = "#462B18";       // 悬停：木色提亮
        const string MenuBg = "#2E1C10";        // 木牍
        const string MenuLine = "#553F1E";

        /// <summary>建一个套好皮肤的菜单容器。</summary>
        public static ContextMenu Create()
        {
            ContextMenu m = new ContextMenu();
            m.Style = MenuStyle();
            m.HasDropShadow = false;
            m.FontFamily = Theme.Font;
            m.FontSize = 13;
            m.Padding = new Thickness(0);
            return m;
        }

        /// <summary>加一行：图标 + 文字（+ 表示当前状态的圆点）。</summary>
        public static MenuRow Add(ContextMenu menu, string icon, string text, Action onClick, bool active, bool danger)
        {
            MenuRow row = new MenuRow(icon, text, danger);
            row.Item.Click += delegate(object s, RoutedEventArgs e) { if (onClick != null) onClick(); };
            row.SetActive(active);
            menu.Items.Add(row.Item);
            return row;
        }

        /// <summary>分隔线（自己画一条，不用系统的 Separator，免得被主题换成浅色）。</summary>
        public static void Line(ContextMenu menu)
        {
            Border b = new Border();
            b.Height = 1;
            b.Background = Theme.B(Theme.C(MenuLine));
            b.Margin = new Thickness(11, 6, 11, 6);
            menu.Items.Add(b);
        }

        /// <summary>菜单里的一行。状态只用颜色和圆点表示，不动字号字重，免得弹出后菜单再变宽。</summary>
        public class MenuRow
        {
            public readonly MenuItem Item;
            readonly TextBlock label;
            readonly Path glyph;
            readonly Ellipse dot;
            readonly bool danger;
            bool active;

            internal MenuRow(string icon, string text, bool danger)
            {
                this.danger = danger;
                label = Ui.Txt(text, 13, Theme.B(Theme.TextOnWood), false);
                label.VerticalAlignment = VerticalAlignment.Center;

                glyph = Ui.IconPath(icon, 13.5, Theme.B(Theme.TextOnWoodFaint), 1.45);
                glyph.HorizontalAlignment = HorizontalAlignment.Left;
                glyph.VerticalAlignment = VerticalAlignment.Center;

                dot = new Ellipse();
                dot.Width = 6;
                dot.Height = 6;
                dot.Fill = Theme.B(Theme.Accent);
                dot.VerticalAlignment = VerticalAlignment.Center;
                dot.Margin = new Thickness(16, 0, 1, 0);
                dot.Opacity = 0;

                Grid g = new Grid();
                ColumnDefinition c0 = new ColumnDefinition();
                c0.Width = new GridLength(24);
                g.ColumnDefinitions.Add(c0);
                g.ColumnDefinitions.Add(new ColumnDefinition());
                ColumnDefinition c2 = new ColumnDefinition();
                c2.Width = GridLength.Auto;
                g.ColumnDefinitions.Add(c2);
                Grid.SetColumn(glyph, 0);
                g.Children.Add(glyph);
                Grid.SetColumn(label, 1);
                g.Children.Add(label);
                Grid.SetColumn(dot, 2);
                g.Children.Add(dot);

                Border box = new Border();
                box.Padding = new Thickness(9, 6.5, 12, 7);
                box.Child = g;

                Item = new MenuItem();
                Item.Template = ItemTemplate();
                Item.Background = Theme.B(Colors.Transparent);
                Item.Header = box;
                Item.MouseEnter += delegate(object s, MouseEventArgs e) { Hover(true); };
                Item.MouseLeave += delegate(object s, MouseEventArgs e) { Hover(false); };
            }

            public void SetActive(bool on)
            {
                active = on;
                Paint();
            }

            void Hover(bool on)
            {
                if (on)
                {
                    Item.Background = Theme.B(ItemHot);
                    label.Foreground = Theme.B(danger ? Theme.Danger : Theme.C("#FFF1DF"));
                    glyph.Stroke = Theme.B(danger ? Theme.Danger : Theme.C("#FFF1DF"));
                }
                else
                {
                    Item.Background = Theme.B(Colors.Transparent);
                    Paint();
                }
            }

            void Paint()
            {
                Color idle = danger ? Theme.Danger : (active ? Theme.Accent : Theme.TextOnWood);
                Color idleIcon = danger ? Theme.Danger : (active ? Theme.Accent : Theme.TextOnWoodFaint);
                label.Foreground = Theme.B(idle);
                glyph.Stroke = Theme.B(idleIcon);
                dot.Opacity = active ? 1 : 0;
            }
        }

        static Style MenuStyle()
        {
            if (_menuStyle != null) return _menuStyle;
            string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ContextMenu}'>" +
                "<Setter Property='OverridesDefaultStyle' Value='True'/>" +
                "<Setter Property='Template'><Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type ContextMenu}'>" +
                "<Border MinWidth='188' Background='" + MenuBg + "' BorderBrush='#7A5612' BorderThickness='1' " +
                "CornerRadius='12' Padding='3,6,3,6' SnapsToDevicePixels='True'>" +
                "<Border.Effect>" +
                "<DropShadowEffect BlurRadius='16' ShadowDepth='4' Direction='270' Opacity='0.55' Color='#000000'/>" +
                "</Border.Effect>" +
                "<ItemsPresenter/>" +
                "</Border>" +
                "</ControlTemplate>" +
                "</Setter.Value></Setter>" +
                "</Style>";
            _menuStyle = (Style)System.Windows.Markup.XamlReader.Parse(xaml);
            return _menuStyle;
        }

        static ControlTemplate ItemTemplate()
        {
            if (_itemTemplate != null) return _itemTemplate;
            string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type MenuItem}'>" +
                "<Border x:Name='Bd' Background='{TemplateBinding Background}' CornerRadius='8' " +
                "Margin='0,1,0,1' SnapsToDevicePixels='True'>" +
                "<ContentPresenter ContentSource='Header'/>" +
                "</Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsHighlighted' Value='True'>" +
                "<Setter TargetName='Bd' Property='Background' Value='" + ItemHot + "'/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>";
            _itemTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
            return _itemTemplate;
        }
    }
}
