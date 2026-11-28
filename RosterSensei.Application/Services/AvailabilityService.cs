using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;
using RosterSensei.Domain.Entities;
using RosterSensei.Domain.Enums;

namespace RosterSensei.Application.Services;

public class AvailabilityService : IAvailabilityService
{
    // Enough for a month view with its leading and trailing weeks, and no more.
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(62);

    // Overrides of past dates are kept this long, then pruned on the next save.
    private const int KeepPastOverridesDays = 7;

    // How far ahead a date can be overridden.
    private const int MaxOverrideDaysAhead = 366;

    private readonly IAvailabilityRepository _availabilityRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly TimeProvider _time;

    public AvailabilityService(IAvailabilityRepository availabilityRepository, ITeamRepository teamRepository, TimeProvider time)
    {
        _availabilityRepository = availabilityRepository;
        _teamRepository = teamRepository;
        _time = time;
    }

    private DateOnly UtcToday => DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

    public async Task<Result<MyAvailabilityDto>> GetMineAsync(string userId, CancellationToken cancellationToken = default)
    {
        var availability = await _availabilityRepository.GetAsync(userId, UtcToday.AddDays(-KeepPastOverridesDays), cancellationToken);
        return Result<MyAvailabilityDto>.SuccessResult(ToDto(availability));
    }

    public async Task<Result<MyAvailabilityDto>> SaveWeeklyAsync(string userId, SaveWeeklyAvailabilityDto dto)
    {
        await _availabilityRepository.SaveWeeklyAsync(userId, dto.TimeZone, string.Concat(dto.Weekly));
        return await GetMineAsync(userId);
    }

    public async Task<Result<AvailabilityOverrideDto>> SaveOverrideAsync(string userId, DateOnly date, SaveAvailabilityOverrideDto dto)
    {
        // Allows a day either side of the UTC date, for every time zone's idea of "today".
        if (date < UtcToday.AddDays(-1))
            return Result<AvailabilityOverrideDto>.Failure("Past days can't be changed.");
        if (date > UtcToday.AddDays(MaxOverrideDaysAhead))
            return Result<AvailabilityOverrideDto>.Failure("That day is too far ahead.");

        await _availabilityRepository.SaveOverrideAsync(userId, dto.TimeZone, date, dto.Slots, UtcToday.AddDays(-KeepPastOverridesDays));
        return Result<AvailabilityOverrideDto>.SuccessResult(new AvailabilityOverrideDto { Date = date, Slots = dto.Slots });
    }

    public async Task<Result<string>> DeleteOverrideAsync(string userId, DateOnly date)
    {
        if (!await _availabilityRepository.DeleteOverrideAsync(userId, date))
            return Result<string>.NotFound("That day already follows your usual week.");

        return Result<string>.SuccessResult("The day follows your usual week again.");
    }

    public async Task<Result<TeamAvailabilityDto>> GetTeamAsync(string userId, int teamId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
            return Result<TeamAvailabilityDto>.Failure("The end of the range must be after its start.");
        if (to - from > MaxRange)
            return Result<TeamAvailabilityDto>.Failure("The date range is too long.");

        if (await _teamRepository.GetTeamMemberAsync(teamId, userId, cancellationToken) == null)
            return Result<TeamAvailabilityDto>.Forbidden("You don't have access to this team's availability.");

        var members = await _teamRepository.GetTeamMembersWithUserDetailsAsync(teamId, cancellationToken);
        var userIds = members.Select(m => m.Member.UserId).ToList();

        // Overrides are stored by local date, so a day either side covers every offset.
        var availabilities = await _availabilityRepository.GetForUsersAsync(
            userIds,
            DateOnly.FromDateTime(from).AddDays(-2),
            DateOnly.FromDateTime(to).AddDays(2),
            cancellationToken);

        var freeBySlot = new SortedDictionary<DateTime, List<string>>();
        foreach (var availability in availabilities)
        {
            foreach (var slot in AvailabilityExpander.FreeSlots(availability, AvailabilityExpander.ZoneOf(availability), from, to))
            {
                if (!freeBySlot.TryGetValue(slot, out var free))
                    freeBySlot[slot] = free = new List<string>();
                free.Add(availability.UserId);
            }
        }

        var filledIn = availabilities.Where(HasAnyFreeTime).Select(a => a.UserId).ToHashSet();

        return Result<TeamAvailabilityDto>.SuccessResult(new TeamAvailabilityDto
        {
            SlotMinutes = AvailabilitySlots.SlotMinutes,
            PlayersNeeded = AvailabilitySlots.PlayersNeeded,
            Members = members
                .Select(m => new TeamAvailabilityMemberDto
                {
                    UserId = m.Member.UserId,
                    Username = m.Username,
                    Role = m.Member.Role.ToString(),
                    CountsTowardsLineup = m.Member.Role.CanPlay(),
                    HasFilledIn = filledIn.Contains(m.Member.UserId)
                })
                .OrderByDescending(m => m.CountsTowardsLineup)
                .ThenBy(m => m.Username, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Slots = freeBySlot
                .Select(s => new TeamAvailabilitySlotDto { Start = s.Key, UserIds = s.Value })
                .ToList()
        });
    }

    // Someone who saved only empty days hasn't really filled anything in yet.
    private static bool HasAnyFreeTime(UserAvailability availability) =>
        availability.WeeklySlots.Contains(AvailabilitySlots.Free) || availability.Overrides.Any(o => o.Slots.Contains(AvailabilitySlots.Free));

    private static MyAvailabilityDto ToDto(UserAvailability? availability)
    {
        var weekly = availability?.WeeklySlots ?? AvailabilitySlots.EmptyWeek;
        return new MyAvailabilityDto
        {
            TimeZone = availability?.TimeZone,
            Weekly = Enumerable.Range(0, 7).Select(day => weekly.Substring(day * AvailabilitySlots.PerDay, AvailabilitySlots.PerDay)).ToList(),
            Overrides = availability?.Overrides
                .OrderBy(o => o.Date)
                .Select(o => new AvailabilityOverrideDto { Date = o.Date, Slots = o.Slots })
                .ToList() ?? new()
        };
    }
}
