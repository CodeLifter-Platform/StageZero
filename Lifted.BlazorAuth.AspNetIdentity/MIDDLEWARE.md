# Authentication Middleware and Flow

This document explains how authentication works in Lifted.BlazorAuth.AspNetIdentity and the role of middleware.

## Overview

Lifted.BlazorAuth.AspNetIdentity is designed for **Blazor Server** applications and uses **ASP.NET Core Identity** with **cookie-based authentication**. This requires proper middleware configuration in the host application.

## Why Middleware is Required

### Cookie Authentication
When a user logs in, ASP.NET Core Identity creates an authentication cookie that is sent to the browser. On subsequent requests, this cookie is used to authenticate the user. The middleware is responsible for:

1. **Reading the authentication cookie** from incoming requests
2. **Validating the cookie** and creating a ClaimsPrincipal
3. **Setting HttpContext.User** with the authenticated user
4. **Managing cookie expiration** and sliding expiration
5. **Handling logout** by clearing the cookie

### SignInManager Dependency
The `AuthService` uses `SignInManager<ApplicationUser>` which requires:
- **HttpContext** - Available in Blazor Server
- **Cookie authentication middleware** - To persist authentication state
- **Proper middleware order** - Authentication before Authorization

## Middleware Pipeline

### Required Middleware Order

```csharp
var app = builder.Build();

// 1. HTTPS Redirection (optional but recommended)
app.UseHttpsRedirection();

// 2. Static Files (serves CSS, JS, images)
app.UseStaticFiles();

// 3. AUTHENTICATION (reads cookies, sets HttpContext.User)
app.UseAuthentication();  // ← CRITICAL: Must come before Authorization

// 4. AUTHORIZATION (checks if user has permission)
app.UseAuthorization();

// 5. Antiforgery (CSRF protection)
app.UseAntiforgery();

// 6. Razor Components (Blazor)
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Login).Assembly);

app.Run();
```

### Why Order Matters

**Authentication MUST come before Authorization** because:
- `UseAuthentication()` reads the cookie and sets `HttpContext.User`
- `UseAuthorization()` checks `HttpContext.User` to determine permissions
- If Authorization runs first, `HttpContext.User` will be null/anonymous

## Authentication Flow

### Login Flow

```
User submits login form
    ↓
Login.razor calls AuthService.LoginAsync()
    ↓
AuthService calls SignInManager.PasswordSignInAsync()
    ↓
SignInManager validates credentials
    ↓
SignInManager creates authentication cookie
    ↓
Cookie is sent to browser
    ↓
Browser includes cookie in subsequent requests
    ↓
UseAuthentication() middleware reads cookie
    ↓
HttpContext.User is populated
    ↓
User is authenticated
```

### Protected Page Access Flow

```
User navigates to protected page
    ↓
Browser sends request with authentication cookie
    ↓
UseAuthentication() middleware reads cookie
    ↓
HttpContext.User is set from cookie claims
    ↓
UseAuthorization() middleware checks [Authorize] attribute
    ↓
If authenticated: Page renders
If not authenticated: Redirect to login
```

### Logout Flow

```
User clicks logout
    ↓
Component calls AuthService.LogoutAsync()
    ↓
AuthService calls SignInManager.SignOutAsync()
    ↓
SignInManager clears authentication cookie
    ↓
Browser receives response with cleared cookie
    ↓
Subsequent requests are unauthenticated
```

## Service Configuration

### Required Services

```csharp
// 1. Identity with Entity Framework
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => { ... })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()  // ← Required for cookie auth
    .AddDefaultTokenProviders();

// 2. Cookie configuration
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;  // Prevent JavaScript access
    options.ExpireTimeSpan = TimeSpan.FromDays(7);  // Cookie lifetime
    options.LoginPath = "/identity/login";  // Redirect path for unauthenticated users
    options.AccessDeniedPath = "/identity/access-denied";  // Redirect for unauthorized
    options.SlidingExpiration = true;  // Extend cookie on activity
});

// 3. Auth service
builder.Services.AddScoped<IAuthService, AuthService>();

// 4. Authorization
builder.Services.AddAuthorization();

// 5. Cascading authentication state (for Blazor)
builder.Services.AddCascadingAuthenticationState();
```

## How It Works in Blazor Server

### Server-Side Rendering
Blazor Server maintains a SignalR connection between the browser and server. The authentication cookie is sent with:
1. Initial HTTP request (page load)
2. SignalR connection establishment
3. All subsequent SignalR messages

### HttpContext Access
Unlike Blazor WebAssembly, Blazor Server has access to `HttpContext` because:
- The application runs on the server
- Each circuit has an associated HttpContext
- SignInManager can read/write cookies via HttpContext

### Authentication State
The authentication state is:
1. **Established** by the cookie middleware
2. **Cascaded** to components via `CascadingAuthenticationState`
3. **Accessible** via `AuthenticationStateProvider` or `[CascadingParameter] Task<AuthenticationState>`

## Common Issues and Solutions

### Issue: "No service for type 'SignInManager'"
**Cause**: Missing `.AddSignInManager()` call  
**Solution**: Add it after `.AddIdentity()`:
```csharp
.AddIdentity<ApplicationUser, IdentityRole>(...)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()  // ← Add this
```

### Issue: User not authenticated after login
**Cause**: Missing `UseAuthentication()` middleware  
**Solution**: Add it before `UseAuthorization()`:
```csharp
app.UseAuthentication();
app.UseAuthorization();
```

### Issue: Infinite redirect loop
**Cause**: LoginPath points to a protected route  
**Solution**: Ensure login page is accessible anonymously:
```csharp
options.LoginPath = "/identity/login";  // This route must allow anonymous access
```

### Issue: Authentication state not updating in UI
**Cause**: Missing `CascadingAuthenticationState`  
**Solution**: Wrap Router in Routes.razor:
```razor
<CascadingAuthenticationState>
    <Router ...>
    </Router>
</CascadingAuthenticationState>
```

## Security Considerations

### Cookie Security
The authentication cookie is configured with:
- `HttpOnly = true` - Prevents JavaScript access (XSS protection)
- `Secure = true` (in production) - Only sent over HTTPS
- `SameSite = Lax` - CSRF protection

### HTTPS
Always use HTTPS in production:
```csharp
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();
```

### Antiforgery
Blazor Server includes antiforgery protection:
```csharp
app.UseAntiforgery();
```

## Summary

The middleware pipeline is essential for authentication to work. Key points:

1. ✅ **Use `UseAuthentication()` before `UseAuthorization()`**
2. ✅ **Configure cookie authentication** with `ConfigureApplicationCookie()`
3. ✅ **Add SignInManager** with `.AddSignInManager()`
4. ✅ **Use CascadingAuthenticationState** in Blazor components
5. ✅ **Map additional assemblies** in Router for library components

Without proper middleware configuration, authentication will not work!

