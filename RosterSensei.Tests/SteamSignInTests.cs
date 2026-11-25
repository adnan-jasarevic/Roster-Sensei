using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;
using RosterSensei.Application.Services;
using RosterSensei.Domain.Enums;
using RosterSensei.Infrastructure.Repositories;
using RosterSensei.Tests.Infrastructure;

namespace RosterSensei.Tests;

// Linking a Steam account by signing in through Steam. Steam's own check is faked; SteamOpenIdVerifierTests
// covers the real one.
public class SteamSignInTests : IDisposable
{
    private const string SteamId64 = "76561197960287930";
    private const string OtherSteamId64 = "76561198000000000";
    private const string AccountPage = TestDatabase.FrontendOrigin + "/account";

    private readonly TestDatabase _db = new();
    private readonly string _alice;
    private readonly string _bob;

    public SteamSignInTests()
    {
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
    }

    public void Dispose() => _db.Dispose();

    // Starts a sign-in and returns the reply Steam would send back, as the browser forwards it.
    private async Task<CompleteSteamSignInDto> SignInAtSteamAsync(string userId, string mode = "id_res")
    {
        var started = await _db.AccountService().StartSteamSignInAsync(userId, new StartSteamSignInDto { ReturnUrl = AccountPage });
        Assert.True(started.Success, started.ErrorMessage);
        return Reply(ReturnToOf(started.Data!.RedirectUrl), mode);
    }

