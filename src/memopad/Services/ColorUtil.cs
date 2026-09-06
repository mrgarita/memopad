using System.Globalization;
using System.Windows.Media;

namespace Memopad.Services;

/// <summary>色の文字列変換と、標準色 16 色の定義。</summary>
public static class ColorUtil
{
    /// <summary>"#RRGGBB" を Color に変換する。空や不正な文字列なら null。</summary>
    public static Color? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return null;
        return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>標準色 16 色（Windows の基本 16 色）。名前は日本語で表示する。</summary>
    public static readonly (string Name, Color Color)[] StandardColors =
    {
        ("黒", Color.FromRgb(0x00, 0x00, 0x00)),
        ("茶", Color.FromRgb(0x80, 0x00, 0x00)),
        ("緑", Color.FromRgb(0x00, 0x80, 0x00)),
        ("オリーブ", Color.FromRgb(0x80, 0x80, 0x00)),
        ("紺", Color.FromRgb(0x00, 0x00, 0x80)),
        ("紫", Color.FromRgb(0x80, 0x00, 0x80)),
        ("青緑", Color.FromRgb(0x00, 0x80, 0x80)),
        ("銀", Color.FromRgb(0xC0, 0xC0, 0xC0)),
        ("灰", Color.FromRgb(0x80, 0x80, 0x80)),
        ("赤", Color.FromRgb(0xFF, 0x00, 0x00)),
        ("黄緑", Color.FromRgb(0x00, 0xFF, 0x00)),
        ("黄", Color.FromRgb(0xFF, 0xFF, 0x00)),
        ("青", Color.FromRgb(0x00, 0x00, 0xFF)),
        ("赤紫", Color.FromRgb(0xFF, 0x00, 0xFF)),
        ("水色", Color.FromRgb(0x00, 0xFF, 0xFF)),
        ("白", Color.FromRgb(0xFF, 0xFF, 0xFF)),
    };
}
