using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using StageZero.Services;

namespace StageZero.Api;

/// <summary>
/// What every <c>/api</c> request goes through: JSON conventions, the request-origin guard,
/// no caching, and one error shape (RFC 9457 problem details) whose message is only ever
/// one written for the operator.
/// </summary>
public static class ApiPipeline
{
    public const string Prefix = "/api";

    /// <summary>
    /// Every state-changing API call must carry this header. A cross-site form or image
    /// can't set a custom header, and a cross-site script that tries triggers a CORS
    /// preflight this app never answers; the SameSite=Strict cookie is the other half.
    /// </summary>
    public const string RequestHeader = "X-Requested-With";
    public const string RequestHeaderValue = "StageZero";

    public static IServiceCollection AddStageZeroApi(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options => Configure(options.SerializerOptions));
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["traceId"] =
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            };
        });
        return services;
    }

    /// <summary>camelCase, enums as snake_case strings ("service_token"), times as UTC.</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        options.Converters.Add(new UtcDateTimeConverter());
    }

    /// <summary>Refuses a state-changing API request that lacks <see cref="RequestHeader"/>.</summary>
    public static IApplicationBuilder UseApiRequestGuard(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var request = context.Request;
        if (request.Path.StartsWithSegments(Prefix))
        {
            context.Response.Headers.CacheControl = "no-store";

            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)
                && !string.Equals(request.Headers[RequestHeader], RequestHeaderValue, StringComparison.Ordinal))
            {
                await Results.Problem(
                    title: $"Requests that change anything must send the {RequestHeader} header.",
                    statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    });

    /// <summary>
    /// Turns a <see cref="UserFacingException"/> into a problem with its message and status.
    /// Anything else propagates to the exception handler, which answers 500 with a reference
    /// and nothing from the exception.
    /// </summary>
    public static RouteGroupBuilder WithUserFacingErrors(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                return await next(context);
            }
            catch (UserFacingException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: ex.StatusCode);
            }
        });
        return group;
    }

    /// <summary>EF Core reads SQLite times back as Unspecified; every stored time is UTC.</summary>
    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTime().ToUniversalTime();

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime());
    }
}
