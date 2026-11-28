using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Application.Validators;
using RosterSensei.Domain.Entities;
using RosterSensei.Domain.Enums;
using RosterSensei.Tests.Infrastructure;

namespace RosterSensei.Tests;

public class AvailabilityTests : IDisposable
{
    private const string Sarajevo = "Europe/Sarajevo";
    private const string NewYork = "America/New_York";

    private readonly TestDatabase _db = new();
    private readonly string _owner;
    private readonly string _player;
    private readonly string _coach;
    private readonly string _outsider;
    private readonly int _teamId;

    // FakeTime's "now" is Thursday 19 November 2026. The next week runs Monday 23 to Sunday 29.
    private static readonly DateTime NextMonday = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);

    public AvailabilityTests()
    {
        _owner = _db.AddUser("owner");
        _player = _db.AddUser("player");
        _coach = _db.AddUser("coach");
        _outsider = _db.AddUser("outsider");
        _teamId = _db.AddTeam(_owner, (_player, TeamRole.Player), (_coach, TeamRole.Coach));
    }

    public void Dispose() => _db.Dispose();

    // A repeating week that is free on the given weekdays (Monday = 0) from start to end, local time.
    private static SaveWeeklyAvailabilityDto Week(string timeZone, string start, string end, params int[] days) => new()
    {
        TimeZone = timeZone,
        Weekly = Enumerable.Range(0, 7).Select(d => days.Contains(d) ? Day(start, end) : AvailabilitySlots.EmptyDay).ToList()
    };

    // One day, free from start to end ("HH:mm", on the half hour).
    private static string Day(string start, string end)
    {
        var from = (int)TimeSpan.Parse(start).TotalMinutes / AvailabilitySlots.SlotMinutes;
        var to = (int)TimeSpan.Parse(end).TotalMinutes / AvailabilitySlots.SlotMinutes;
        return string.Concat(Enumerable.Range(0, AvailabilitySlots.PerDay).Select(i => i >= from && i < to ? '1' : '0'));
    }

    private async Task<TeamAvailabilityDto> Team(DateTime from, int days = 7)
    {
        var result = await _db.AvailabilityService().GetTeamAsync(_owner, _teamId, from, from.AddDays(days));
        Assert.True(result.Success, result.ErrorMessage);
        return result.Data!;
    }

    private static List<DateTime> FreeTimes(TeamAvailabilityDto team, string userId) =>
        team.Slots.Where(s => s.UserIds.Contains(userId)).Select(s => s.Start).ToList();

    [Fact]
    public async Task Someone_who_never_saved_gets_an_empty_week()
    {
        var mine = await _db.AvailabilityService().GetMineAsync(_player);

        Assert.Null(mine.Data!.TimeZone);
        Assert.Equal(7, mine.Data.Weekly.Count);
        Assert.All(mine.Data.Weekly, day => Assert.Equal(AvailabilitySlots.EmptyDay, day));
        Assert.Empty(mine.Data.Overrides);
    }

    [Fact]
    public async Task A_saved_week_comes_back_as_saved_and_can_be_replaced()
    {
        await _db.AvailabilityService().SaveWeeklyAsync(_player, Week(Sarajevo, "19:00", "23:00", 1, 3));
        var saved = await _db.AvailabilityService().SaveWeeklyAsync(_player, Week(NewYork, "18:00", "20:00", 0));

        Assert.Equal(NewYork, saved.Data!.TimeZone);
        Assert.Equal(Day("18:00", "20:00"), saved.Data.Weekly[0]);
        Assert.Equal(AvailabilitySlots.EmptyDay, saved.Data.Weekly[1]);
    }

    [Fact]
    public async Task The_repeating_week_is_read_in_the_players_own_time_zone()
    {
        // Tuesday 19:00-21:00 in Sarajevo is 18:00-20:00 UTC in winter (UTC+1).
        await _db.AvailabilityService().SaveWeeklyAsync(_player, Week(Sarajevo, "19:00", "21:00", 1));

        var free = FreeTimes(await Team(NextMonday), _player);

        var tuesday = NextMonday.AddDays(1);
        Assert.Equal([tuesday.AddHours(18), tuesday.AddHours(18.5), tuesday.AddHours(19), tuesday.AddHours(19.5)], free);
    }

    [Fact]
    public async Task Local_times_stay_put_when_the_clocks_change()
    {
        await _db.AvailabilityService().SaveWeeklyAsync(_player, Week(Sarajevo, "19:00", "19:30", 0));

        // Monday 29 March 2027 is summer time (UTC+2), so 19:00 local is 17:00 UTC.
        var summerMonday = new DateTime(2027, 3, 29, 0, 0, 0, DateTimeKind.Utc);
        var free = FreeTimes(await Team(summerMonday, 1), _player);

        Assert.Equal([summerMonday.AddHours(17)], free);
    }

    [Fact]
    public async Task The_local_hour_skipped_when_clocks_go_forward_is_free_for_nobody()
    {
        // 28 March 2027 is a Sunday: Sarajevo goes from 02:00 straight to 03:00.
        await _db.AvailabilityService().SaveWeeklyAsync(_player, Week(Sarajevo, "01:00", "04:00", 6));

        var sunday = new DateTime(2027, 3, 28, 0, 0, 0, DateTimeKind.Utc);
        var free = FreeTimes(await Team(sunday.AddDays(-1), 2), _player);

        // 01:00-02:00 at UTC+1, then 03:00-04:00 at UTC+2: two hours, not three.
        Assert.Equal([sunday, sunday.AddHours(0.5), sunday.AddHours(1), sunday.AddHours(1.5)], free);
    }

    [Fact]
    public async Task An_override_replaces_that_one_day_only()
    {
        await _db.AvailabilityService().SaveWeeklyAsync(_player, Week(Sarajevo, "19:00", "20:00", 0, 1, 2, 3, 4, 5, 6));
        var wednesday = DateOnly.FromDateTime(NextMonday.AddDays(2));
        var saved = await _db.AvailabilityService().SaveOverrideAsync(_player, wednesday, new SaveAvailabilityOverrideDto { TimeZone = Sarajevo, Slots = AvailabilitySlots.EmptyDay });

        Assert.True(saved.Success, saved.ErrorMessage);
        var free = FreeTimes(await Team(NextMonday), _player);
        // Six evenings of two half hours: Wednesday is off.
        Assert.Equal(12, free.Count);
        Assert.DoesNotContain(free, t => DateOnly.FromDateTime(t) == wednesday);
    }

    [Fact]
    public async Task Saving_an_override_twice_keeps_one_and_resetting_brings_the_usual_week_back()
    {
        await _db.AvailabilityService().SaveWeeklyAsync(_player, Week(Sarajevo, "19:00", "20:00", 2));
        var wednesday = DateOnly.FromDateTime(NextMonday.AddDays(2));
        await _db.AvailabilityService().SaveOverrideAsync(_player, wednesday, new SaveAvailabilityOverrideDto { TimeZone = Sarajevo, Slots = Day("10:00", "11:00") });
        await _db.AvailabilityService().SaveOverrideAsync(_player, wednesday, new SaveAvailabilityOverrideDto { TimeZone = Sarajevo, Slots = Day("12:00", "13:00") });

        var mine = await _db.AvailabilityService().GetMineAsync(_player);
        var only = Assert.Single(mine.Data!.Overrides);
        Assert.Equal(Day("12:00", "13:00"), only.Slots);

        var reset = await _db.AvailabilityService().DeleteOverrideAsync(_player, wednesday);
        Assert.True(reset.Success);
        Assert.Equal(2, FreeTimes(await Team(NextMonday), _player).Count);

        var again = await _db.AvailabilityService().DeleteOverrideAsync(_player, wednesday);
        Assert.Equal(ErrorType.NotFound, again.ErrorType);
    }

    [Fact]
    public async Task A_first_save_can_be_a_single_day()
    {
        var friday = DateOnly.FromDateTime(NextMonday.AddDays(4));
        var saved = await _db.AvailabilityService().SaveOverrideAsync(_player, friday, new SaveAvailabilityOverrideDto { TimeZone = Sarajevo, Slots = Day("20:00", "21:00") });

        Assert.True(saved.Success, saved.ErrorMessage);
        var mine = await _db.AvailabilityService().GetMineAsync(_player);
        Assert.Equal(Sarajevo, mine.Data!.TimeZone);
        Assert.All(mine.Data.Weekly, day => Assert.Equal(AvailabilitySlots.EmptyDay, day));
        Assert.Equal(2, FreeTimes(await Team(NextMonday), _player).Count);
    }

    [Fact]
    public async Task Past_days_and_days_too_far_ahead_cannot_be_overridden()
    {
        var service = _db.AvailabilityService();
        var dto = new SaveAvailabilityOverrideDto { TimeZone = Sarajevo, Slots = AvailabilitySlots.EmptyDay };

        var past = await service.SaveOverrideAsync(_player, new DateOnly(2026, 11, 10), dto);
        var far = await service.SaveOverrideAsync(_player, new DateOnly(2028, 1, 1), dto);

        Assert.False(past.Success);
        Assert.False(far.Success);
    }

    [Fact]
    public async Task Old_overrides_are_pruned_on_the_next_save()
    {
        var service = _db.AvailabilityService();
        var dto = new SaveAvailabilityOverrideDto { TimeZone = Sarajevo, Slots = Day("19:00", "20:00") };
        await service.SaveOverrideAsync(_player, new DateOnly(2026, 11, 19), dto);

        _db.Time.Advance(TimeSpan.FromDays(30));
        await _db.AvailabilityService().SaveOverrideAsync(_player, new DateOnly(2026, 12, 20), dto);

        using var context = _db.CreateContext();
        Assert.Equal([new DateOnly(2026, 12, 20)], context.AvailabilityOverrides.Select(o => o.Date).ToList());
    }

    [Fact]
    public async Task Players_in_different_time_zones_line_up_on_the_same_moment()
    {
        // 19:00 in Sarajevo and 13:00 in New York are both 18:00 UTC in late November.
        await _db.AvailabilityService().SaveWeeklyAsync(_owner, Week(Sarajevo, "19:00", "19:30", 0));
        await _db.AvailabilityService().SaveWeeklyAsync(_player, Week(NewYork, "13:00", "13:30", 0));

        var slot = Assert.Single((await Team(NextMonday)).Slots);

        Assert.Equal(NextMonday.AddHours(18), slot.Start);
        Assert.Equal([_owner, _player], slot.UserIds.Order());
    }

    [Fact]
    public async Task Coaches_are_listed_but_never_count_towards_a_lineup()
    {
        await _db.AvailabilityService().SaveWeeklyAsync(_coach, Week(Sarajevo, "19:00", "20:00", 0));

        var team = await Team(NextMonday);

        Assert.Equal(AvailabilitySlots.PlayersNeeded, team.PlayersNeeded);
        var coach = team.Members.Single(m => m.UserId == _coach);
        Assert.False(coach.CountsTowardsLineup);
        Assert.True(coach.HasFilledIn);
        Assert.Equal("Coach", coach.Role);
        // Players first, the coach last.
        Assert.Equal(_coach, team.Members[^1].UserId);
        Assert.All(team.Members.Where(m => m.UserId != _coach), m => Assert.True(m.CountsTowardsLineup));
        // Their free time is still there to see.
        Assert.Equal(2, FreeTimes(team, _coach).Count);
    }

    [Fact]
    public async Task Someone_who_saved_only_empty_days_has_not_filled_anything_in()
    {
        await _db.AvailabilityService().SaveWeeklyAsync(_player, Week(Sarajevo, "19:00", "20:00"));

        var team = await Team(NextMonday);

        Assert.False(team.Members.Single(m => m.UserId == _player).HasFilledIn);
    }

    [Fact]
    public async Task Only_members_can_see_a_teams_availability()
    {
        var result = await _db.AvailabilityService().GetTeamAsync(_outsider, _teamId, NextMonday, NextMonday.AddDays(7));

        Assert.Equal(ErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task Ranges_must_run_forwards_and_stay_short()
    {
        var backwards = await _db.AvailabilityService().GetTeamAsync(_owner, _teamId, NextMonday, NextMonday);
        var tooLong = await _db.AvailabilityService().GetTeamAsync(_owner, _teamId, NextMonday, NextMonday.AddDays(90));

        Assert.False(backwards.Success);
        Assert.False(tooLong.Success);
    }

    [Theory]
    [InlineData("", 7, 48)]
    [InlineData("Mars/Olympus_Mons", 7, 48)]
    [InlineData(Sarajevo, 6, 48)]
    [InlineData(Sarajevo, 7, 47)]
    public void A_week_needs_a_real_time_zone_and_seven_full_days(string timeZone, int days, int slots)
    {
        var dto = new SaveWeeklyAvailabilityDto { TimeZone = timeZone, Weekly = Enumerable.Repeat(new string('0', slots), days).ToList() };

        Assert.False(new SaveWeeklyAvailabilityDtoValidator().Validate(dto).IsValid);
    }

    [Fact]
    public void A_day_may_only_hold_zeros_and_ones()
    {
        var validator = new SaveAvailabilityOverrideDtoValidator();

        Assert.True(validator.Validate(new SaveAvailabilityOverrideDto { TimeZone = Sarajevo, Slots = Day("19:00", "20:00") }).IsValid);
        Assert.False(validator.Validate(new SaveAvailabilityOverrideDto { TimeZone = Sarajevo, Slots = new string('2', 48) }).IsValid);
    }
}
