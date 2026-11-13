namespace Ashana.Domain.Menu;

/// <summary>Dietary and marketing labels. Stored as bit flags, so one value holds any combination.</summary>
[Flags]
public enum MenuTags
{
    None = 0,
    Vegetarian = 1,
    Spicy = 2,
    Halal = 4,
    New = 8,
    GlutenFree = 16,
}
