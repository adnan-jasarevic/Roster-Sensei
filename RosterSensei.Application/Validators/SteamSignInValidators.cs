using RosterSensei.Application.DTOs;
using FluentValidation;

namespace RosterSensei.Application.Validators;

public class StartSteamSignInDtoValidator : AbstractValidator<StartSteamSignInDto>
{
    public StartSteamSignInDtoValidator()
    {
        RuleFor(d => d.ReturnUrl)
            .NotEmpty().WithMessage("The return URL is required.")
            .MaximumLength(500).WithMessage("The return URL cannot exceed 500 characters.");
    }
}

public class CompleteSteamSignInDtoValidator : AbstractValidator<CompleteSteamSignInDto>
{
    // A Steam reply has 11 openid.* fields; this leaves room without letting a request carry megabytes.
    public const int MaxParameters = 30;
    public const int MaxValueLength = 2048;

    public CompleteSteamSignInDtoValidator()
    {
        RuleFor(d => d.Parameters)
            .NotEmpty().WithMessage("The Steam sign-in reply is missing.")
            .Must(p => p.Count <= MaxParameters).WithMessage("The Steam sign-in reply has too many fields.")
            .Must(p => p.All(kv => kv.Key.Length <= 100 && kv.Value.Length <= MaxValueLength))
            .WithMessage("The Steam sign-in reply has a field that is too long.");
    }
}
