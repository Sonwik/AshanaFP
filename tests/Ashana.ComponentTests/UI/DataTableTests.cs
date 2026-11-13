using Ashana.ComponentTests.Support;
using Ashana.UI;
using Ashana.UI.Components;
using Microsoft.AspNetCore.Components;

namespace Ashana.ComponentTests.UI;

public sealed class DataTableTests : IAsyncDisposable
{
    private static readonly Dish[] Dishes =
    [
        new("Plov", 1800m),
        new("Black tea", 300m),
        new("Lagman", 1700m),
        new("Samsa", 600m),
        new("Ayran", 400m),
    ];

    private readonly ComponentTestContext _ctx = new();

    public ValueTask DisposeAsync() => _ctx.DisposeAsync();

    [Fact]
    public async Task Columns_declared_as_children_become_headers_and_cells()
    {
        var table = await RenderTable(pageSize: 0);

        Assert.Equal(["Dish", "Price"], table.FindAll("th").Select(th => th.Text));
        Assert.Equal(5, table.FindAll("tr").Count - 1); // minus the header row
        Assert.Equal("Price", table.FindAll("td")[1]["data-label"]); // phones show the column name next to the value
    }

    [Fact]
    public async Task Clicking_a_sortable_header_sorts_then_reverses()
    {
        var table = await RenderTable(pageSize: 0);
        var priceHeader = table.Find("button", b => b.Text == "Price");

        await table.ClickAsync(priceHeader);
        var ascending = FirstColumn(table);
        await table.ClickAsync(table.Find("button", b => b.Text == "Price"));
        var descending = FirstColumn(table);

        Assert.Equal(["Black tea", "Ayran", "Samsa", "Lagman", "Plov"], ascending);
        Assert.Equal(["Plov", "Lagman", "Samsa", "Ayran", "Black tea"], descending);
        Assert.Equal("descending", table.FindAll("th")[1]["aria-sort"]);
        Assert.Null(table.FindAll("th")[0]["aria-sort"]); // "Dish" is not sortable in this table
    }

    [Fact]
    public async Task Long_lists_are_split_into_pages()
    {
        var table = await RenderTable(pageSize: 2);

        Assert.Equal(["Plov", "Black tea"], FirstColumn(table));
        Assert.Contains("Page 1 of 3", table.Text);
        Assert.True(table.Find("button", b => b["aria-label"] == "Previous page").Has("disabled"));

        await table.ClickAsync(table.Find("button", b => b["aria-label"] == "Next page"));
        await table.ClickAsync(table.Find("button", b => b["aria-label"] == "Next page"));

        Assert.Equal(["Ayran"], FirstColumn(table));
        Assert.True(table.Find("button", b => b["aria-label"] == "Next page").Has("disabled"));
    }

    [Fact]
    public async Task Empty_table_shows_a_message_across_all_columns()
    {
        var table = await RenderTable(pageSize: 10, items: []);

        var cell = Assert.Single(table.FindAll("td"));
        Assert.Equal("No dishes match the filters.", cell.Text);
        Assert.Equal("2", cell["colspan"]);
    }

    [Fact]
    public void Paging_math_handles_edge_cases()
    {
        Assert.Equal(1, TableQuery.PageCount(0, 10));
        Assert.Equal(3, TableQuery.PageCount(21, 10));
        Assert.Equal(1, TableQuery.PageCount(500, 0)); // page size 0 = everything on one page
        Assert.Equal([5], TableQuery.Page([1, 2, 3, 4, 5], page: 99, pageSize: 2)); // out of range: last page
        Assert.Equal([3, 1, 2], TableQuery.Sort([3, 1, 2], key: null, ascending: true)); // no sort key: keep order
    }

    private static List<string> FirstColumn(Rendered<DataTable<Dish>> table) =>
        table.FindAll("td", td => td["data-label"] == "Dish").Select(td => td.Text).ToList();

    private Task<Rendered<DataTable<Dish>>> RenderTable(int pageSize, IReadOnlyList<Dish>? items = null) =>
        _ctx.RenderAsync<DataTable<Dish>>(new Dictionary<string, object?>
        {
            [nameof(DataTable<Dish>.Items)] = items ?? Dishes,
            [nameof(DataTable<Dish>.RowKey)] = (Func<Dish, object>)(d => d.Name),
            [nameof(DataTable<Dish>.PageSize)] = pageSize,
            [nameof(DataTable<Dish>.Caption)] = "Dishes",
            [nameof(DataTable<Dish>.EmptyContent)] = Fragments.Text("No dishes match the filters."),
            [nameof(DataTable<Dish>.Columns)] = (RenderFragment)(builder =>
            {
                builder.OpenComponent<DataColumn<Dish>>(0);
                builder.AddAttribute(1, nameof(DataColumn<Dish>.Title), "Dish");
                builder.AddAttribute(2, nameof(DataColumn<Dish>.ChildContent), (RenderFragment<Dish>)(d => b => b.AddContent(0, d.Name)));
                builder.CloseComponent();

                builder.OpenComponent<DataColumn<Dish>>(3);
                builder.AddAttribute(4, nameof(DataColumn<Dish>.Title), "Price");
                builder.AddAttribute(5, nameof(DataColumn<Dish>.SortBy), (Func<Dish, object?>)(d => d.Price));
                builder.AddAttribute(6, nameof(DataColumn<Dish>.AlignEnd), true);
                builder.AddAttribute(7, nameof(DataColumn<Dish>.ChildContent), (RenderFragment<Dish>)(d => b => b.AddContent(0, d.Price)));
                builder.CloseComponent();
            }),
        });

    public sealed record Dish(string Name, decimal Price);
}
