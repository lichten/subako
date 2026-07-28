using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TweetViewer.Converters;

/// <summary>
/// value と ConverterParameter が等しいときだけ Visible (メニューのチェック印用)。
/// MenuItem.IsCheckable + IsChecked のバインドは、クリック時に IsChecked へ
/// ローカル値が書き込まれてバインドが恒久的に壊れるため使えない。
/// </summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Equals(value, parameter) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
