using SIV.UI.ViewModels;

namespace SIV.UI.Services;

public static class SortHelper
{
    public static IEnumerable<InventoryItemViewModel> SortItems(
        IEnumerable<InventoryItemViewModel> items, SortState sort)
    {
        return sort.Field switch
        {
            SortField.Newest => OrderBy(items, i => i.NewestSortKey, sort.Direction),
            SortField.Rarity => OrderBy(items, i => i.Rarity, sort.Direction),
            SortField.Type => OrderBy(items, i => i.BaseName, sort.Direction, StringComparer.OrdinalIgnoreCase),
            SortField.Price => OrderBy(items, i => i.PriceUsd ?? 0, sort.Direction),
            SortField.Float => OrderBy(items, i => i.PaintWear, sort.Direction),
            _ => OrderBy(items, i => i.Name, sort.Direction, StringComparer.OrdinalIgnoreCase),
        };
    }

    public static IEnumerable<InventoryGroupViewModel> SortGroups(
        IEnumerable<InventoryGroupViewModel> groups, SortState sort)
    {
        return sort.Field switch
        {
            SortField.Newest => OrderBy(groups, g => g.Items.Select(i => i.NewestSortKey).DefaultIfEmpty(0).Max(), sort.Direction),
            SortField.Quantity => OrderBy(groups, g => g.Count, sort.Direction),
            SortField.Rarity => OrderBy(groups, g => g.Items.FirstOrDefault()?.Rarity ?? 0, sort.Direction),
            SortField.Type => OrderBy(groups, g => g.Items.FirstOrDefault()?.BaseName ?? "", sort.Direction, StringComparer.OrdinalIgnoreCase),
            SortField.Price => OrderBy(groups, g => g.TotalPriceUsd ?? 0, sort.Direction),
            SortField.Float => OrderBy(groups, g => g.Items.FirstOrDefault()?.PaintWear ?? 0, sort.Direction),
            _ => OrderBy(groups, g => g.Name, sort.Direction, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static IOrderedEnumerable<InventoryItemViewModel> OrderBy<TKey>(
        IEnumerable<InventoryItemViewModel> source, Func<InventoryItemViewModel, TKey> keySelector,
        SortDirection direction, IComparer<TKey>? comparer = null)
    {
        var ordered = direction == SortDirection.Ascending
            ? (comparer is not null ? source.OrderBy(keySelector, comparer) : source.OrderBy(keySelector))
            : (comparer is not null ? source.OrderByDescending(keySelector, comparer) : source.OrderByDescending(keySelector));
        return ordered.ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static IOrderedEnumerable<InventoryGroupViewModel> OrderBy<TKey>(
        IEnumerable<InventoryGroupViewModel> source, Func<InventoryGroupViewModel, TKey> keySelector,
        SortDirection direction, IComparer<TKey>? comparer = null)
    {
        var ordered = direction == SortDirection.Ascending
            ? (comparer is not null ? source.OrderBy(keySelector, comparer) : source.OrderBy(keySelector))
            : (comparer is not null ? source.OrderByDescending(keySelector, comparer) : source.OrderByDescending(keySelector));
        return ordered.ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase);
    }
}
