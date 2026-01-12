# Lifted.BlazorAuth.AspNetIdentity

A comprehensive, production-ready authentication library for Blazor applications using **ASP.NET Core Identity** with beautiful **MudBlazor** UI components.

## 🌟 Features

### Core Authentication
- ✅ **Email/Password Authentication** - Secure login with ASP.NET Core Identity
- ✅ **Email Verification** - Confirm user email addresses
- ✅ **Password Reset** - Secure password recovery flow
- ✅ **Account Lockout** - Automatic lockout after failed login attempts
- ✅ **Password Requirements** - Configurable password complexity rules

### Advanced Features
- ✅ **Two-Factor Authentication (2FA)** - Email-based 2FA support
- ✅ **Role Management** - Assign and manage user roles
- ✅ **Claims-Based Authorization** - Fine-grained access control
- ✅ **Account Management** - Change password, update email
- ✅ **User Profile** - Extended user properties (FirstName, LastName, etc.)
- ✅ **Session Management** - Remember me functionality

### UI Components

#### Public Pages
- ✅ **Login Page** (`/identity/login`) - Beautiful, responsive login form
- ✅ **Registration** (`/identity/register`) - User signup with validation
- ✅ **Forgot Password** (`/identity/forgot-password`) - Password reset request
- ✅ **Reset Password** (`/identity/reset-password`) - Set new password with token
- ✅ **Two-Factor** (`/identity/two-factor`) - 2FA code entry
- ✅ **Account Setup** (`/identity/setup`) - Initial admin account creation

#### Account Management Pages
- ✅ **Profile Page** (`/identity/account/profile`) - Complete user profile management
- ✅ **Change Password** - Secure password update component
- ✅ **Change Email** - Email address update component
- ✅ **Two-Factor Setup** - Enable/disable 2FA component

#### Design
- ✅ **MudBlazor Integration** - Modern, Material Design UI
- ✅ **Responsive Design** - Works on desktop, tablet, and mobile
- ✅ **Consistent Styling** - Professional look and feel throughout

## 📦 Installation

> **📘 Important Documentation:**
> - **[QUICK_REFERENCE.md](QUICK_REFERENCE.md)** - Quick reference card and cheat sheet
> - **[SETUP_GUIDE.md](SETUP_GUIDE.md)** - Complete step-by-step setup instructions
> - **[MIDDLEWARE.md](MIDDLEWARE.md)** - Understanding authentication middleware and flow
> - **[COMPONENTS.md](COMPONENTS.md)** - Detailed component reference

> **⚠️ Critical**: This library requires proper middleware configuration. Authentication will not work without `UseAuthentication()` and `UseAuthorization()` middleware. See [MIDDLEWARE.md](MIDDLEWARE.md) for details.

### Quick Start

### 1. Install the NuGet Package

```bash
dotnet add package Lifted.BlazorAuth.AspNetIdentity
dotnet add package Microsoft.EntityFrameworkCore.Sqlite  # or your preferred database
dotnet add package Microsoft.EntityFrameworkCore.Tools
```

### 2. Create Your DbContext

Your application's DbContext should inherit from `IdentityAuthDbContext`:

```csharp
using Lifted.BlazorAuth.AspNetIdentity.Data;
using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : IdentityAuthDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Add your application's DbSets here
    public DbSet<YourEntity> YourEntities => Set<YourEntity>();
}
```

### 3. Configure Services in Program.cs

```csharp
using Lifted.BlazorAuth.AspNetIdentity.Data;
using Lifted.BlazorAuth.AspNetIdentity.Services;
using Lifted.BlazorAuth.AspNetIdentity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor
builder.Services.AddMudServices();

// Add DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=app.db"));

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
.AddSignInManager()  // Required for cookie authentication
.AddDefaultTokenProviders();

// Configure cookie authentication
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.LoginPath = "/identity/login";
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

// Configure middleware pipeline (ORDER MATTERS!)
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();  // Must come before Authorization
app.UseAuthorization();
app.UseAntiforgery();

// Map Blazor components
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Lifted.BlazorAuth.AspNetIdentity.Components.Login).Assembly);

app.Run();
```

