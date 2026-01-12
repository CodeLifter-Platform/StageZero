# Complete Setup Guide

This guide walks you through setting up Lifted.BlazorAuth.AspNetIdentity in a Blazor Server application.

## Prerequisites

- .NET 10.0 SDK
- A Blazor Server application
- SQL Server, SQLite, or PostgreSQL database

## Step-by-Step Setup

### 1. Install NuGet Packages

```bash
dotnet add package Lifted.BlazorAuth.AspNetIdentity
dotnet add package Microsoft.EntityFrameworkCore.Sqlite  # or your preferred database provider
dotnet add package Microsoft.EntityFrameworkCore.Tools
```

### 2. Create Your DbContext

Create `Data/ApplicationDbContext.cs`:

```csharp
using Lifted.BlazorAuth.AspNetIdentity.Data;
using Microsoft.EntityFrameworkCore;

namespace YourApp.Data;

public class ApplicationDbContext : IdentityAuthDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Add your application's DbSets here
    // public DbSet<YourEntity> YourEntities => Set<YourEntity>();
}
```

### 3. Configure Program.cs

Replace your `Program.cs` with the following complete configuration:

```csharp
using Lifted.BlazorAuth.AspNetIdentity.Data;
using Lifted.BlazorAuth.AspNetIdentity.Services;
using Lifted.BlazorAuth.AspNetIdentity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using YourApp.Data;
using YourApp.Components;

var builder = WebApplication.CreateBuilder(args);

// Add Blazor Server services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor
builder.Services.AddMudServices();

// Add DbContext with your database provider
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") 
        ?? "Data Source=app.db"));

// Add ASP.NET Core Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Password settings
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;

    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false; // Set to true for production
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

// Configure cookie authentication
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.LoginPath = "/identity/login";
    options.LogoutPath = "/identity/logout";
    options.AccessDeniedPath = "/identity/access-denied";
    options.SlidingExpiration = true;
});

// Add Authentication Services
builder.Services.AddScoped<IAuthService, AuthService>();

// Optional: Add Email Service (implement IEmailService)
// builder.Services.AddScoped<IEmailService, YourEmailService>();

// Add Authorization
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// IMPORTANT: Authentication must come before Authorization
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Lifted.BlazorAuth.AspNetIdentity.Components.Login).Assembly);

app.Run();
```

### 4. Update appsettings.json

Add your connection string:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=app.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### 5. Update App.razor

Make sure your `Components/App.razor` includes authentication:

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link href="https://fonts.googleapis.com/css?family=Roboto:300,400,500,700&display=swap" rel="stylesheet" />
    <link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
    <link rel="stylesheet" href="app.css" />
    <HeadOutlet />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
</body>
</html>
```

### 6. Update Routes.razor

Ensure `Components/Routes.razor` includes authentication:

```razor
<CascadingAuthenticationState>
    <Router AppAssembly="typeof(Program).Assembly" 
            AdditionalAssemblies="new[] { typeof(Lifted.BlazorAuth.AspNetIdentity.Components.Login).Assembly }">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
                <NotAuthorized>
                    @if (context.User.Identity?.IsAuthenticated != true)
                    {
                        <RedirectToLogin />
                    }
                    else
                    {
                        <p>You are not authorized to access this resource.</p>
                    }
                </NotAuthorized>
            </AuthorizeRouteView>
        </Found>
    </Router>
</CascadingAuthenticationState>

@code {
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }
}
```

### 7. Create RedirectToLogin Component (Optional)

Create `Components/RedirectToLogin.razor`:

```razor
@inject NavigationManager Navigation

