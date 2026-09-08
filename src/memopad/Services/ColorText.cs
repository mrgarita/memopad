using System.Globalization;

namespace Memopad.Services;

/// <summary>
/// 設定の色文字列（"#RRGGBB"）を Windows Forms の色に変換する。
/// ColorUtil は WPF の Color を使うため、起動時（WPF を読み込まない）にはこちらを使う。
/// </summary>
public static class ColorText
{
    /// <summary>"#RRGGBB" を Color に変換する。空や不正な文字列なら null。</summary>
    public static System.Drawing.Color? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return null;
        return System.Drawing.Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
    }
}
