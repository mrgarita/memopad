using System.Globalization;
using System.Windows.Data;

namespace Memopad;

/// <summary>幅から指定した値（ConverterParameter）を引く。タブ一覧の最大幅を「＋ボタンの分だけ狭く」するために使う。</summary>
public sealed class MinusConverter : IValueConverter
{
    public static readonly MinusConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var width = value is double d ? d : 0;
        var minus = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 0;
        return Math.Max(0, width - minus);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
