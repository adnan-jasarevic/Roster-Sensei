using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Domain.Entities;
using FluentValidation;

namespace RosterSensei.Application.Validators;

// The rules for a match's list of coaches, shared by adding, editing and demo import.
public class MatchCoachesValidator : AbstractValidator<List<MatchCoachDto>>
{
    public MatchCoachesValidator()
    {
        RuleFor(c => c)
            .Cascade(CascadeMode.Stop)
            .Must(c => c.Count <= MatchCoachLimits.MaxCoaches).WithMessage($"A match can have at most {MatchCoachLimits.MaxCoaches} coaches.")
            .Must(c => NoDuplicates(c.Where(x => !string.IsNullOrEmpty(x.UserId)).Select(x => x.UserId!)))
                .WithMessage("A coach can only be listed once.")
            .Must(c => NoDuplicates(c.Where(x => !string.IsNullOrEmpty(x.SteamId)).Select(x => x.SteamId!)))
                .WithMessage("A coach can only be listed once.")
            .OverridePropertyName("Coaches");

        RuleForEach(c => c).SetValidator(new MatchCoachDtoValidator()).OverridePropertyName("Coaches");
    }

    private static bool NoDuplicates(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Distinct().Count() == list.Count;
    }
}

public class MatchCoachDtoValidator : AbstractValidator<MatchCoachDto>
{
    public MatchCoachDtoValidator()
    {
        RuleFor(c => c)
            .Must(c => !string.IsNullOrWhiteSpace(c.UserId) || !string.IsNullOrWhiteSpace(c.Name))
            .OverridePropertyName("Name")
            .WithMessage("A coach from outside the team needs a name.");

        RuleFor(c => c.UserId)
            .MaximumLength(450).WithMessage("The user ID is too long.");

        RuleFor(c => c.Name)
            .MaximumLength(MatchCoachLimits.MaxNameLength).WithMessage($"A coach's name cannot exceed {MatchCoachLimits.MaxNameLength} characters.");

        RuleFor(c => c.SteamId)
            .Must(DemoSummaryDtoValidator.IsSteamId64)
            .When(c => !string.IsNullOrEmpty(c.SteamId))
            .WithMessage("A coach's Steam ID must be a SteamID64.");
    }
}
