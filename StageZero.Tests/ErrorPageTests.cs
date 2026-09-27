using System.Net;
using Microsoft.AspNetCore.Builder;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests;

/// <summary>Outside Development, an unhandled exception gets a page — not a 404, not a stack trace.</summary>
[Collection(AppCollection.Name)]
public class ErrorPageTests
{
    [Fact]
    public async Task An_unhandled_exception_renders_the_error_page_without_its_details()
    {
        await using var app = new StageZeroApp(environment: "Production")
        {
            AppendToPipeline = pipeline => pipeline.Run(context =>
                context.Request.Path == "/boom"
                    ? throw new InvalidOperationException("secret internal detail")
                    : Task.CompletedTask)
        };
        using var client = app.CreateNonRedirectingClient();

        var response = await client.GetAsync("/boom");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Something went wrong", html);
        Assert.Contains("Reference:", html);
        Assert.DoesNotContain("secret internal detail", html);
        Assert.DoesNotContain("InvalidOperationException", html);
    }
}
