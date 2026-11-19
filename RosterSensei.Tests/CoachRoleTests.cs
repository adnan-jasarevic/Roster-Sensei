using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Domain.Entities;
using RosterSensei.Domain.Enums;
using RosterSensei.Tests.Infrastructure;

namespace RosterSensei.Tests;

// Coaches run the team like the Captain and Officers, can be several, can receive ownership, but never play.
public class CoachRoleTests : IDisposable
{
    private static readonly DateTime Yesterday = DateTime.UtcNow.Date.AddDays(-1);

    private readonly TestDatabase _db = new();
    private readonly string _owner;
    private readonly string _coach;
    private readonly string _player;
    private readonly int _teamId;

    public CoachRoleTests()
    {
        _owner = _db.AddUser("owner");
        _coach = _db.AddUser("coach");
        _player = _db.AddUser("player");
        _teamId = _db.AddTeam(_owner, (_coach, TeamRole.Coach), (_player, TeamRole.Player));
    }

    public void Dispose() => _db.Dispose();

    private static CreateMatchDto Match(params string[] lineup) => new()
    {
        OpponentName = "Kyoto Kitsune",
        ScheduledDate = Yesterday,
        Status = MatchStatus.Scheduled,
        Lineup = lineup.Select(id => new LineupPlayerDto { UserId = id }).ToList()
    };

    private static UpdateMatchDto Edit(MatchDto match, params string[] lineup) => new()
    {
        Version = match.Version,
        OpponentName = match.OpponentName,
        ScheduledDate = match.ScheduledDate,
        Status = MatchStatus.Scheduled,
        Lineup = lineup.Select(id => new LineupPlayerDto { UserId = id }).ToList()
    };

    private static SetMatchLineupDto Lineup(params string[] players) => new()
    {
        Players = players.Select(id => new LineupPlayerDto { UserId = id }).ToList()
    };

    [Fact]
    public async Task Owner_can_make_several_members_Coach()
    {
        var result = await _db.TeamService().ChangeMemberRoleAsync(_teamId, _owner, _player, "Coach");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(TeamRole.Coach, _db.RoleOf(_teamId, _player));
        Assert.Equal(TeamRole.Coach, _db.RoleOf(_teamId, _coach));
    }

    [Fact]
    public async Task Only_the_Owner_can_make_someone_Coach()
    {
        var result = await _db.TeamService().ChangeMemberRoleAsync(_teamId, _coach, _player, "Coach");

        Assert.Equal(ErrorType.Forbidden, result.ErrorType);
        Assert.Equal(TeamRole.Player, _db.RoleOf(_teamId, _player));
    }

    [Fact]
    public async Task Owner_can_make_themselves_Coach()
    {
        var result = await _db.TeamService().ChangeMemberRoleAsync(_teamId, _owner, _owner, "Coach");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(TeamRole.Coach, _db.RoleOf(_teamId, _owner));
        Assert.Equal(_owner, _db.OwnerOf(_teamId));
    }

    [Fact]
    public async Task Coach_can_manage_the_team_like_a_Captain()
    {
        var invite = await _db.TeamService().InviteMemberAsync(_teamId, _coach, TestDatabase.EmailOf(_db.AddUser("invitee")));
        Assert.True(invite.Success, invite.ErrorMessage);

        var match = await _db.MatchService().AddMatchAsync(_coach, _teamId, Match(_player));
        Assert.True(match.Success, match.ErrorMessage);

        var lineup = await _db.MatchService().SetLineupAsync(_coach, _teamId, match.Data!.Id, Lineup(_owner, _player));
        Assert.True(lineup.Success, lineup.ErrorMessage);
    }

    [Fact]
    public async Task Ownership_can_be_transferred_to_a_Coach()
    {
        var result = await _db.TeamService().TransferOwnershipAsync(_teamId, _owner, _coach);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(_coach, _db.OwnerOf(_teamId));
        Assert.Equal(TeamRole.Coach, _db.RoleOf(_teamId, _coach));
    }

    [Fact]
    public async Task A_new_match_cannot_have_a_Coach_in_its_lineup()
    {
        var result = await _db.MatchService().AddMatchAsync(_owner, _teamId, Match(_player, _coach));

        Assert.Equal(ErrorType.Validation, result.ErrorType);
        Assert.Equal("Coaches can't be put in a lineup.", result.ErrorMessage);
    }

    [Fact]
    public async Task A_Coach_cannot_be_added_to_a_lineup()
    {
        var match = await _db.MatchService().AddMatchAsync(_owner, _teamId, Match(_player));

        var result = await _db.MatchService().SetLineupAsync(_owner, _teamId, match.Data!.Id, Lineup(_player, _coach));

        Assert.Equal(ErrorType.Validation, result.ErrorType);
        Assert.Equal("Coaches can't be put in a lineup.", result.ErrorMessage);
    }

    [Fact]
    public async Task A_Coach_cannot_be_added_when_editing_a_match()
    {
        var match = await _db.MatchService().AddMatchAsync(_owner, _teamId, Match(_player));

        var result = await _db.MatchService().UpdateMatchAsync(_owner, _teamId, match.Data!.Id, Edit(match.Data!, _player, _coach));

        Assert.Equal(ErrorType.Validation, result.ErrorType);
        Assert.Equal("Coaches can't be put in a lineup.", result.ErrorMessage);
    }

    [Fact]
    public async Task A_Player_who_becomes_Coach_keeps_their_place_in_past_lineups()
    {
        var match = await _db.MatchService().AddMatchAsync(_owner, _teamId, Match(_player));
        await _db.TeamService().ChangeMemberRoleAsync(_teamId, _owner, _player, "Coach");

        var lineup = await _db.MatchService().SetLineupAsync(_owner, _teamId, match.Data!.Id, Lineup(_player, _owner));
        Assert.True(lineup.Success, lineup.ErrorMessage);

        var stored = (await _db.MatchService().GetTeamMatchesAsync(_owner, _teamId)).Data!.Single(m => m.Id == match.Data!.Id);
        var edited = await _db.MatchService().UpdateMatchAsync(_owner, _teamId, match.Data!.Id, Edit(stored, _player));
        Assert.True(edited.Success, edited.ErrorMessage);
        Assert.Equal([_player], edited.Data!.Lineup.Select(p => p.UserId));
    }
}
