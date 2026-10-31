using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using YellowEditorX.Core;

namespace YellowEditorX;

/// <summary>One line of the card list.</summary>
public sealed class CardRow : INotifyPropertyChanged
{
    bool isCurrent;
    public Card Card { get; }
    public string Code { get; }
    public string Name { get; }
    public string Kind { get; }
    public string LevelText { get; }
    public string Stats { get; }
    /// <summary>Lower-case text the filter searches in.</summary>
    public string SearchText { get; }

    public CardRow(Card c, CardInfo info)
    {
        Card = c;
        Code = c.Id.ToString();
        Name = c.Name;
        Kind = info.TypeText(c);
        LevelText = CardInfo.LevelText(c);
        Stats = CardInfo.AtkDefText(c);
        SearchText = (Code + "\n" + c.Name + "\n" + Kind + "\n" + c.Desc).ToLowerInvariant();
    }

    public bool IsCurrent
    {
        get => isCurrent;
        set
        {
            if (isCurrent == value) return;
            isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class NotVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
