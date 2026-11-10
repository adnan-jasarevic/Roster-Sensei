using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;

namespace RosterSensei.Application.Interfaces;

public interface IMapPoolService
{
    Task<Result<MapPoolDto>> GetAsync(string userId, int teamId, CancellationToken cancellationToken = default);

    // Each member only ever changes their own ratings: the user comes from the token, never the request.
    Task<Result<MapRatingDto>> SaveRatingAsync(string userId, int teamId, string side, string mapName, SaveMapRatingDto dto);
    Task<Result<string>> ClearRatingAsync(string userId, int teamId, string side, string mapName);
}
