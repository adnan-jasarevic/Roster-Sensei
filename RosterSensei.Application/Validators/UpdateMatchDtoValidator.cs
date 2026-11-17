using RosterSensei.Application.DTOs;
using FluentValidation;

namespace RosterSensei.Application.Validators;

// An edit follows the same rules as adding a match.
public class UpdateMatchDtoValidator : AbstractValidator<UpdateMatchDto>
{
    public UpdateMatchDtoValidator()
    {
        Include(new CreateMatchDtoValidator());

        RuleFor(m => m.Version)
            .GreaterThanOrEqualTo(0).WithMessage("The match version is required when editing.");
    }
}
