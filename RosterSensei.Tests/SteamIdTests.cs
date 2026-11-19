using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Domain.Enums;
using RosterSensei.Tests.Infrastructure;

namespace RosterSensei.Tests;

public class SteamIdTests
{
    private const string Gabe = "76561197960287930";

    [Theory]
    [InlineData(Gabe)]
    [InlineData(" 76561197960287930 ")]
    [InlineData("https://steamcommunity.com/profiles/76561197960287930")]
    [InlineData("https://steamcommunity.com/profiles/76561197960287930/")]
    [InlineData("http://www.steamcommunity.com/profiles/76561197960287930/?l=english")]
    [InlineData("steamcommunity.com/profiles/76561197960287930")]
    [InlineData("STEAM_0:0:11101")]
    [InlineData("STEAM_1:0:11101")]
    [InlineData("[U:1:22202]")]
    [InlineData("U:1:22202")]
    public void Reads_every_supported_format_as_the_same_SteamID64(string input)
    {
        Assert.True(SteamId.TryParse(input, out var steamId64));
        Assert.Equal(Gabe, steamId64);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("7656119796028793")]          // 16 digits
    [InlineData("765611979602879300")]        // 18 digits
    [InlineData("76561197960265728")]         // account number 0
    [InlineData("12345678901234567")]         // below the individual range
    [InlineData("76561202255233024")]         // above the individual range
    [InlineData("https://steamcommunity.com/groups/valve")]
    [InlineData("https://evil.example/steamcommunity.com/profiles/76561197960287930")]
    [InlineData("https://steamcommunity.com.evil.example/profiles/76561197960287930")]
    [InlineData("STEAM_0:2:11101")]
    [InlineData("[U:1:0]")]
    [InlineData("[U:1:4294967296]")]
    [InlineData("gaben")]
    public void Rejects_anything_that_is_not_an_individual_Steam_account(string? input)
    {
        Assert.False(SteamId.TryParse(input, out _));
    }

    [Fact]
    public void Highest_account_number_is_accepted()
    {
        Assert.True(SteamId.TryParse("[U:1:4294967295]", out var steamId64));
        Assert.Equal("76561202255233023", steamId64);
    }
}
