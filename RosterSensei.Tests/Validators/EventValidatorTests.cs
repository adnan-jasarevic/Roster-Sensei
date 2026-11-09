using RosterSensei.Application.DTOs;
using RosterSensei.Application.Validators;

namespace RosterSensei.Tests.Validators;

// The rules for saving a calendar event. The title is optional: an event without one shows its type's name.
public class EventValidatorTests
{
    private static readonly DateTime Start = new(2026, 11, 11, 18, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Pracc vs Tokyo Ronin")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Any_title_up_to_100_characters_or_none_is_valid(string? title)
    {
        var result = new SaveTeamEventDtoValidator().Validate(Event(title!));
        Assert.True(result.IsValid, string.Join(" ", result.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));
    }

    [Fact]
    public void A_title_longer_than_100_characters_is_refused() =>
        Assert.Contains(new SaveTeamEventDtoValidator().Validate(Event(new string('a', 101))).Errors, e => e.PropertyName == nameof(SaveTeamEventDto.Title));

    private static SaveTeamEventDto Event(string title) =>
        new() { Title = title, StartTime = Start, EndTime = Start.AddHours(2), EventTypeId = 1 };
}
