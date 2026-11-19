using RosterSensei.Application.DTOs;
using RosterSensei.Domain.Entities;
using FluentValidation;

namespace RosterSensei.Application.Validators;

public class SetMatchLineupDtoValidator : AbstractValidator<SetMatchLineupDto>
{
    // Five starters plus substitutes and stand-ins. More than five can have played, since a
    // player can be swapped between maps of a series.
    public const int MaxPlayers = 10;
    public const int MaxRoleLength = 30;

    public SetMatchLineupDtoValidator()
    {
        RuleFor(d => d.Players)
            .NotNull().WithMessage("The lineup is required.");

        RuleFor(d => d.Players)
            .SetValidator(new LineupValidator())
            .When(d => d.Players != null);
    }
}

// The rules for a list of players, shared by the lineup endpoint and a match added with its lineup.
public class LineupValidator : AbstractValidator<List<LineupPlayerDto>>
{
    public LineupValidator()
    {
        RuleFor(p => p)
            .Cascade(CascadeMode.Stop)
            .Must(p => p.Count <= SetMatchLineupDtoValidator.MaxPlayers).WithMessage($"A lineup can have at most {SetMatchLineupDtoValidator.MaxPlayers} players.")
            .Must(p => p.Select(x => x.UserId).Distinct().Count() == p.Count).WithMessage("A player can only be in the lineup once.")
            .OverridePropertyName("Players");

        RuleForEach(p => p).SetValidator(new LineupPlayerDtoValidator()).OverridePropertyName("Players");
    }
}

public class LineupPlayerDtoValidator : AbstractValidator<LineupPlayerDto>
{
    public LineupPlayerDtoValidator()
    {
        RuleFor(p => p.UserId)
            .NotEmpty().WithMessage("Each player needs a user ID.")
            .MaximumLength(450).WithMessage("The user ID is too long.");

        RuleFor(p => p.Role)
            .MaximumLength(SetMatchLineupDtoValidator.MaxRoleLength).WithMessage($"A role cannot exceed {SetMatchLineupDtoValidator.MaxRoleLength} characters.");

        RuleFor(p => p.Rating)
            .InclusiveBetween(PlayerRating.Min, PlayerRating.Max)
            .WithMessage($"A rating must be between {PlayerRating.Min} and {PlayerRating.Max}.");

        RuleFor(p => p.Rating)
            .Must(r => decimal.Round(r!.Value, 2) == r.Value)
            .When(p => p.Rating.HasValue)
            .WithMessage("A rating can have at most two decimals.");

        RuleFor(p => p.Rating)
            .Null()
            .When(p => !p.Played)
            .WithMessage("Only a player who played can be rated.");

        RuleFor(p => p.Kills)
            .InclusiveBetween(0, PlayerMatchStats.MaxCount)
            .WithMessage($"Kills must be between 0 and {PlayerMatchStats.MaxCount}.");

        RuleFor(p => p.Deaths)
            .InclusiveBetween(0, PlayerMatchStats.MaxCount)
            .WithMessage($"Deaths must be between 0 and {PlayerMatchStats.MaxCount}.");

        RuleFor(p => p.Assists)
            .InclusiveBetween(0, PlayerMatchStats.MaxCount)
            .WithMessage($"Assists must be between 0 and {PlayerMatchStats.MaxCount}.");

        RuleFor(p => p.Adr)
            .InclusiveBetween(0m, PlayerMatchStats.MaxAdr)
            .WithMessage($"ADR must be between 0 and {PlayerMatchStats.MaxAdr}.");

        RuleFor(p => p.Adr)
            .Must(v => decimal.Round(v!.Value, 1) == v.Value)
            .When(p => p.Adr.HasValue)
            .WithMessage("ADR can have at most one decimal.");

        RuleFor(p => p.HeadshotPercentage)
            .InclusiveBetween(0m, PlayerMatchStats.MaxHeadshotPercentage)
            .WithMessage("HS% must be between 0 and 100.");

        RuleFor(p => p.HeadshotPercentage)
            .Must(v => decimal.Round(v!.Value, 1) == v.Value)
            .When(p => p.HeadshotPercentage.HasValue)
            .WithMessage("HS% can have at most one decimal.");

        RuleFor(p => p)
            .Must(p => p.Kills == null && p.Deaths == null && p.Assists == null && p.Adr == null && p.HeadshotPercentage == null)
            .When(p => !p.Played)
            .OverridePropertyName("Stats")
            .WithMessage("Only a player who played can have stats.");
    }
}
