using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;

namespace RosterSensei.Application.Interfaces;

public interface IAvailabilityService
{
    Task<Result<MyAvailabilityDto>> GetMineAsync(string userId, CancellationToken cancellationToken = default);
    Task<Result<MyAvailabilityDto>> SaveWeeklyAsync(string userId, SaveWeeklyAvailabilityDto dto);
    Task<Result<AvailabilityOverrideDto>> SaveOverrideAsync(string userId, DateOnly date, SaveAvailabilityOverrideDto dto);
    Task<Result<string>> DeleteOverrideAsync(string userId, DateOnly date);
    Task<Result<TeamAvailabilityDto>> GetTeamAsync(string userId, int teamId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
