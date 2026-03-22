using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SIV.UI.Services;

public enum SortField
{
    Newest,
    Name,
    Rarity,
    Type,
    Quantity,
    Price,
    Float
}

public enum SortDirection
{
    Ascending,
    Descending
}

public class SortOption
{
    public SortField Field { get; }
    public string Label { get; }

    public SortOption(SortField field, string label)
    {
        Field = field;
        Label = label;
    }

    public override string ToString() => Label;
}

public partial class SortState : ObservableObject
{
    public static readonly ObservableCollection<SortOption> AllOptions =
    [
        new(SortField.Newest, "Newest"),
        new(SortField.Name, "Name"),
        new(SortField.Rarity, "Rarity"),
        new(SortField.Type, "Type"),
        new(SortField.Quantity, "Quantity"),
        new(SortField.Price, "Price"),
        new(SortField.Float, "Float"),
    ];

    [ObservableProperty]
    private SortField _field = SortField.Newest;

    [ObservableProperty]
    private SortDirection _direction = SortDirection.Descending;

    [ObservableProperty]
    private SortOption _selectedOption;

    public ObservableCollection<SortOption> Options => AllOptions;

    public string DirectionGlyph => Direction == SortDirection.Ascending ? "\uE70E" : "\uE70D";

    public string DisplayText => $"{Field} {(Direction == SortDirection.Ascending ? "↑" : "↓")}";

    public SortState()
    {
        _selectedOption = AllOptions[0];
    }

    partial void OnSelectedOptionChanged(SortOption value)
    {
        if (value is null || _suppressSelectedChange) return;
        var newField = value.Field;
        if (Field == newField) return;
        Field = newField;
        Direction = newField is SortField.Newest or SortField.Rarity or SortField.Price or SortField.Quantity
            ? SortDirection.Descending
            : SortDirection.Ascending;
    }

    public void ToggleDirection()
    {
        Direction = Direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
    }

    private bool _suppressSelectedChange;

    public void Toggle(SortField field)
    {
        if (Field == field)
            Direction = Direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
        else
        {
            Field = field;
            Direction = field is SortField.Newest or SortField.Rarity or SortField.Price or SortField.Quantity
                ? SortDirection.Descending
                : SortDirection.Ascending;
        }
        _suppressSelectedChange = true;
        SelectedOption = AllOptions.First(o => o.Field == Field);
        _suppressSelectedChange = false;
    }

    partial void OnFieldChanged(SortField value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnDirectionChanged(SortDirection value)
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(DirectionGlyph));
    }
}
