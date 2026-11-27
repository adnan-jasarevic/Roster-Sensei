using RosterSensei.Application.Interfaces;
using RosterSensei.Domain.Entities;
using RosterSensei.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace RosterSensei.Infrastructure.Repositories;

public class DemoImportRepository : IDemoImportRepository
{
    private readonly AppDbContext _context;

    public DemoImportRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<(int MatchId, int MapOrder, string OpponentName)?> FindImportAsync(int teamId, string fingerprint)
    {
        var found = await _context.MatchDemoImports
            .AsNoTracking()
            .Where(i => i.TeamId == teamId && i.Fingerprint == fingerprint)
            .Select(i => new { i.MatchId, i.MapOrder, i.Match.OpponentName })
            .FirstOrDefaultAsync();
        return found == null ? null : (found.MatchId, found.MapOrder, found.OpponentName);
    }

    public Task<int?> NewestImportedPatchAsync() =>
        _context.MatchDemoImports.MaxAsync(i => i.PatchVersion);

    public Task<int?> PausedForPatchAsync(string parserVersion) =>
        _context.DemoParserStatuses
            .Where(s => s.ParserVersion == parserVersion)
            .Select(s => (int?)s.PausedForPatch)
            .FirstOrDefaultAsync();

    public async Task PauseAsync(string parserVersion, int patchVersion, DateTime now)
    {
        if (await _context.DemoParserStatuses.AnyAsync(s => s.ParserVersion == parserVersion))
            return;

        _context.DemoParserStatuses.Add(new DemoParserStatus { ParserVersion = parserVersion, PausedForPatch = patchVersion, PausedAt = now });
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Someone else paused it at the same moment: the primary key kept one row, which is all we need.
            _context.ChangeTracker.Clear();
        }
    }
}
