using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;

namespace RosterSensei.Application.Interfaces;

public interface IDemoImportService
{
    Task<Result<DemoImportStatusDto>> GetStatusAsync(string parserVersion);
    Task<Result<DemoImportPreviewDto>> PreviewAsync(string userId, int teamId, int matchId, PreviewDemoImportDto dto);
    Task<Result<MatchDto>> ApplyAsync(string userId, int teamId, int matchId, ApplyDemoImportDto dto);
    Task<Result<DemoImportStatusDto>> ReportReadFailureAsync(string userId, int teamId, DemoReadFailureDto dto);
}
