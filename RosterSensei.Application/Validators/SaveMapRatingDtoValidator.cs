using RosterSensei.Application.DTOs;
using RosterSensei.Domain.Entities;
using FluentValidation;

namespace RosterSensei.Application.Validators;

public class SaveMapRatingDtoValidator : AbstractValidator<SaveMapRatingDto>
{
    public SaveMapRatingDtoValidator()
    {
        RuleFor(d => d.Rating)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("A rating is required.")
            .InclusiveBetween(MapPool.MinRating, MapPool.MaxRating).WithMessage("A rating must be from 1 to 10.")
            .Must(r => r == Math.Round(r!.Value, 1)).WithMessage("A rating can have at most one decimal, like 6.5.");
    }
}
