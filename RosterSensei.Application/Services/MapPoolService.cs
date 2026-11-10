using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;
using RosterSensei.Domain.Entities;
using RosterSensei.Domain.Enums;

namespace RosterSensei.Application.Services;

public class MapPoolService : IMapPoolService
{
    private const string NotMemberMessage = "You don't have access to the map pool of this team.";

    private readonly IMapRatingRepository _ratingRepository;
    private readonly ITeamRepository _teamRepository;

    public MapPoolService(IMapRatingRepository ratingRepository, ITeamRepository teamRepository)
    {
        _ratingRepository = ratingRepository;
        _teamRepository = teamRepository;
    }

    public async Task<Result<MapPoolDto>> GetAsync(string userId, int teamId, CancellationToken cancellationToken = default)
    {
        if (!await _teamRepository.IsUserInTeamAsync(teamId, userId, cancellationToken))
            return Result<MapPoolDto>.Forbidden(NotMemberMessage);

        var members = await _teamRepository.GetTeamMembersWithUserDetailsAsync(teamId, cancellationToken);
        var ratings = await _ratingRepository.GetForTeamAsync(teamId, cancellationToken);

        // Ratings only exist for current members (they're deleted with the membership), but a member who
        // left between the two reads would otherwise show up as a rating with no column.
        var memberIds = members.Select(m => m.Member.UserId).ToHashSet();

        return Result<MapPoolDto>.SuccessResult(new MapPoolDto
        {
            Maps = MapPool.Maps.ToList(),
            // Oldest members first, so columns don't move around when someone joins.
            Players = members
                .OrderBy(m => m.Member.JoinedAt)
                .ThenBy(m => m.Username, StringComparer.OrdinalIgnoreCase)
                .Select(m => new MapPoolPlayerDto { UserId = m.Member.UserId, Username = m.Username })
                .ToList(),
            CurrentUserId = userId,
            Ratings = ratings
                .Where(r => memberIds.Contains(r.UserId))
                .Select(ToDto)
                .ToList()
        });
    }

    public async Task<Result<MapRatingDto>> SaveRatingAsync(string userId, int teamId, string side, string mapName, SaveMapRatingDto dto)
    {
        var target = Parse<MapRatingDto>(side, mapName, out var parsedSide, out var map);
        if (target != null)
            return target;

        if (!await _teamRepository.IsUserInTeamAsync(teamId, userId))
            return Result<MapRatingDto>.Forbidden(NotMemberMessage);

        var rating = new MapRating
        {
            TeamId = teamId,
            UserId = userId,
            MapName = map!,
            Side = parsedSide,
            Rating = dto.Rating!.Value,
            UpdatedAt = DateTime.UtcNow
        };

        return await _ratingRepository.SaveAsync(rating) switch
        {
            WriteOutcome.Done => Result<MapRatingDto>.SuccessResult(ToDto(rating)),
            // The member left (or was removed) after the check above.
            WriteOutcome.Rejected => Result<MapRatingDto>.Forbidden(NotMemberMessage),
            _ => Result<MapRatingDto>.Conflict("Your rating was changed at the same time from somewhere else. Refresh and try again.")
        };
    }

    public async Task<Result<string>> ClearRatingAsync(string userId, int teamId, string side, string mapName)
    {
        var target = Parse<string>(side, mapName, out var parsedSide, out var map);
        if (target != null)
            return target;

        if (!await _teamRepository.IsUserInTeamAsync(teamId, userId))
            return Result<string>.Forbidden(NotMemberMessage);

        // Clearing a rating that's already empty (twice in a row, or from two tabs at once) is not an error.
        await _ratingRepository.DeleteAsync(teamId, userId, map!, parsedSide);
        return Result<string>.SuccessResult("Rating cleared.");
    }

    // Null when both are valid; otherwise the failure to return.
    private static Result<T>? Parse<T>(string side, string mapName, out MapSide parsedSide, out string? map)
    {
        map = MapPool.Find(mapName);
        if (!TryParseSide(side, out parsedSide))
            return Result<T>.NotFound("The side must be T or CT.");
        if (map == null)
            return Result<T>.NotFound("That map isn't in the map pool.");
        return null;
    }

    private static bool TryParseSide(string? side, out MapSide parsed)
    {
        switch (side?.Trim().ToUpperInvariant())
        {
            case "T":
                parsed = MapSide.T;
                return true;
            case "CT":
                parsed = MapSide.CT;
                return true;
            default:
                parsed = default;
                return false;
        }
    }

    private static MapRatingDto ToDto(MapRating r) => new()
    {
        UserId = r.UserId,
        MapName = r.MapName,
        Side = r.Side.ToString(),
        Rating = r.Rating
    };
}
