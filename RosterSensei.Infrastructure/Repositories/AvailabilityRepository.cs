using RosterSensei.Application.Interfaces;
using RosterSensei.Domain.Entities;
using RosterSensei.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace RosterSensei.Infrastructure.Repositories;

public class AvailabilityRepository : IAvailabilityRepository
{
    private readonly AppDbContext _context;

    public AvailabilityRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<UserAvailability?> GetAsync(string userId, DateOnly overridesFrom, CancellationToken cancellationToken = default)
    {
        return await _context.UserAvailabilities
            .AsNoTracking()
            .Include(a => a.Overrides.Where(o => o.Date >= overridesFrom).OrderBy(o => o.Date))
            .FirstOrDefaultAsync(a => a.UserId == userId, cancellationToken);
    }

    public async Task<List<UserAvailability>> GetForUsersAsync(IReadOnlyCollection<string> userIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        return await _context.UserAvailabilities
            .AsNoTracking()
            .Include(a => a.Overrides.Where(o => o.Date >= from && o.Date <= to))
            .Where(a => userIds.Contains(a.UserId))
            .ToListAsync(cancellationToken);
    }

    public async Task SaveWeeklyAsync(string userId, string timeZone, string weeklySlots)
    {
        // Changing a saved week is one UPDATE, with nothing read first.
        if (await UpdateWeeklyAsync(userId, timeZone, weeklySlots) == 1)
            return;

        _context.UserAvailabilities.Add(new UserAvailability { UserId = userId, TimeZone = timeZone, WeeklySlots = weeklySlots });
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // A simultaneous first save (another tab) inserted it first. The last save wins, as it would
            // have one after the other.
            _context.ChangeTracker.Clear();
            await UpdateWeeklyAsync(userId, timeZone, weeklySlots);
        }
    }

    public async Task SaveOverrideAsync(string userId, string timeZone, DateOnly date, string slots, DateOnly pruneBefore)
    {
        await EnsureAvailabilityAsync(userId, timeZone);

        await _context.AvailabilityOverrides
            .Where(o => o.UserId == userId && o.Date < pruneBefore)
            .ExecuteDeleteAsync();

        if (await UpdateOverrideAsync(userId, date, slots) == 1)
            return;

        _context.AvailabilityOverrides.Add(new AvailabilityOverride { UserId = userId, Date = date, Slots = slots });
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The unique (UserId, Date) index: a simultaneous save of the same date got there first.
            _context.ChangeTracker.Clear();
            await UpdateOverrideAsync(userId, date, slots);
        }
    }

    public async Task<bool> DeleteOverrideAsync(string userId, DateOnly date)
    {
        var deleted = await _context.AvailabilityOverrides
            .Where(o => o.UserId == userId && o.Date == date)
            .ExecuteDeleteAsync();

        return deleted == 1;
    }

    // A first save that is a single date still needs the row its overrides hang off.
    private async Task EnsureAvailabilityAsync(string userId, string timeZone)
    {
        if (await _context.UserAvailabilities.AnyAsync(a => a.UserId == userId))
            return;

        _context.UserAvailabilities.Add(new UserAvailability { UserId = userId, TimeZone = timeZone });
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Created by a simultaneous request: that's all this needed.
            _context.ChangeTracker.Clear();
        }
    }

    private Task<int> UpdateWeeklyAsync(string userId, string timeZone, string weeklySlots) =>
        _context.UserAvailabilities
            .Where(a => a.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.TimeZone, timeZone)
                .SetProperty(a => a.WeeklySlots, weeklySlots)
                .SetProperty(a => a.UpdatedAt, DateTime.UtcNow));

    private Task<int> UpdateOverrideAsync(string userId, DateOnly date, string slots) =>
        _context.AvailabilityOverrides
            .Where(o => o.UserId == userId && o.Date == date)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Slots, slots));
}
