using RosterSensei.Application.Interfaces;
using RosterSensei.Domain.Entities;
using RosterSensei.Domain.Enums;
using RosterSensei.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace RosterSensei.Infrastructure.Repositories;

public class MapRatingRepository : IMapRatingRepository
{
    private readonly AppDbContext _context;

    public MapRatingRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<MapRating>> GetForTeamAsync(int teamId, CancellationToken cancellationToken = default)
    {
        return await _context.MapRatings
            .AsNoTracking()
            .Where(r => r.TeamId == teamId)
            .ToListAsync(cancellationToken);
    }

    public async Task<WriteOutcome> SaveAsync(MapRating rating)
    {
        // Changing a rating that exists is one UPDATE, with nothing read first.
        if (await UpdateAsync(rating) == 1)
            return WriteOutcome.Done;

        _context.MapRatings.Add(rating);
        try
        {
            await _context.SaveChangesAsync();
            return WriteOutcome.Done;
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();

            // The foreign key to the membership: the member left or was removed in the meantime.
            if (!await _context.TeamMembers.AnyAsync(tm => tm.TeamId == rating.TeamId && tm.UserId == rating.UserId))
                return WriteOutcome.Rejected;

            // The unique index: a simultaneous first save of the same rating (another tab, a double submit)
            // inserted it first. The last save wins, as it would have one after the other.
            return await UpdateAsync(rating) == 1 ? WriteOutcome.Done : WriteOutcome.Conflict;
        }
    }

    public async Task<bool> DeleteAsync(int teamId, string userId, string mapName, MapSide side)
    {
        var deleted = await _context.MapRatings
            .Where(r => r.TeamId == teamId && r.UserId == userId && r.MapName == mapName && r.Side == side)
            .ExecuteDeleteAsync();

        return deleted == 1;
    }

    private Task<int> UpdateAsync(MapRating rating) =>
        _context.MapRatings
            .Where(r => r.TeamId == rating.TeamId && r.UserId == rating.UserId && r.MapName == rating.MapName && r.Side == rating.Side)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Rating, rating.Rating)
                .SetProperty(r => r.UpdatedAt, rating.UpdatedAt));
}
