using Microsoft.EntityFrameworkCore;
using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;
using RosterSensei.Domain.Enums;
using RosterSensei.Infrastructure.Repositories;
using RosterSensei.Tests.Infrastructure;

namespace RosterSensei.Tests;

// Stored Steam names and avatars, the weekly background refresh, and sign-in state kept in the database.
public class SteamProfileRefreshTests : IDisposable
{
    private const string AliceSteam = "76561197960287930";
    private const string BobSteam = "76561198000000000";
    private const string AccountPage = TestDatabase.FrontendOrigin + "/account";

    private readonly TestDatabase _db = new();
    private readonly string _alice;
    private readonly string _bob;

    public SteamProfileRefreshTests()
    {
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
    }

    public void Dispose() => _db.Dispose();

    private void Link(string userId, string steamId, bool verified = true, DateTime? due = null, string? name = null)
    {
        using var context = _db.CreateContext();
        var user = context.Users.Single(u => u.Id == userId);
        user.SteamId = steamId;
        user.SteamVerified = verified;
        user.SteamPersonaName = name;
        user.SteamProfileRefreshDueAt = due;
        context.SaveChanges();
    }

    private (string? Name, string? Avatar, DateTime? Due) Stored(string userId)
    {
        using var context = _db.CreateContext();
        return context.Users.Where(u => u.Id == userId)
            .Select(u => new { u.SteamPersonaName, u.SteamAvatarUrl, u.SteamProfileRefreshDueAt })
            .AsEnumerable()
            .Select(u => (u.SteamPersonaName, u.SteamAvatarUrl, u.SteamProfileRefreshDueAt))
            .Single();
    }

    private DateTime Now => _db.Time.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task A_new_link_is_looked_up_and_then_left_alone_for_a_week()
    {
        Link(_alice, AliceSteam);
        _db.SteamProfiles.Profiles[AliceSteam] = new SteamProfile("gaben", "https://avatars.steamstatic.com/a.jpg");

        Assert.Equal(1, await _db.SteamProfileRefresher().RunOnceAsync(CancellationToken.None));
        Assert.Equal(("gaben", "https://avatars.steamstatic.com/a.jpg", Now + SteamProfileRefresh.Interval), Stored(_alice));

        // Later passes the same week don't ask Steam again.
        _db.Time.Advance(TimeSpan.FromDays(6));
        await _db.SteamProfileRefresher().RunOnceAsync(CancellationToken.None);
        Assert.Single(_db.SteamProfiles.Requested);

        // A week on, a changed Steam name shows up.
        _db.Time.Advance(TimeSpan.FromDays(1));
        _db.SteamProfiles.Profiles[AliceSteam] = new SteamProfile("gabe", null);
        await _db.SteamProfileRefresher().RunOnceAsync(CancellationToken.None);
        Assert.Equal(("gabe", null, Now + SteamProfileRefresh.Interval), Stored(_alice));
    }

    [Fact]
    public async Task When_Steam_does_not_answer_the_old_name_stays_and_it_is_tried_again_a_day_later()
    {
        Link(_alice, AliceSteam, due: Now, name: "gaben");

        Assert.Equal(0, await _db.SteamProfileRefresher().RunOnceAsync(CancellationToken.None));
        Assert.Equal(("gaben", null, Now + SteamProfileRefresh.RetryAfter), Stored(_alice));
    }

    [Fact]
    public async Task Made_up_players_are_never_looked_up()
    {
        Link(_alice, AliceSteam, due: SteamProfileRefresh.Never, name: "fake");
        Link(_bob, BobSteam, verified: false);

        await _db.SteamProfileRefresher().RunOnceAsync(CancellationToken.None);

        // Bob's pasted link is still looked up; only the made-up player is skipped.
        Assert.Equal([BobSteam], _db.SteamProfiles.Requested);
        Assert.Equal("fake", Stored(_alice).Name);
    }

