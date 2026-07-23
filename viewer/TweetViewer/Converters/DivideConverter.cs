using System.Globalization;
using System.Windows.Data;

namespace TweetViewer.Converters;

/// <summary>数値を parameter で割る (メディアグリッドの正方形セル高さ = 幅÷3 用)。</summary>
public sealed class DivideConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var input = value is double d ? d : System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        var divisor = parameter is null ? 1.0 : System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
        return divisor == 0 ? input : input / divisor;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
