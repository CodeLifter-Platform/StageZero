# Quick Reference Card

## Installation Checklist

- [ ] Install NuGet packages
  ```bash
  dotnet add package Lifted.BlazorAuth.AspNetIdentity
  dotnet add package Microsoft.EntityFrameworkCore.Sqlite
  dotnet add package Microsoft.EntityFrameworkCore.Tools
  ```

- [ ] Create DbContext inheriting from `IdentityAuthDbContext`

- [ ] Configure services in Program.cs
  - [ ] Add Blazor Server services
  - [ ] Add MudBlazor
  - [ ] Add DbContext
  - [ ] Add Identity with `.AddSignInManager()`
  - [ ] Configure cookie authentication
  - [ ] Add IAuthService
  - [ ] Add Authorization
  - [ ] Add CascadingAuthenticationState

- [ ] Configure middleware (ORDER MATTERS!)
  - [ ] UseHttpsRedirection
  - [ ] UseStaticFiles
  - [ ] **UseAuthentication** ← Must be before Authorization
  - [ ] **UseAuthorization**
  - [ ] UseAntiforgery
  - [ ] MapRazorComponents with additional assemblies

- [ ] Update Routes.razor
  - [ ] Wrap Router in `<CascadingAuthenticationState>`
  - [ ] Add library assembly to `AdditionalAssemblies`

- [ ] Run migrations
  ```bash
  dotnet ef migrations add InitialIdentity
  dotnet ef database update
  ```

- [ ] Navigate to `/identity/setup` to create admin account

## Middleware Order (Critical!)

```csharp
app.UseHttpsRedirection();   // 1
app.UseStaticFiles();        // 2
app.UseAuthentication();     // 3 ← MUST BE BEFORE AUTHORIZATION
app.UseAuthorization();      // 4
app.UseAntiforgery();        // 5
app.MapRazorComponents...    // 6
```

## Available Routes

### Public Pages
- `/identity/login` - Login page
- `/identity/register` - Registration
- `/identity/forgot-password` - Request password reset
- `/identity/reset-password?email=...&token=...` - Reset password
- `/identity/two-factor` - 2FA verification
- `/identity/setup` - Initial admin setup (only when no users exist)

### Account Management (Requires Authentication)
- `/identity/account/profile` - User profile and account management

## Common Code Snippets

### Protect a Page
```razor
@attribute [Authorize]
```

### Protect with Role
```razor
@attribute [Authorize(Roles = "Admin")]
```

### Get Current User
```razor
@inject IAuthService AuthService

@code {
    private async Task GetUser()
    {
        var user = await AuthService.GetCurrentUserAsync();
        if (user != null)
        {
            var name = user.FullName;
            var email = user.Email;
        }
    }
}
```

### Check Authentication State
```razor
<AuthorizeView>
    <Authorized>
        <p>Hello, @context.User.Identity?.Name!</p>
    </Authorized>
    <NotAuthorized>
        <p>Please log in.</p>
    </NotAuthorized>
</AuthorizeView>
```

### Check Role
```razor
<AuthorizeView Roles="Admin">
    <Authorized>
        <p>Admin content</p>
    </Authorized>
</AuthorizeView>
```

## IAuthService Methods

### Authentication
- `LoginAsync(email, password, rememberMe)` - Login user
- `LogoutAsync()` - Logout user
- `GetCurrentUserAsync()` - Get current authenticated user

### Registration
- `RegisterAsync(email, password, firstName, lastName)` - Register new user

### Password Management
- `ChangePasswordAsync(user, currentPassword, newPassword)` - Change password
- `ForgotPasswordAsync(email)` - Request password reset
- `ResetPasswordAsync(email, token, newPassword)` - Reset password with token

### Email Verification
- `SendEmailVerificationAsync(user)` - Send verification email
- `ConfirmEmailAsync(user, token)` - Confirm email with token

### Two-Factor Authentication
- `EnableTwoFactorAsync(user)` - Enable 2FA
- `DisableTwoFactorAsync(user)` - Disable 2FA
- `SendTwoFactorCodeAsync(user)` - Send 2FA code
- `VerifyTwoFactorCodeAsync(user, code)` - Verify 2FA code

### Role Management
- `GetUserRolesAsync(user)` - Get user's roles
- `AddToRoleAsync(user, role)` - Add user to role
- `RemoveFromRoleAsync(user, role)` - Remove user from role

## Configuration Options

### Password Requirements
```csharp
options.Password.RequireDigit = true;
options.Password.RequireLowercase = true;
options.Password.RequireUppercase = true;
options.Password.RequireNonAlphanumeric = false;
options.Password.RequiredLength = 8;
```

### Lockout Settings
```csharp
options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
options.Lockout.MaxFailedAccessAttempts = 5;
options.Lockout.AllowedForNewUsers = true;
```

### User Settings
```csharp
options.User.RequireUniqueEmail = true;
options.SignIn.RequireConfirmedEmail = false; // Set true for production
```

### Cookie Settings
```csharp
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.LoginPath = "/identity/login";
    options.AccessDeniedPath = "/identity/access-denied";
    options.SlidingExpiration = true;
});
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "No service for SignInManager" | Add `.AddSignInManager()` after `.AddIdentity()` |
| User not authenticated after login | Add `app.UseAuthentication()` before `app.UseAuthorization()` |
| Components not found | Add library assembly to Router's `AdditionalAssemblies` |
| Authentication state not updating | Wrap Router in `<CascadingAuthenticationState>` |
| Infinite redirect loop | Ensure login path is accessible anonymously |

## Documentation Links

- **[README.md](README.md)** - Overview and quick start
- **[SETUP_GUIDE.md](SETUP_GUIDE.md)** - Complete setup instructions
- **[MIDDLEWARE.md](MIDDLEWARE.md)** - Middleware and authentication flow
- **[COMPONENTS.md](COMPONENTS.md)** - Component reference
- **[DEVELOPMENT.md](DEVELOPMENT.md)** - Development notes

## Support

For issues, questions, or contributions, please refer to the documentation above or create an issue in the repository.

