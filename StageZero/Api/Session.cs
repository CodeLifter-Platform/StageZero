using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Lifted.BlazorAuth.Basic.Models;
using Lifted.BlazorAuth.Basic.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace StageZero.Api;

/// <summary>
/// The signed-in session: an encrypted, HTTP-only cookie (ASP.NET Data Protection, the same
/// key ring that protects the Cloudflare tokens) holding the user's id and a stamp of their
/// credentials. Every request re-reads the user and makes them current on
/// <see cref="IAuthService"/>, so a deleted, deactivated or locked-out account, or a changed
/// password or email, ends every session at its next request.
/// </summary>
public static class Session
{
    public const string CookieName = "stagezero.session";

    /// <summary>Signed in, with nothing left to do before using the app.</summary>
    public const string OperatorPolicy = "operator";

    /// <summary>Signed in, possibly still owing the account setup (verify email, replace password).</summary>
    public const string SignedInPolicy = "signed-in";

    public const string StampClaim = "sz:stamp";

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public static IServiceCollection AddStageZeroSession(this IServiceCollection services)
    {
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                // Strict: the browser never attaches it to a request another site starts.
                options.Cookie.SameSite = SameSiteMode.Strict;
                // Cloudflare terminates TLS and forwards plain HTTP; with forwarded headers
                // applied the request reads as https there, so the cookie is Secure. On a
                // LAN over plain HTTP it can't be.
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = Lifetime;
                options.SlidingExpiration = true;

                // An API answers with a status, never a redirect to a login page.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = ValidateAsync;
            });

        services.AddSingleton<IAuthorizationHandler, AccountSetupCompleteHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(SignedInPolicy, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(OperatorPolicy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new AccountSetupCompleteRequirement()))
            .SetFallbackPolicy(null);

        return services;
    }

    /// <summary>Issues the cookie for a user who has just proved who they are.</summary>
    public static Task SignInAsync(HttpContext context, User user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(StampClaim, Stamp(user)),
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        return context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    public static Task SignOutAsync(HttpContext context) =>
        context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    /// <summary>
    /// A digest of what the session was issued against. Changing the password or the email
    /// changes it, which voids every cookie issued before.
    /// </summary>
    public static string Stamp(User user)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{user.Id}\n{user.Email}\n{user.PasswordHash}"));
        return Convert.ToBase64String(bytes, 0, 16);
    }

    private static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var auth = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
        var idClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.Principal?.FindFirstValue(StampClaim);

        if (int.TryParse(idClaim, out var userId)
            && await auth.ResumeAsync(userId)
            && await auth.GetCurrentUserAsync() is { } user
            && stamp is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Stamp(user)), Encoding.UTF8.GetBytes(stamp)))
        {
            return;
        }

        await auth.LogoutAsync();
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}

/// <summary>A user who still owes the account setup may only finish it (or sign out).</summary>
public sealed class AccountSetupCompleteRequirement : IAuthorizationRequirement;

public sealed class AccountSetupCompleteHandler : AuthorizationHandler<AccountSetupCompleteRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AccountSetupCompleteRequirement requirement)
    {
        if (context.Resource is HttpContext http
            && http.RequestServices.GetRequiredService<IAuthService>() is { IsAuthenticated: true } auth
            && !auth.RequiresPasswordChange
            && !auth.RequiresEmailVerification)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
