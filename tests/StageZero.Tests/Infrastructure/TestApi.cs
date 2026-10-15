using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StageZero.Api;

namespace StageZero.Tests.Infrastructure;

/// <summary>
/// Calls the API the way the web app does: same JSON conventions, the request header every
/// state-changing call carries, and a cookie jar so a sign-in sticks across requests.
/// </summary>
internal static class TestApi
{
    public const string Password = "correct horse battery";

    public static readonly JsonSerializerOptions Json = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ApiPipeline.Configure(options);
        return options;
    }

    /// <summary>A browser tab: carries the request header and keeps cookies.</summary>
    public static HttpClient CreateApiClient(this StageZeroApp app)
    {
        var client = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        client.DefaultRequestHeaders.Add(ApiPipeline.RequestHeader, ApiPipeline.RequestHeaderValue);
        return client;
    }

    /// <summary>Adds an operator account and signs a fresh client in as them.</summary>
    public static async Task<HttpClient> SignedInClientAsync(this StageZeroApp app, string email = "owner@example.com")
    {
        await TestData.AddUserAsync(app, email, Password);
        var client = app.CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object? body = null) =>
        client.PostAsJsonAsync(url, body ?? new { }, Json);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, Json);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    /// <summary>The problem title — the one message the API shows the operator.</summary>
    public static async Task<string> ProblemTitleAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("title").GetString()!;
    }
}
