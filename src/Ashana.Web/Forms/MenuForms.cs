using System.ComponentModel.DataAnnotations;
using Ashana.Application.Menu;
using Ashana.Domain.Menu;

namespace Ashana.Web.Forms;

/// <summary>Fails for <see cref="Guid.Empty"/>; [Required] alone does not, because a Guid is never null.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotEmptyAttribute : ValidationAttribute
{
    public NotEmptyAttribute()
        : base("Please choose a value.")
    {
    }

    public override bool IsValid(object? value) => value is not Guid id || id != Guid.Empty;
}

/// <summary>Add / edit dish form. Flags are plain booleans so each one binds to a checkbox.</summary>
public sealed class MenuItemForm
{
    [NotEmpty(ErrorMessage = "Choose a category.")]
    public Guid CategoryId { get; set; }

    [Required(ErrorMessage = "Enter the dish name.")]
    [StringLength(MenuItem.NameMaxLength, ErrorMessage = "Name must be at most 60 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(MenuItem.DescriptionMaxLength, ErrorMessage = "Description must be at most 300 characters.")]
    public string? Description { get; set; }

    [Range(1, MenuErrors.MaxPrice, ErrorMessage = "Price must be between 1 and 100 000 ₸.")]
    public decimal Price { get; set; } = 1000;

    [Range(0, MenuErrors.MaxDailyStock, ErrorMessage = "Daily portions must be between 0 and 1000.")]
    public int DailyStock { get; set; } = 30;

    [Range(1, 120, ErrorMessage = "Preparation time must be between 1 and 120 minutes.")]
    public int PrepMinutes { get; set; } = 5;

    [Range(0, 5000, ErrorMessage = "Calories must be between 0 and 5000.")]
    public int? Calories { get; set; }

    [StringLength(8, ErrorMessage = "Use one emoji.")]
    public string Emoji { get; set; } = "🍽️";

    [Url(ErrorMessage = "Enter a full link that starts with https://")]
    [StringLength(500)]
    public string? ImageUrl { get; set; }

    public bool Vegetarian { get; set; }

    public bool Spicy { get; set; }

    public bool Halal { get; set; }

    public bool GlutenFree { get; set; }

    public bool IsNew { get; set; }

    public MenuTags Tags =>
        (Vegetarian ? MenuTags.Vegetarian : MenuTags.None) |
        (Spicy ? MenuTags.Spicy : MenuTags.None) |
        (Halal ? MenuTags.Halal : MenuTags.None) |
        (GlutenFree ? MenuTags.GlutenFree : MenuTags.None) |
        (IsNew ? MenuTags.New : MenuTags.None);

    public static MenuItemForm From(MenuItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new MenuItemForm
        {
            CategoryId = item.CategoryId,
            Name = item.Name,
            Description = item.Description,
            Price = item.Price,
            DailyStock = item.DailyStock,
            PrepMinutes = item.PrepMinutes,
            Calories = item.Calories,
            Emoji = item.Emoji,
            ImageUrl = item.ImageUrl,
            Vegetarian = item.Has(MenuTags.Vegetarian),
            Spicy = item.Has(MenuTags.Spicy),
            Halal = item.Has(MenuTags.Halal),
            GlutenFree = item.Has(MenuTags.GlutenFree),
            IsNew = item.Has(MenuTags.New),
        };
    }

    public MenuItemDetails ToDetails() =>
        new(CategoryId, Name, Description ?? string.Empty, Price, Emoji, ImageUrl, Calories, Tags, PrepMinutes, DailyStock);

    /// <summary>A fake DTO for the live "how it looks on the menu" preview.</summary>
    public MenuItemDto ToPreview(Guid id, int remaining) =>
        new(id, CategoryId, string.IsNullOrWhiteSpace(Name) ? "Dish name" : Name, Description ?? string.Empty, Price,
            string.IsNullOrWhiteSpace(Emoji) ? "🍽️" : Emoji, ImageUrl, Calories, Tags, PrepMinutes, DailyStock, remaining, true, false, 0);
}

public sealed class CategoryForm
{
    [Required(ErrorMessage = "Enter a category name.")]
    [StringLength(Category.NameMaxLength, ErrorMessage = "Name must be at most 30 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(8, ErrorMessage = "Use one emoji.")]
    public string Icon { get; set; } = "🍽️";
}