**⚠️ Important**: The middleware order is critical! `UseAuthentication()` must come before `UseAuthorization()`.

### 4. Update Routes.razor

Ensure your `Components/Routes.razor` includes the authentication library's assembly:

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
```

### 5. Run Migrations

```bash
dotnet ef migrations add InitialIdentity
dotnet ef database update
```

## 🚀 Usage

### Initial Setup

After installation, navigate to `/identity/setup` to create your first administrator account. This page will only be accessible when no users exist in the database.

### Using the Pre-Built Pages

The library includes complete, ready-to-use authentication pages:

- **Login**: `/identity/login`
- **Register**: `/identity/register`
- **Forgot Password**: `/identity/forgot-password`
- **Reset Password**: `/identity/reset-password` (with token from email)
- **Two-Factor**: `/identity/two-factor`
- **Profile**: `/identity/account/profile`

These pages are automatically available once you install the package. No additional configuration needed!

### Using Components Directly

You can also use the components directly in your own pages:

```razor
@page "/login"
@using Lifted.BlazorAuth.AspNetIdentity.Components

<Login />
```

Or embed account management components:

```razor
@page "/my-account"
@using Lifted.BlazorAuth.AspNetIdentity.Components.Account

<MudContainer>
    <ChangePassword />
    <ChangeEmail />
    <TwoFactorSetup />
</MudContainer>
```

### Protect Routes

```razor
@using Microsoft.AspNetCore.Authorization

@attribute [Authorize]

<h3>Protected Content</h3>
<p>Only authenticated users can see this.</p>
```

### Role-Based Authorization

```razor
@attribute [Authorize(Roles = "Admin")]

<h3>Admin Only</h3>
```

### Use Auth Service in Code

```csharp
@inject IAuthService AuthService

@code {
    private async Task DoSomething()
    {
        var user = await AuthService.GetCurrentUserAsync();
        if (user != null)
        {
            // User is logged in
            var roles = await AuthService.GetUserRolesAsync(user);
        }
    }
}
```

## 📚 API Reference

### IAuthService Methods

- `LoginAsync(email, password, rememberMe)` - Authenticate user
- `LogoutAsync()` - Sign out current user
- `GetCurrentUserAsync()` - Get currently logged-in user
- `ChangePasswordAsync(currentPassword, newPassword)` - Change password
- `SendEmailVerificationCodeAsync(email)` - Send verification email
- `VerifyEmailAsync(userId, token)` - Confirm email address
- `SendPasswordResetTokenAsync(email)` - Send password reset email
- `ResetPasswordAsync(email, token, newPassword)` - Reset password
- `CreateUserAsync(user, password)` - Create new user
- `AddToRoleAsync(user, role)` - Add user to role
- `RemoveFromRoleAsync(user, role)` - Remove user from role
- `GetUserRolesAsync(user)` - Get user's roles
- `IsInRoleAsync(user, role)` - Check if user has role
- `EnableTwoFactorAsync(user)` - Enable 2FA
- `DisableTwoFactorAsync(user)` - Disable 2FA
- `GenerateTwoFactorTokenAsync(user)` - Generate 2FA token
- `VerifyTwoFactorTokenAsync(user, token)` - Verify 2FA token

## 🎨 Customization

### Custom Email Service

Implement `IEmailService` to send emails:

```csharp
public class MyEmailService : IEmailService
{
    public async Task SendEmailVerificationAsync(string email, string token)
    {
        // Send email with verification link
    }

    public async Task SendPasswordResetAsync(string email, string token)
    {
        // Send password reset email
    }

    public async Task SendTwoFactorCodeAsync(string email, string code)
    {
        // Send 2FA code
    }
}
```

Register it:

```csharp
builder.Services.AddScoped<IEmailService, MyEmailService>();
```

## 📄 License

MIT License - feel free to use in commercial projects!

## 🤝 Contributing

Contributions welcome! This is part of the Lifted framework for building production-ready Blazor applications.

## 🔗 Related Packages

- **Lifted.BlazorAuth.Basic** - Simpler authentication without ASP.NET Core Identity
- **MudBlazor** - Material Design components for Blazor

## 📞 Support

For issues and questions, please open an issue on GitHub.