@code {
    protected override void OnInitialized()
    {
        Navigation.NavigateTo($"/identity/login?returnUrl={Uri.EscapeDataString(Navigation.Uri)}", forceLoad: true);
    }
}
```

### 8. Add _Imports.razor

Update your `Components/_Imports.razor` to include:

```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Authorization
@using Microsoft.JSInterop
@using MudBlazor
@using YourApp
@using YourApp.Components
@using Lifted.BlazorAuth.AspNetIdentity.Components
@using Lifted.BlazorAuth.AspNetIdentity.Models
@using Lifted.BlazorAuth.AspNetIdentity.Services
```

### 9. Run Database Migrations

Create and apply the initial migration:

```bash
dotnet ef migrations add InitialIdentity
dotnet ef database update
```

### 10. Run Your Application

```bash
dotnet run
```

Navigate to:
- `/identity/setup` - Create your first admin account
- `/identity/login` - Login page
- `/identity/register` - Registration page

## Important Middleware Order

The middleware order in `Program.cs` is critical:

```csharp
app.UseHttpsRedirection();      // 1. HTTPS redirect
app.UseStaticFiles();           // 2. Static files
app.UseAuthentication();        // 3. Authentication (MUST be before Authorization)
app.UseAuthorization();         // 4. Authorization
app.UseAntiforgery();          // 5. Antiforgery
app.MapRazorComponents<App>()  // 6. Razor components
```

**Never change this order!** Authentication must come before Authorization.

## Protecting Routes

### Protect a Page

```razor
@page "/admin"
@attribute [Authorize]

<h3>Protected Page</h3>
<p>Only authenticated users can see this.</p>
```

### Protect with Roles

```razor
@page "/admin"
@attribute [Authorize(Roles = "Admin")]

<h3>Admin Only</h3>
```

### Protect with Policies

In `Program.cs`:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdminRole", policy =>
        policy.RequireRole("Admin"));
    options.AddPolicy("RequireEmailVerified", policy =>
        policy.RequireClaim("EmailVerified", "true"));
});
```

In your component:

```razor
@attribute [Authorize(Policy = "RequireAdminRole")]
```

## Using AuthenticationState

### In a Component

```razor
@inject AuthenticationStateProvider AuthenticationStateProvider

@code {
    private async Task CheckAuth()
    {
        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var userName = user.Identity.Name;
            var isAdmin = user.IsInRole("Admin");
        }
    }
}
```

### Using IAuthService

```razor
@inject IAuthService AuthService

@code {
    private async Task GetUserInfo()
    {
        var user = await AuthService.GetCurrentUserAsync();
        if (user != null)
        {
            var fullName = user.FullName;
            var roles = await AuthService.GetUserRolesAsync(user);
        }
    }
}
```

## Troubleshooting

### Issue: "No service for type 'SignInManager' has been registered"

**Solution**: Make sure you called `.AddSignInManager()` after `.AddIdentity()`:

```csharp
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => { ... })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()  // ← Add this
    .AddDefaultTokenProviders();
```

### Issue: "Unable to resolve service for type 'IAuthService'"

**Solution**: Register the service:

```csharp
builder.Services.AddScoped<IAuthService, AuthService>();
```

### Issue: Login redirects to AccessDenied

**Solution**: Check your cookie configuration paths match your routes:

```csharp
options.LoginPath = "/identity/login";
options.AccessDeniedPath = "/identity/access-denied";
```

### Issue: Authentication state not updating

**Solution**: Make sure you have:
1. `AddCascadingAuthenticationState()` in services
2. `<CascadingAuthenticationState>` wrapping your Router in Routes.razor
3. `UseAuthentication()` before `UseAuthorization()` in middleware

### Issue: Components not found

**Solution**: Add the assembly to your Router:

```razor
<Router AppAssembly="typeof(Program).Assembly"
        AdditionalAssemblies="new[] { typeof(Lifted.BlazorAuth.AspNetIdentity.Components.Login).Assembly }">
```

## Next Steps

1. Navigate to `/identity/setup` to create your admin account
2. Customize the Identity options in `Program.cs`
3. Implement `IEmailService` for email functionality
4. Add role-based authorization to your pages
5. Customize the UI components if needed

## Additional Resources

- [ASP.NET Core Identity Documentation](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/identity)
- [MudBlazor Documentation](https://mudblazor.com/)
- [Blazor Authentication Documentation](https://docs.microsoft.com/en-us/aspnet/core/blazor/security/)



