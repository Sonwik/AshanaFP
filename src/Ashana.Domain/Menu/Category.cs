using Ashana.Domain.Common;

namespace Ashana.Domain.Menu;

/// <summary>Group of dishes shown as a tab on the menu (Mains, Soups, Drinks...). An immutable record.</summary>
public sealed record Category : IEntity
{
    public const int NameMaxLength = 30;

    private Category()
    {
    }

    public Guid Id { get; private init; }

    public int Version { get; private init; }

    public string Name { get; private init; } = string.Empty;

    /// <summary>Emoji used as the category icon.</summary>
    public string Icon { get; private init; } = string.Empty;

    public int SortOrder { get; private init; }

    public static Result<Category> Create(string name, string icon, int sortOrder, Guid? id = null) =>
        ValidateName(name).Map(valid => new Category
        {
            Id = id ?? Guid.NewGuid(),
            Name = valid,
            Icon = NormalizeIcon(icon),
            SortOrder = sortOrder,
        });

    public Result<Category> Rename(string name, string icon, int sortOrder) =>
        ValidateName(name).Map(valid => this with
        {
            Name = valid,
            Icon = NormalizeIcon(icon),
            SortOrder = sortOrder,
            Version = Version + 1,
        });

    /// <summary>Returns the trimmed name, or the validation error.</summary>
    private static Result<string> ValidateName(string? name) =>
        Result.Success((name ?? string.Empty).Trim())
            .Ensure(n => n.Length is > 0 and <= NameMaxLength, Error.Validation("menu.category_name", $"Category name must be 1–{NameMaxLength} characters."));

    private static string NormalizeIcon(string? icon) => string.IsNullOrWhiteSpace(icon) ? "🍽️" : icon.Trim();
}