    private static CompleteSteamSignInDto Reply(string returnTo, string mode = "id_res") => new()
    {
        Parameters = new Dictionary<string, string>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = mode,
            ["openid.return_to"] = returnTo,
            ["openid.claimed_id"] = $"https://steamcommunity.com/openid/id/{SteamId64}",
            ["openid.identity"] = $"https://steamcommunity.com/openid/id/{SteamId64}"
        }
    };

    private static string ReturnToOf(string redirectUrl)
    {
        var query = new Uri(redirectUrl).Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
        return query["openid.return_to"];
    }

    private void SeedPastedLink(string userId, string steamId)
    {
        using var context = _db.CreateContext();
        var user = context.Users.Single(u => u.Id == userId);
        user.SteamId = steamId;
        user.SteamVerified = false;
        context.SaveChanges();
    }

    [Fact]
    public async Task A_new_user_has_no_Steam_account()
    {
        var result = await _db.AccountService().GetSteamAccountAsync(_alice);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Null(result.Data!.SteamId);
        Assert.Null(result.Data.ProfileUrl);
        Assert.False(result.Data.Verified);
    }

    [Fact]
    public async Task Starting_returns_a_Steam_page_that_comes_back_to_the_account_page_with_a_state()
    {
        var result = await _db.AccountService().StartSteamSignInAsync(_alice, new StartSteamSignInDto { ReturnUrl = AccountPage + "?old=1" });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.StartsWith("https://steamcommunity.com/openid/login?", result.Data!.RedirectUrl);
        Assert.Matches($"^{AccountPage}\\?{AccountService.StateParameter}=[0-9A-F]{{64}}$", ReturnToOf(result.Data.RedirectUrl));
    }

    [Theory]
    [InlineData("https://evil.example/account")]
    [InlineData("http://localhost:5174/account")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/account")]
    [InlineData("http://user@localhost:5173/account")]
    [InlineData("http://localhost:5173/account#x")]
    public async Task Steam_may_only_return_to_our_own_frontend(string returnUrl)
    {
        var result = await _db.AccountService().StartSteamSignInAsync(_alice, new StartSteamSignInDto { ReturnUrl = returnUrl });

        Assert.False(result.Success);
        Assert.Equal(ErrorType.Validation, result.ErrorType);
        Assert.Equal(0, _db.PendingSteamSignIns());
    }

    [Fact]
    public async Task Signing_in_through_Steam_links_a_verified_account()
    {
        var reply = await SignInAtSteamAsync(_alice);

        var result = await _db.AccountService().CompleteSteamSignInAsync(_alice, reply);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(SteamId64, result.Data!.SteamId);
        Assert.True(result.Data.Verified);
        Assert.Equal($"https://steamcommunity.com/profiles/{SteamId64}", result.Data.ProfileUrl);
        Assert.True((await _db.AccountService().GetSteamAccountAsync(_alice)).Data!.Verified);
    }

    [Fact]
    public async Task A_reply_started_by_someone_else_is_refused()
    {
        // Login CSRF: Bob signs in to his own Steam, then gets Alice to submit the reply.
        var bobsReply = await SignInAtSteamAsync(_bob);

        var result = await _db.AccountService().CompleteSteamSignInAsync(_alice, bobsReply);

        Assert.False(result.Success);
        Assert.Null((await _db.AccountService().GetSteamAccountAsync(_alice)).Data!.SteamId);
        Assert.Equal(0, _db.SteamOpenId.VerifyCalls);
    }

    [Fact]
    public async Task A_reply_can_be_used_once()
    {
        var reply = await SignInAtSteamAsync(_alice);
        Assert.True((await _db.AccountService().CompleteSteamSignInAsync(_alice, reply)).Success);
        await _db.AccountService().UnlinkSteamAccountAsync(_alice);

        var replay = await _db.AccountService().CompleteSteamSignInAsync(_alice, reply);

        Assert.False(replay.Success);
        Assert.Null((await _db.AccountService().GetSteamAccountAsync(_alice)).Data!.SteamId);
    }

    [Fact]
    public async Task The_same_reply_sent_twice_at_once_is_checked_with_Steam_once()
    {
        var reply = await SignInAtSteamAsync(_alice);

        var results = await Task.WhenAll(
            _db.AccountService().CompleteSteamSignInAsync(_alice, reply),
            _db.AccountService().CompleteSteamSignInAsync(_alice, reply));

        Assert.Single(results, r => r.Success);
        Assert.Equal(1, _db.SteamOpenId.VerifyCalls);
    }

    [Fact]
    public async Task A_sign_in_expires_after_ten_minutes()
    {
        var reply = await SignInAtSteamAsync(_alice);
        _db.Time.Advance(SteamSignInStateStore.Lifetime + TimeSpan.FromSeconds(1));

        var result = await _db.AccountService().CompleteSteamSignInAsync(_alice, reply);

        Assert.False(result.Success);
        Assert.Contains("expired", result.ErrorMessage);
    }

    [Fact]
    public async Task A_reply_with_an_edited_return_URL_is_refused()
    {
        var reply = await SignInAtSteamAsync(_alice);
        reply.Parameters["openid.return_to"] = reply.Parameters["openid.return_to"].Replace("/account", "/elsewhere");

        var result = await _db.AccountService().CompleteSteamSignInAsync(_alice, reply);

        Assert.False(result.Success);
        Assert.Equal(0, _db.SteamOpenId.VerifyCalls);
    }

    [Fact]
    public async Task Cancelling_at_Steam_says_so()
    {
        var reply = await SignInAtSteamAsync(_alice, mode: "cancel");

        var result = await _db.AccountService().CompleteSteamSignInAsync(_alice, reply);

        Assert.False(result.Success);
        Assert.Equal("Steam sign-in was cancelled.", result.ErrorMessage);
    }

    [Fact]
    public async Task Nothing_is_linked_when_Steam_does_not_confirm_the_reply()
    {
        _db.SteamOpenId.SteamIdToReturn = null;
        var reply = await SignInAtSteamAsync(_alice);

        var result = await _db.AccountService().CompleteSteamSignInAsync(_alice, reply);

        Assert.False(result.Success);
        Assert.Null((await _db.AccountService().GetSteamAccountAsync(_alice)).Data!.SteamId);
    }

    [Fact]
    public async Task Signing_in_takes_the_account_over_from_an_unverified_pasted_link()
    {
        SeedPastedLink(_bob, SteamId64);

        var result = await _db.AccountService().CompleteSteamSignInAsync(_alice, await SignInAtSteamAsync(_alice));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(SteamId64, (await _db.AccountService().GetSteamAccountAsync(_alice)).Data!.SteamId);
        Assert.Null((await _db.AccountService().GetSteamAccountAsync(_bob)).Data!.SteamId);
    }

    [Fact]
    public async Task An_account_verified_by_someone_else_is_refused()
    {
        Assert.True((await _db.AccountService().CompleteSteamSignInAsync(_bob, await SignInAtSteamAsync(_bob))).Success);

        var result = await _db.AccountService().CompleteSteamSignInAsync(_alice, await SignInAtSteamAsync(_alice));

        Assert.Equal(ErrorType.Conflict, result.ErrorType);
        Assert.Equal(SteamId64, (await _db.AccountService().GetSteamAccountAsync(_bob)).Data!.SteamId);
        Assert.Null((await _db.AccountService().GetSteamAccountAsync(_alice)).Data!.SteamId);
    }

    [Fact]
    public async Task Two_users_verifying_the_same_account_at_once_end_with_one_owner()
    {
        var aliceReply = await SignInAtSteamAsync(_alice);
        var bobReply = await SignInAtSteamAsync(_bob);

        var results = await Task.WhenAll(
            _db.AccountService().CompleteSteamSignInAsync(_alice, aliceReply),
            _db.AccountService().CompleteSteamSignInAsync(_bob, bobReply));

        Assert.Single(results, r => r.Success);
        Assert.Single(results, r => r.ErrorType == ErrorType.Conflict);
    }

    [Fact]
    public async Task Signing_in_with_another_Steam_account_replaces_the_old_one()
    {
        await _db.AccountService().CompleteSteamSignInAsync(_alice, await SignInAtSteamAsync(_alice));
        _db.SteamOpenId.SteamIdToReturn = OtherSteamId64;

        var result = await _db.AccountService().CompleteSteamSignInAsync(_alice, await SignInAtSteamAsync(_alice));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(OtherSteamId64, result.Data!.SteamId);
    }

    [Fact]
    public async Task Unlinking_clears_the_account_and_its_verification()
    {
        await _db.AccountService().CompleteSteamSignInAsync(_alice, await SignInAtSteamAsync(_alice));

        var result = await _db.AccountService().UnlinkSteamAccountAsync(_alice);

        Assert.True(result.Success, result.ErrorMessage);
        var account = (await _db.AccountService().GetSteamAccountAsync(_alice)).Data!;
        Assert.Null(account.SteamId);
        Assert.False(account.Verified);
        Assert.True((await _db.AccountService().UnlinkSteamAccountAsync(_alice)).Success);
    }

    [Fact]
    public async Task Unknown_users_get_not_found()
    {
        Assert.Equal(ErrorType.NotFound, (await _db.AccountService().GetSteamAccountAsync("ghost")).ErrorType);
        Assert.Equal(ErrorType.NotFound, (await _db.AccountService().StartSteamSignInAsync("ghost", new StartSteamSignInDto { ReturnUrl = AccountPage })).ErrorType);
        Assert.Equal(ErrorType.NotFound, (await _db.AccountService().UnlinkSteamAccountAsync("ghost")).ErrorType);
    }

    [Fact]
    public async Task Abandoned_sign_ins_do_not_pile_up()
    {
        // A user restarting over and over keeps only a few; their newest still works.
        CompleteSteamSignInDto? latest = null;
        for (var i = 0; i < 20; i++)
            latest = await SignInAtSteamAsync(_alice);
        Assert.Equal(SteamSignInStateStore.MaxPendingPerUser, _db.PendingSteamSignIns());

        // Expired ones are dropped the next time anyone starts a sign-in.
        _db.Time.Advance(SteamSignInStateStore.Lifetime + TimeSpan.FromSeconds(1));
        var bobs = await SignInAtSteamAsync(_bob);
        Assert.Equal(1, _db.PendingSteamSignIns());

        Assert.True((await _db.AccountService().CompleteSteamSignInAsync(_bob, bobs)).Success);
        Assert.False((await _db.AccountService().CompleteSteamSignInAsync(_alice, latest!)).Success);
    }

    [Fact]
    public async Task The_account_and_roster_show_the_Steam_name_and_avatar_and_whether_it_is_verified()
    {
        _db.SteamProfiles.Profiles[SteamId64] = new SteamProfile("gaben", "https://avatars.steamstatic.com/a_medium.jpg");
        var teamId = _db.AddTeam(_alice, (_bob, TeamRole.Player));
        await _db.AccountService().CompleteSteamSignInAsync(_bob, await SignInAtSteamAsync(_bob));
        SeedPastedLink(_alice, OtherSteamId64);

        var account = (await _db.AccountService().GetSteamAccountAsync(_bob)).Data!;
        Assert.Equal("gaben", account.PersonaName);
        Assert.Equal("https://avatars.steamstatic.com/a_medium.jpg", account.AvatarUrl);

        var members = await _db.TeamService().GetTeamMembersAsync(teamId, _alice);
        Assert.True(members.Success, members.ErrorMessage);
        var bob = Assert.Single(members.Data!, m => m.Id == _bob);
        Assert.True(bob.SteamVerified);
        Assert.Equal("gaben", bob.SteamPersonaName);
        Assert.Equal("https://avatars.steamstatic.com/a_medium.jpg", bob.SteamAvatarUrl);
        Assert.Equal($"https://steamcommunity.com/profiles/{SteamId64}", bob.SteamProfileUrl);

        var alice = Assert.Single(members.Data!, m => m.Id == _alice);
        Assert.Equal(OtherSteamId64, alice.SteamId);
        Assert.False(alice.SteamVerified);
        Assert.Null(alice.SteamPersonaName);
    }
}
