using System.Globalization;
using System.Windows.Data;

namespace Memopad;

/// <summary>親の幅の半分を返す。検索バーの入力欄を、ウィンドウ幅に応じてほどよい広さにするために使う。</summary>
public sealed class HalfWidthConverter : IValueConverter
{
    public static readonly HalfWidthConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d && !double.IsNaN(d) ? Math.Max(200, d * 0.45) : 200.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
