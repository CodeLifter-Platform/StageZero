using Lifted.BlazorAuth.Basic.Services;
using StageZero.Services.Cloudflare;

namespace StageZero.Api;

/// <summary>
/// The Settings page: the one Cloudflare API token everything else uses. The token goes in
/// on a PUT and never comes back out — responses carry only what Cloudflare says about it.
/// </summary>
public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder api)
    {
        var cloudflare = api.MapGroup("/settings/cloudflare").RequireAuthorization(Session.OperatorPolicy).WithUserFacingErrors();

        cloudflare.MapGet("/", async (ICloudflareCredentialService credentials) =>
            CredentialResponse.From(await credentials.GetStatusAsync(), null));

        // Adds the token, or replaces the saved one. PUT, so the token travels in the body and
        // never in a URL or an access log.
        cloudflare.MapPut("/", async (SaveRequest request, ICloudflareCredentialService credentials) =>
        {
            var check = await credentials.SaveAsync(request.ApiToken ?? string.Empty, request.AccountId);
            return CredentialResponse.From(check.Status, check.Permissions);
        });

        cloudflare.MapPost("/check", async (ICloudflareCredentialService credentials) =>
        {
            var check = await credentials.CheckAsync();
            return CredentialResponse.From(check.Status, check.Permissions);
        });

        cloudflare.MapDelete("/", async (ICloudflareCredentialService credentials) =>
        {
            await credentials.RemoveAsync();
            return Results.NoContent();
        });

        // Email from CodeLifter: the newsletter and StageZero updates, each its own request to
        // codelifter.net's double opt-in subscriptions, for the signed-in operator's address.
        var email = api.MapGroup("/settings/email-updates").RequireAuthorization(Session.OperatorPolicy).WithUserFacingErrors();

        email.MapGet("/", async (IAuthService auth, ISignupOptIn optIn) =>
            new EmailUpdatesView(optIn.Choices.Count > 0, (await auth.GetCurrentUserAsync())?.Email, optIn.Choices));

        email.MapPost("/", async (EmailUpdateRequest request, IAuthService auth, ISignupOptIn optIn) =>
        {
            var choice = optIn.Choices.FirstOrDefault(c => c.Key == request.Key)
                         ?? throw new Services.UserFacingException("Pick the newsletter or StageZero updates.");
            var user = await auth.GetCurrentUserAsync()
                       ?? throw new Services.UserFacingException("Sign in again.", StatusCodes.Status401Unauthorized);
            return await optIn.SubmitAsync(user.Email, [choice.Key]);
        });
    }

    // ── contracts ────────────────────────────────────────────────

    public sealed record SaveRequest(string? ApiToken, string? AccountId);

    /// <param name="Enabled">False when codelifter.net is switched off (a blank <c>CodeLifter__SubscriptionsUrl</c>).</param>
    /// <param name="Email">Where a subscription goes: the signed-in operator's address.</param>
    public sealed record EmailUpdatesView(bool Enabled, string? Email, IReadOnlyList<SignupOptInChoice> Choices);

    public sealed record EmailUpdateRequest(string? Key);

    public sealed record PermissionView(string Scope, string Item, string Level, string UsedFor)
    {
        public static PermissionView From(CloudflarePermission p) => new(p.Scope, p.Item, p.Level, p.UsedFor);
    }

    public sealed record PermissionCheckView(PermissionView Permission, bool Granted, string Detail);

    /// <param name="Required">The permissions the token needs, always sent so the page can show them before any token exists.</param>
    /// <param name="Checks">What the last save or check found; null on a plain read.</param>
    public sealed record CredentialResponse(
        CloudflareCredentialStatus Status,
        IReadOnlyList<PermissionView> Required,
        IReadOnlyList<PermissionCheckView>? Checks)
    {
        public static CredentialResponse From(CloudflareCredentialStatus status, IReadOnlyList<CloudflarePermissionResult>? checks) => new(
            status,
            CloudflareCredentialService.RequiredPermissions.Select(PermissionView.From).ToList(),
            checks?.Select(c => new PermissionCheckView(PermissionView.From(c.Permission), c.Granted, c.Detail)).ToList());
    }
}
