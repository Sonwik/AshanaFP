namespace Ashana.UI;

/// <summary>Sorting and paging used by <c>DataTable</c>. Pure functions, so they are unit-tested on their own.</summary>
internal static class TableQuery
{
    public static IReadOnlyList<TItem> Sort<TItem>(IReadOnlyList<TItem> items, Func<TItem, object?>? key, bool ascending)
    {
        if (key is null)
        {
            return items;
        }

        var comparer = Comparer<object?>.Default;
        return (ascending ? items.OrderBy(key, comparer) : items.OrderByDescending(key, comparer)).ToList();
    }

    public static int PageCount(int itemCount, int pageSize) =>
        pageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(itemCount / (double)pageSize));

    /// <summary>Items of the zero-based <paramref name="page"/>; out-of-range pages are clamped.</summary>
    public static IReadOnlyList<TItem> Page<TItem>(IReadOnlyList<TItem> items, int page, int pageSize)
    {
        if (pageSize <= 0)
        {
            return items;
        }

        var safePage = Math.Clamp(page, 0, PageCount(items.Count, pageSize) - 1);
        return items.Skip(safePage * pageSize).Take(pageSize).ToList();
    }
}
