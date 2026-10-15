using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace StageZero.Api;

/// <summary>
/// Serves the React app that <c>web/</c> builds into <c>wwwroot</c>. Hashed assets under
/// <c>/assets</c> are cached for a year; <c>index.html</c> never is, so a new build is picked
/// up on the next load. Any path that isn't the API or a file gets <c>index.html</c>, which
/// is what lets a deep link or a reload land on the right screen.
/// </summary>
public static class SpaHosting
{
    public const string IndexFile = "index.html";

    public static IApplicationBuilder UseSpaStaticFiles(this IApplicationBuilder app) =>
        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = new FileExtensionContentTypeProvider(),
            OnPrepareResponse = context =>
            {
                var headers = context.Context.Response.Headers;
                headers.CacheControl = context.Context.Request.Path.StartsWithSegments("/assets")
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
            }
        });

    public static void MapSpaFallback(this WebApplication app)
    {
        // An unknown API route is a 404 problem, never the app's HTML.
        app.MapFallback(ApiPipeline.Prefix + "/{**rest}", () =>
            Results.Problem(title: "No such API endpoint.", statusCode: StatusCodes.Status404NotFound));

        app.MapFallback(async context =>
        {
            var index = app.Environment.WebRootFileProvider.GetFileInfo(IndexFile);
            if (!index.Exists)
            {
                // A source checkout run with `dotnet run` before the web app was ever built.
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(
                    "StageZero's web app has not been built. Run `npm ci && npm run build` in web/, "
                    + "or start the Vite dev server with `npm run dev` and open that instead.");
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.SendFileAsync(index);
        });
    }
}
