using RosterSensei.Application.DTOs;
using RosterSensei.Domain.Entities;
using FluentValidation;

namespace RosterSensei.Application.Validators;

public class SaveWeeklyAvailabilityDtoValidator : AbstractValidator<SaveWeeklyAvailabilityDto>
{
    public SaveWeeklyAvailabilityDtoValidator()
    {
        RuleFor(d => d.TimeZone).MustBeTimeZone();

        RuleFor(d => d.Weekly)
            .Must(days => days is { Count: 7 } && days.All(AvailabilitySlots.IsDay))
            .WithMessage($"Send seven days of {AvailabilitySlots.PerDay} half hours, each '0' or '1'.");
    }
}

public class SaveAvailabilityOverrideDtoValidator : AbstractValidator<SaveAvailabilityOverrideDto>
{
    public SaveAvailabilityOverrideDtoValidator()
    {
        RuleFor(d => d.TimeZone).MustBeTimeZone();

        RuleFor(d => d.Slots)
            .Must(AvailabilitySlots.IsDay)
            .WithMessage($"Send {AvailabilitySlots.PerDay} half hours, each '0' or '1'.");
    }
}

internal static class TimeZoneRules
{
    public static IRuleBuilderOptions<T, string> MustBeTimeZone<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("The time zone is required.")
            .MaximumLength(AvailabilitySlots.MaxTimeZoneLength).WithMessage("That time zone isn't known.")
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out _)).WithMessage("That time zone isn't known.");
}
