namespace Ashana.UI;

public enum ButtonVariant
{
    /// <summary>Main action of the screen (saffron).</summary>
    Primary,

    /// <summary>Normal action (outlined).</summary>
    Secondary,

    /// <summary>Low-emphasis action (text only).</summary>
    Ghost,

    /// <summary>Destructive action (red).</summary>
    Danger,

    /// <summary>Strong brand action (turquoise), for example on the kitchen board.</summary>
    Tile,
}

public enum ButtonSize
{
    Small,
    Medium,
    Large,
}

/// <summary>Color of a badge. Order statuses map to Info/Warning/Success/Neutral/Danger.</summary>
public enum Tone
{
    Neutral,
    Info,
    Warning,
    Success,
    Danger,
    Accent,
    Saffron,
}

public enum ModalSize
{
    Small,
    Medium,
    Large,
}

public enum StepState
{
    Done,
    Current,
    Upcoming,
    Failed,
}

/// <summary>A step in <c>StatusSteps</c>.</summary>
public sealed record StepItem(string Label, string? Detail, StepState State);

/// <summary>A value in <c>BarChart</c> or <c>RankList</c>.</summary>
public sealed record ChartPoint(string Label, double Value, string? Tooltip = null, string? Emoji = null);

/// <summary>An option of <c>SegmentedControl</c>.</summary>
public sealed record SegmentOption<TValue>(TValue Value, string Text, string? Icon = null);