    [Fact]
    public async Task Many_due_accounts_are_spread_over_batches()
    {
        for (var i = 0; i < 60; i++)
        {
            var user = _db.AddUser($"p{i}");
            var steamId = (76561198000001000L + i).ToString();
            Link(user, steamId);
            _db.SteamProfiles.Profiles[steamId] = new SteamProfile($"p{i}", null);
        }

        Assert.Equal(60, await _db.SteamProfileRefresher().RunOnceAsync(CancellationToken.None));
        Assert.Equal(60, _db.SteamProfiles.Requested.Distinct().Count());
        Assert.Equal(60, _db.SteamProfiles.Requested.Count);
    }

    [Fact]
    public async Task A_lookup_that_finishes_after_an_unlink_or_relink_is_not_saved()
    {
        Link(_alice, AliceSteam);
        await _db.AccountService().UnlinkSteamAccountAsync(_alice);

        using var context = _db.CreateContext();
        await new UserRepository(context).SaveSteamProfileAsync(_alice, AliceSteam, new SteamProfile("gaben", null), Now);

        Assert.Equal((null, null, null), Stored(_alice));
    }

    [Fact]
    public async Task The_roster_shows_the_display_name_and_the_stored_Steam_name_without_asking_Steam()
    {
        var teamId = _db.AddTeam(_alice, (_bob, TeamRole.Player));
        Link(_bob, BobSteam, name: "b0b", due: Now + TimeSpan.FromDays(3));

        var members = await _db.TeamService().GetTeamMembersAsync(teamId, _alice);

        var bob = Assert.Single(members.Data!, m => m.Id == _bob);
        Assert.Equal("bob", bob.Username);
        Assert.Equal("b0b", bob.SteamPersonaName);
        Assert.Empty(_db.SteamProfiles.Requested);
    }

    [Fact]
    public async Task Signing_in_with_a_different_Steam_account_drops_the_old_name()
    {
        Link(_alice, BobSteam, name: "old name", due: Now + TimeSpan.FromDays(3));

        var started = await _db.AccountService().StartSteamSignInAsync(_alice, new StartSteamSignInDto { ReturnUrl = AccountPage });
        var returnTo = Uri.UnescapeDataString(new Uri(started.Data!.RedirectUrl).Query.Split("openid.return_to=")[1].Split('&')[0]);
        // Steam doesn't answer for the new account's profile.
        var result = await _db.AccountService().CompleteSteamSignInAsync(_alice, new CompleteSteamSignInDto
        {
            Parameters = new Dictionary<string, string> { ["openid.mode"] = "id_res", ["openid.return_to"] = returnTo }
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(AliceSteam, result.Data!.SteamId);
        Assert.Null(result.Data.PersonaName);
        // Due straight away, so the background refresh fills it in on its next pass.
        Assert.Equal((null, null, null), Stored(_alice));
    }

    [Fact]
    public async Task Sign_in_state_lives_in_the_database_as_a_hash_and_survives_a_restart()
    {
        var issued = await _db.SteamStates().IssueAsync(_alice, s => $"{AccountPage}?rs_state={s}");
        Assert.NotNull(issued);

        using (var context = _db.CreateContext())
        {
            var row = await context.SteamSignInStates.SingleAsync();
            Assert.NotEqual(issued.State, row.Id);
            Assert.Equal(64, row.Id.Length);
        }

        // A new store (another API instance, or the API after a restart) can finish it, once.
        Assert.True(await _db.SteamStates().TryConsumeAsync(issued.State, _alice, issued.ReturnTo));
        Assert.False(await _db.SteamStates().TryConsumeAsync(issued.State, _alice, issued.ReturnTo));
    }

    [Fact]
    public async Task The_background_pass_deletes_expired_sign_ins()
    {
        await _db.SteamStates().IssueAsync(_alice, s => $"{AccountPage}?rs_state={s}");
        _db.Time.Advance(TimeSpan.FromMinutes(5));
        await _db.SteamStates().IssueAsync(_bob, s => $"{AccountPage}?rs_state={s}");

        _db.Time.Advance(TimeSpan.FromMinutes(6));
        await _db.SteamProfileRefresher().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, _db.PendingSteamSignIns());
    }
}
