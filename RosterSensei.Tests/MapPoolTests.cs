using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;
using RosterSensei.Application.Validators;
using RosterSensei.Domain.Entities;
using RosterSensei.Domain.Enums;
using RosterSensei.Infrastructure.Repositories;
using RosterSensei.Tests.Infrastructure;

namespace RosterSensei.Tests;

public class MapPoolTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly string _owner;
    private readonly string _officer;
    private readonly string _player;
    private readonly string _outsider;
    private readonly int _teamId;

    public MapPoolTests()
    {
        _owner = _db.AddUser("owner");
        _officer = _db.AddUser("officer");
        _player = _db.AddUser("player");
        _outsider = _db.AddUser("outsider");
        _teamId = _db.AddTeam(_owner, (_officer, TeamRole.Officer), (_player, TeamRole.Player));
    }

    public void Dispose() => _db.Dispose();

    private static SaveMapRatingDto Rating(decimal? value) => new() { Rating = value };

    private async Task<MapPoolDto> Pool(string asUser)
    {
        var result = await _db.MapPoolService().GetAsync(asUser, _teamId);
        Assert.True(result.Success, result.ErrorMessage);
        return result.Data!;
    }

    private async Task Save(string userId, string side, string map, decimal value)
    {
        var result = await _db.MapPoolService().SaveRatingAsync(userId, _teamId, side, map, Rating(value));
        Assert.True(result.Success, result.ErrorMessage);
    }

    private int StoredRatingCount()
    {
        using var context = _db.CreateContext();
        return context.MapRatings.Count();
    }

    [Fact]
    public async Task A_new_team_has_every_map_and_member_and_no_ratings()
    {
        var pool = await Pool(_player);

        Assert.Equal(MapPool.Maps, pool.Maps);
        Assert.Equal(new[] { "owner", "officer", "player" }.Order(), pool.Players.Select(p => p.Username).Order());
        Assert.Empty(pool.Ratings);
        Assert.Equal(_player, pool.CurrentUserId);
    }

    [Fact]
    public async Task A_member_rates_a_side_and_the_whole_team_sees_it()
    {
        await Save(_player, "T", "Mirage", 6.5m);

        var rating = Assert.Single((await Pool(_owner)).Ratings);
        Assert.Equal(_player, rating.UserId);
        Assert.Equal("Mirage", rating.MapName);
        Assert.Equal("T", rating.Side);
        Assert.Equal(6.5m, rating.Rating);
    }

    [Fact]
    public async Task T_and_CT_are_rated_separately()
    {
        await Save(_player, "T", "Nuke", 3m);
        await Save(_player, "CT", "Nuke", 9.5m);

        var ratings = (await Pool(_player)).Ratings;
        Assert.Equal(3m, Assert.Single(ratings, r => r.Side == "T").Rating);
        Assert.Equal(9.5m, Assert.Single(ratings, r => r.Side == "CT").Rating);
    }

    [Fact]
    public async Task Rating_the_same_map_again_replaces_the_old_rating()
    {
        await Save(_player, "CT", "Inferno", 4m);
        await Save(_player, "CT", "Inferno", 7.8m);

        Assert.Equal(7.8m, Assert.Single((await Pool(_player)).Ratings).Rating);
        Assert.Equal(1, StoredRatingCount());
    }

    [Fact]
    public async Task Side_and_map_names_ignore_case_and_are_stored_as_listed()
    {
        await Save(_player, "ct", "dust2", 8m);

        var rating = Assert.Single((await Pool(_player)).Ratings);
        Assert.Equal("CT", rating.Side);
        Assert.Equal("Dust2", rating.MapName);
    }

    [Fact]
    public async Task Clearing_a_rating_removes_it_and_clearing_it_again_is_fine()
    {
        await Save(_player, "T", "Ancient", 5m);

        var first = await _db.MapPoolService().ClearRatingAsync(_player, _teamId, "T", "Ancient");
        var second = await _db.MapPoolService().ClearRatingAsync(_player, _teamId, "T", "Ancient");

        Assert.True(first.Success, first.ErrorMessage);
        Assert.True(second.Success, second.ErrorMessage);
        Assert.Empty((await Pool(_player)).Ratings);
    }

    [Fact]
    public async Task Clearing_only_touches_the_callers_own_rating()
    {
        await Save(_player, "T", "Ancient", 5m);
        await Save(_owner, "T", "Ancient", 9m);

        // Even the Owner can only clear their own column.
        await _db.MapPoolService().ClearRatingAsync(_owner, _teamId, "T", "Ancient");

        var rating = Assert.Single((await Pool(_player)).Ratings);
        Assert.Equal(_player, rating.UserId);
    }

    [Fact]
    public async Task Someone_outside_the_team_can_neither_read_nor_rate()
    {
        var read = await _db.MapPoolService().GetAsync(_outsider, _teamId);
        var save = await _db.MapPoolService().SaveRatingAsync(_outsider, _teamId, "T", "Mirage", Rating(5m));
        var clear = await _db.MapPoolService().ClearRatingAsync(_outsider, _teamId, "T", "Mirage");

        Assert.Equal(ErrorType.Forbidden, read.ErrorType);
        Assert.Equal(ErrorType.Forbidden, save.ErrorType);
        Assert.Equal(ErrorType.Forbidden, clear.ErrorType);
        Assert.Equal(0, StoredRatingCount());
    }

    [Theory]
    [InlineData("T", "Cobblestone")]
    [InlineData("Terrorist", "Mirage")]
    [InlineData("", "Mirage")]
    public async Task A_map_outside_the_pool_or_an_unknown_side_is_not_found(string side, string map)
    {
        var result = await _db.MapPoolService().SaveRatingAsync(_player, _teamId, side, map, Rating(5m));

        Assert.False(result.Success);
        Assert.Equal(ErrorType.NotFound, result.ErrorType);
        Assert.Equal(0, StoredRatingCount());
    }

    [Fact]
    public async Task Leaving_the_team_takes_the_members_ratings_with_them()
    {
        await Save(_player, "T", "Mirage", 6m);
        await Save(_owner, "T", "Mirage", 8m);

        Assert.True((await _db.TeamService().LeaveTeamAsync(_teamId, _player)).Success);

        var pool = await Pool(_owner);
        Assert.DoesNotContain(pool.Players, p => p.UserId == _player);
        Assert.Equal(_owner, Assert.Single(pool.Ratings).UserId);
        Assert.Equal(1, StoredRatingCount());
    }

    [Fact]
    public async Task Being_removed_from_the_team_takes_the_members_ratings_with_them()
    {
        await Save(_player, "CT", "Train", 2.5m);

        Assert.True((await _db.TeamService().RemoveMemberAsync(_teamId, _owner, _player)).Success);

        Assert.Equal(0, StoredRatingCount());
    }

    [Fact]
    public async Task Deleting_the_team_deletes_its_ratings()
    {
        await Save(_player, "T", "Vertigo", 4m);
        await Save(_owner, "CT", "Vertigo", 4m);

        Assert.True((await _db.TeamService().DeleteTeamAsync(_teamId, _owner)).Success);

        Assert.Equal(0, StoredRatingCount());
    }

    [Fact]
    public async Task A_rating_from_someone_who_left_mid_request_is_refused()
    {
        // The save passed its membership check, then the player left before the rating was written.
        Assert.True((await _db.TeamService().LeaveTeamAsync(_teamId, _player)).Success);

        var outcome = await new MapRatingRepository(_db.CreateContext()).SaveAsync(new MapRating
        {
            TeamId = _teamId,
            UserId = _player,
            MapName = "Mirage",
            Side = MapSide.T,
            Rating = 7m
        });

        Assert.Equal(WriteOutcome.Rejected, outcome);
        Assert.Equal(0, StoredRatingCount());
    }

    [Fact]
    public async Task Ratings_of_another_team_are_not_shown()
    {
        var otherTeam = _db.AddTeam(_outsider, (_player, TeamRole.Player));
        var other = await _db.MapPoolService().SaveRatingAsync(_player, otherTeam, "T", "Mirage", Rating(2m));
        Assert.True(other.Success, other.ErrorMessage);

        Assert.Empty((await Pool(_player)).Ratings);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("10")]
    [InlineData("6.5")]
    [InlineData("7.8")]
    [InlineData("9.0")]
    public void Ratings_from_1_to_10_with_one_decimal_are_valid(string value)
    {
        var result = new SaveMapRatingDtoValidator().Validate(Rating(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.True(result.IsValid, string.Join(", ", result.Errors));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.9")]
    [InlineData("10.1")]
    [InlineData("-5")]
    [InlineData("6.55")]
    [InlineData("7.25")]
    public void Ratings_outside_1_to_10_or_with_more_decimals_are_invalid(string value)
    {
        var result = new SaveMapRatingDtoValidator().Validate(Rating(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_missing_rating_is_invalid()
    {
        Assert.False(new SaveMapRatingDtoValidator().Validate(Rating(null)).IsValid);
    }
}
