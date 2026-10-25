using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Api;

/// <summary>
/// An email address is one address in any case (NOCASE on SQLite, citext on PostgreSQL):
/// one account per address, and signing in doesn't depend on how it was typed. Runs on
/// PostgreSQL too when STAGEZERO_TEST_POSTGRES is set.
/// </summary>
[Collection(AppCollection.Name)]
public class EmailCaseTests
{
    [Fact]
    public async Task Signing_in_ignores_the_case_of_the_address()
    {
        await using var app = new StageZeroApp();
        await TestData.AddUserAsync(app, "Owner@Example.com", TestApi.Password);
        using var client = app.CreateApiClient();

        var response = await client.PostJsonAsync("/api/auth/login", new { email = "owner@EXAMPLE.com", password = TestApi.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_second_account_for_the_same_address_in_another_case_is_refused()
    {
        await using var app = new StageZeroApp();
        await TestData.AddUserAsync(app, "owner@example.com", TestApi.Password);

        await Assert.ThrowsAsync<DbUpdateException>(() => TestData.AddUserAsync(app, "OWNER@example.com", TestApi.Password));

        await using var db = await app.CreateDbContextAsync();
        Assert.Equal("owner@example.com", (await db.Users.SingleAsync(u => u.Email == "Owner@Example.COM")).Email);
    }
}
