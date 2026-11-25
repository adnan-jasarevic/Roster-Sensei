using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RosterSensei.Application.DTOs;
using RosterSensei.Infrastructure.Identity;
using RosterSensei.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Json;

namespace RosterSensei.Tests.Api;

// The database allows one account per email. Identity's own duplicate check is switched off here, which is
// what two sign-ups that arrive at the same time look like: both pass the check, and only the unique
// index stops the second one. That must still come back as the normal "email already taken" error.
public class UniqueEmailApiTests : IDisposable
{
    private readonly ApiFactory _api = new(configureServices: services =>
        services.Configure<IdentityOptions>(options => options.User.RequireUniqueEmail = false));

    private Task<HttpResponseMessage> RegisterAsync(string email, string username) =>
        _api.CreateClient().PostAsJsonAsync("/api/Auth/register", new RegisterDto
        {
            Email = email,
            Username = username,
            Password = ApiFactory.Password
        });

    [Fact]
    public async Task A_duplicate_email_that_gets_past_identity_is_refused_by_the_database()
    {
        await _api.SignedInClientAsync("alice");

        var response = await RegisterAsync(ApiFactory.EmailOf("ALICE"), "alice2");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("DuplicateEmail", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_first_user_can_still_log_in_after_the_database_refuses_a_duplicate()
    {
        await _api.SignedInClientAsync("alice");
        await RegisterAsync(ApiFactory.EmailOf("alice"), "alice2");

        var response = await _api.CreateClient().PostAsJsonAsync("/api/Auth/login",
            new LoginDto { Email = ApiFactory.EmailOf("alice"), Password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.NormalizedEmail == "ALICE@EXAMPLE.COM"));
    }

    [Fact]
    public async Task Sign_ups_with_the_same_email_at_the_same_time_create_one_account()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(i => RegisterAsync(ApiFactory.EmailOf("bob"), $"bob{i}")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("DuplicateEmail", await refused.Content.ReadAsStringAsync());
        }
    }

    // The refused user is no longer tracked, so a later save in the same request doesn't insert it after all.
    [Fact]
    public async Task A_refused_user_is_not_left_tracked_in_the_request()
    {
        await _api.SignedInClientAsync("carol");
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var duplicate = new AppUser { Email = ApiFactory.EmailOf("carol"), UserName = "carol2" };

        var result = await users.CreateUserAsync(db, duplicate, ApiFactory.Password);

        Assert.Equal(nameof(IdentityErrorDescriber.DuplicateEmail), Assert.Single(result.Errors).Code);
        Assert.Equal(EntityState.Detached, db.Entry(duplicate).State);
        Assert.Equal(0, await db.SaveChangesAsync());
    }

    public void Dispose() => _api.Dispose();
}
