using System.ComponentModel.DataAnnotations;
using Ashana.Domain.Canteen;

namespace Ashana.Web.Forms;

/// <summary>Canteen settings form. Cross-field rules (closing after opening) use <see cref="IValidatableObject"/>.</summary>
public sealed class SettingsForm : IValidatableObject
{
    public TimeOnly OpensAt { get; set; }

    public TimeOnly ClosesAt { get; set; }

    [Range(5, 60, ErrorMessage = "Slot length must be between 5 and 60 minutes.")]
    public int SlotMinutes { get; set; }

    [Range(1, 500, ErrorMessage = "Orders per slot must be between 1 and 500.")]
    public int MaxOrdersPerSlot { get; set; }

    [Range(0, 180, ErrorMessage = "Lead time must be between 0 and 180 minutes.")]
    public int MinLeadMinutes { get; set; }

    [Range(5, 240, ErrorMessage = "No-show timeout must be between 5 and 240 minutes.")]
    public int NoShowAfterMinutes { get; set; }

    [Range(1, 10, ErrorMessage = "Active orders per student must be between 1 and 10.")]
    public int MaxActiveOrdersPerUser { get; set; }

    public bool AcceptingOrders { get; set; }

    [StringLength(140, ErrorMessage = "Pause message must be at most 140 characters.")]
    public string? PauseMessage { get; set; }

    public static SettingsForm From(CanteenRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return new SettingsForm
        {
            OpensAt = rules.OpensAt,
            ClosesAt = rules.ClosesAt,
            SlotMinutes = rules.SlotMinutes,
            MaxOrdersPerSlot = rules.MaxOrdersPerSlot,
            MinLeadMinutes = rules.MinLeadMinutes,
            NoShowAfterMinutes = rules.NoShowAfterMinutes,
            MaxActiveOrdersPerUser = rules.MaxActiveOrdersPerUser,
            AcceptingOrders = rules.AcceptingOrders,
            PauseMessage = rules.PauseMessage,
        };
    }

    public CanteenRules ToRules() => new(
        OpensAt, ClosesAt, SlotMinutes, MaxOrdersPerSlot, MinLeadMinutes,
        NoShowAfterMinutes, MaxActiveOrdersPerUser, AcceptingOrders, PauseMessage);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ClosesAt <= OpensAt)
        {
            yield return new ValidationResult("Closing time must be later than opening time.", [nameof(ClosesAt)]);
        }
        else if ((ClosesAt - OpensAt).TotalMinutes < SlotMinutes)
        {
            yield return new ValidationResult("Working hours must fit at least one pickup slot.", [nameof(ClosesAt)]);
        }
    }
}
