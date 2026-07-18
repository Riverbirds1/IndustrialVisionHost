using System;
using System.Windows;
using System.Windows.Media;

namespace IndustrialVisionHost.Services
{
    public static class ThemeService
    {
        public static void Apply(string themeName)
        {
            bool dark = string.Equals(
                themeName,
                "Dark",
                StringComparison.Ordinal);
            ResourceDictionary resources = Application.Current.Resources;

            resources["AppBackgroundBrush"] = Brush(
                dark ? 0x11 : 0xF3,
                dark ? 0x18 : 0xF4,
                dark ? 0x27 : 0xF6);
            resources["SurfaceBrush"] = Brush(
                dark ? 0x1F : 0xFF,
                dark ? 0x29 : 0xFF,
                dark ? 0x37 : 0xFF);
            resources["SubtleSurfaceBrush"] = Brush(
                dark ? 0x27 : 0xF8,
                dark ? 0x34 : 0xFA,
                dark ? 0x49 : 0xFC);
            resources["AppBorderBrush"] = Brush(
                dark ? 0x4B : 0xD1,
                dark ? 0x55 : 0xD5,
                dark ? 0x63 : 0xDB);
            resources["PrimaryTextBrush"] = Brush(
                dark ? 0xF3 : 0x11,
                dark ? 0xF4 : 0x18,
                dark ? 0xF6 : 0x27);
        }

        private static SolidColorBrush Brush(int red, int green, int blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(
                checked((byte)red),
                checked((byte)green),
                checked((byte)blue)));
            brush.Freeze();
            return brush;
        }
    }
}
