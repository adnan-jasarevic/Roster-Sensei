using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Domain.Entities;
using FluentValidation;

namespace RosterSensei.Application.Validators;

public class DemoSummaryDtoValidator : AbstractValidator<DemoSummaryDto>
{
    // Five starters plus anyone who swapped in during the map.
    public const int MaxPlayersPerSide = 10;
    public const int MaxRounds = 200;
    public const int MaxDamage = 100_000;
    public const int MaxParserVersionLength = 50;
    public const int MaxNameLength = 64;

    public static bool IsSteamId64(string? value) =>
        value != null && SteamId.TryParse(value, out var parsed) && parsed == value;

    public static bool IsFingerprint(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public DemoSummaryDtoValidator()
    {
        RuleFor(d => d.ParserVersion)
            .NotEmpty().WithMessage("The parser version is required.")
            .MaximumLength(MaxParserVersionLength).WithMessage("The parser version is too long.");

        RuleFor(d => d.PatchVersion)
            .GreaterThanOrEqualTo(0).WithMessage("The patch version cannot be negative.");

        RuleFor(d => d.Fingerprint)
            .Must(IsFingerprint).WithMessage("The demo fingerprint must be a SHA-256 in lowercase hex.");

        RuleFor(d => d.MapName)
            .NotEmpty().WithMessage("The demo has no map name.")
            .MaximumLength(50).WithMessage("Map names cannot exceed 50 characters.");

        RuleFor(d => d.Rounds)
            .InclusiveBetween(1, MaxRounds).WithMessage($"A demo must have between 1 and {MaxRounds} rounds.");

        RuleFor(d => d.Sides)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("A demo has two sides.")
            .Must(s => s.Count == 2).WithMessage("A demo has two sides.");

        RuleForEach(d => d.Sides)
            .SetValidator(new DemoSideDtoValidator())
            .When(d => d.Sides is { Count: 2 });

        RuleFor(d => d)
            .Must(d => d.Sides[0].Score + d.Sides[1].Score == d.Rounds)
            .When(d => d.Sides is { Count: 2 })
            .OverridePropertyName("Rounds")
            .WithMessage("The two scores don't add up to the rounds played.");

        RuleFor(d => d)
            .Must(d => d.Sides[0].FirstHalf + d.Sides[1].FirstHalf <= MatchMapDtoValidator.RoundsPerHalf
                    && d.Sides[0].SecondHalf + d.Sides[1].SecondHalf <= MatchMapDtoValidator.RoundsPerHalf)
            .When(d => d.Sides is { Count: 2 })
            .OverridePropertyName("Halves")
            .WithMessage($"A half has at most {MatchMapDtoValidator.RoundsPerHalf} rounds.");

        RuleFor(d => d)
            .Must(d =>
            {
                var ids = d.Sides.SelectMany(s => s.Players.Select(p => p.SteamId).Concat(s.Coaches.Select(c => c.SteamId))).ToList();
                return ids.Distinct().Count() == ids.Count;
            })
            .When(d => d.Sides is { Count: 2 } && d.Sides.All(s => s.Players != null && s.Coaches != null))
            .OverridePropertyName("Sides")
            .WithMessage("A Steam account can only appear once in a demo.");
    }
}

public class DemoSideDtoValidator : AbstractValidator<DemoSideDto>
{
    public DemoSideDtoValidator()
    {
        RuleFor(s => s.Score).InclusiveBetween(0, DemoSummaryDtoValidator.MaxRounds).WithMessage("Scores must be between 0 and 200.");
        RuleFor(s => s.FirstHalf).InclusiveBetween(0, MatchMapDtoValidator.RoundsPerHalf).WithMessage("Half scores must be between 0 and 12.");
        RuleFor(s => s.SecondHalf).InclusiveBetween(0, MatchMapDtoValidator.RoundsPerHalf).WithMessage("Half scores must be between 0 and 12.");
        RuleFor(s => s)
            .Must(s => s.FirstHalf + s.SecondHalf <= s.Score)
            .OverridePropertyName("Score")
            .WithMessage("The half scores are more than the final score.");

        RuleFor(s => s.Players)
            .NotNull().WithMessage("Each side needs its players.")
            .Must(p => p == null || p.Count <= DemoSummaryDtoValidator.MaxPlayersPerSide)
            .WithMessage($"A side can have at most {DemoSummaryDtoValidator.MaxPlayersPerSide} players.");
        RuleForEach(s => s.Players).SetValidator(new DemoPlayerDtoValidator()).When(s => s.Players != null);

        RuleFor(s => s.Coaches)
            .NotNull().WithMessage("Each side needs its list of coaches, even if empty.")
            .Must(c => c == null || c.Count <= MatchCoachLimits.MaxCoaches)
            .WithMessage($"A side can have at most {MatchCoachLimits.MaxCoaches} coaches.");
        RuleForEach(s => s.Coaches).SetValidator(new DemoCoachDtoValidator()).When(s => s.Coaches != null);
    }
}

public class DemoPlayerDtoValidator : AbstractValidator<DemoPlayerDto>
{
    public DemoPlayerDtoValidator()
    {
        RuleFor(p => p.SteamId).Must(DemoSummaryDtoValidator.IsSteamId64).WithMessage("Each player needs a SteamID64.");
        RuleFor(p => p.Name).MaximumLength(DemoSummaryDtoValidator.MaxNameLength).WithMessage("A player name is too long.");
        RuleFor(p => p.Kills).InclusiveBetween(0, PlayerMatchStats.MaxCount).WithMessage($"Kills must be between 0 and {PlayerMatchStats.MaxCount}.");
        RuleFor(p => p.Deaths).InclusiveBetween(0, PlayerMatchStats.MaxCount).WithMessage($"Deaths must be between 0 and {PlayerMatchStats.MaxCount}.");
        RuleFor(p => p.Assists).InclusiveBetween(0, PlayerMatchStats.MaxCount).WithMessage($"Assists must be between 0 and {PlayerMatchStats.MaxCount}.");
        RuleFor(p => p.Damage).InclusiveBetween(0, DemoSummaryDtoValidator.MaxDamage).WithMessage("Damage is out of range.");
        RuleFor(p => p.HeadshotKills)
            .GreaterThanOrEqualTo(0).WithMessage("Headshot kills cannot be negative.")
            .LessThanOrEqualTo(p => p.Kills).WithMessage("Headshot kills cannot be more than kills.");
    }
}

public class DemoCoachDtoValidator : AbstractValidator<DemoCoachDto>
{
    public DemoCoachDtoValidator()
    {
        RuleFor(c => c.SteamId).Must(DemoSummaryDtoValidator.IsSteamId64).WithMessage("Each coach needs a SteamID64.");
        RuleFor(c => c.Name).MaximumLength(DemoSummaryDtoValidator.MaxNameLength).WithMessage("A coach's name is too long.");
    }
}

public class PreviewDemoImportDtoValidator : AbstractValidator<PreviewDemoImportDto>
{
    public PreviewDemoImportDtoValidator()
    {
        RuleFor(d => d.Demo).NotNull().WithMessage("The demo is required.").SetValidator(new DemoSummaryDtoValidator());
    }
}

public class ApplyDemoImportDtoValidator : AbstractValidator<ApplyDemoImportDto>
{
    public ApplyDemoImportDtoValidator()
    {
        RuleFor(d => d.Demo).NotNull().WithMessage("The demo is required.").SetValidator(new DemoSummaryDtoValidator());

        RuleFor(d => d.OurSide).InclusiveBetween(0, 1).WithMessage("Our side must be 0 or 1.");

        RuleFor(d => d.MapOrder).InclusiveBetween(1, 5).WithMessage("The map must be between 1 and 5.");

        RuleFor(d => d.Players)
            .Must(p => p!.Select(x => x.SteamId).Distinct().Count() == p!.Count)
            .When(d => d.Players != null)
            .WithMessage("A player can only be linked once.");

        RuleFor(d => d.Players)
            .Must(p => p!.Where(x => !string.IsNullOrEmpty(x.UserId)).Select(x => x.UserId).Distinct().Count()
                    == p!.Count(x => !string.IsNullOrEmpty(x.UserId)))
            .When(d => d.Players != null)
            .WithMessage("A team member can only be linked to one player.");

        RuleForEach(d => d.Players).ChildRules(link =>
        {
            link.RuleFor(l => l.UserId).MaximumLength(450).WithMessage("The user ID is too long.");
        });

        RuleFor(d => d.Coaches!)
            .SetValidator(new MatchCoachesValidator())
            .When(d => d.Coaches != null);
    }
}

public class DemoReadFailureDtoValidator : AbstractValidator<DemoReadFailureDto>
{
    public DemoReadFailureDtoValidator()
    {
        RuleFor(d => d.ParserVersion)
            .NotEmpty().WithMessage("The parser version is required.")
            .MaximumLength(DemoSummaryDtoValidator.MaxParserVersionLength).WithMessage("The parser version is too long.");
        RuleFor(d => d.PatchVersion).GreaterThanOrEqualTo(0).WithMessage("The patch version cannot be negative.");
    }
}
