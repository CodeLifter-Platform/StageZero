# Lifted.BlazorAuth.Basic

A simple, ready-to-use authentication library for Blazor applications with beautiful MudBlazor UI components. Get up and running with email/password authentication in minutes!

## ✨ Features

- 🔐 **Email/Password Authentication** - Secure authentication with BCrypt password hashing
- ✉️ **Email Verification** - 6-digit verification codes for new accounts
- 🔑 **Password Reset** - Secure password reset workflow with email codes
- 👤 **Account Setup** - Guided setup flow for new users
- 🎨 **MudBlazor UI** - Beautiful, responsive UI components out of the box
- 🗄️ **Entity Framework Core** - Built-in database integration
- 🔒 **Route Protection** - Simple `<RequireAuth>` component to protect pages
- 🧩 **Extensible** - Easy to customize and extend for your needs

## 📦 Installation

Install the package via NuGet:

```bash
dotnet add package Lifted.BlazorAuth.Basic
```

Or via Package Manager Console:

```powershell
Install-Package Lifted.BlazorAuth.Basic
```

## 🚀 Quick Start

### 1. Create Your DbContext

Inherit from `BasicAuthDbContext` to get the User table:

```csharp
using Lifted.BlazorAuth.Basic.Data;
using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : BasicAuthDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
    
    // Add your own DbSets here
    // public DbSet<YourEntity> YourEntities { get; set; }
}
```

### 2. Implement IEmailService

Create an email service to send verification and reset codes:

```csharp
using Lifted.BlazorAuth.Basic.Services;

public class EmailService : IEmailService
{
    public async Task SendVerificationCodeAsync(string toEmail, string code)
    {
        // Send verification email with the code
        // Use your preferred email provider (SendGrid, SMTP, etc.)
    }
    
    public async Task SendPasswordResetCodeAsync(string toEmail, string code)
    {
        // Send password reset email with the code
    }
    
    public async Task<bool> IsConfiguredAsync()
    {
        // Return true if email is configured, false otherwise
        return true;
    }
}
```

### 3. Configure Services in Program.cs

```csharp
using Lifted.BlazorAuth.Basic.Data;
using Lifted.BlazorAuth.Basic.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor
builder.Services.AddMudServices();

// Add DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=app.db"));

// Register BasicAuthDbContext for the library
builder.Services.AddScoped<BasicAuthDbContext>(sp => 
    sp.GetRequiredService<ApplicationDbContext>());

// Register Email Service
builder.Services.AddScoped<IEmailService, EmailService>();

// Register Auth Service
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserReader, UserReader>();
builder.Services.AddScoped<IUserWriter, UserWriter>();

var app = builder.Build();

// Run migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.Run();
```

### 4. Add to _Imports.razor

```razor
@using Lifted.BlazorAuth.Basic.Components
@using Lifted.BlazorAuth.Basic.Services
@using MudBlazor
```

### 5. Update App.razor or Routes.razor

Make sure component discovery includes the library:

```razor
<Router AppAssembly="@typeof(App).Assembly" 
        AdditionalAssemblies="new[] { typeof(Lifted.BlazorAuth.Basic.Components.Login).Assembly }">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
    </Found>
</Router>
```

### 6. Add MudBlazor to MainLayout.razor

```razor
<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

@Body
```

### 7. Protect Your Pages

Use the `<RequireAuth>` component to protect pages:

```razor
@page "/dashboard"

<RequireAuth>
    <h1>Protected Dashboard</h1>
    <p>Only authenticated users can see this!</p>
</RequireAuth>
```

## 📄 Available Pages

The library provides these routes out of the box:

- `/login` - Login page
- `/setup` - Initial admin account setup (only shown if no users exist)
- `/forgot-password` - Request password reset
- `/reset-password` - Reset password with code

## 🗄️ Database Schema

The library creates a `Users` table with these fields:

- `Id` (int, primary key)
- `Email` (string, unique)
- `PasswordHash` (string)
- `IsEmailVerified` (bool)
- `EmailVerificationCode` (string, nullable)
- `EmailVerificationCodeExpiry` (DateTime, nullable)
- `PasswordResetCode` (string, nullable)
- `PasswordResetCodeExpiry` (DateTime, nullable)
- `CreatedAt` (DateTime)
- `UpdatedAt` (DateTime)

## 🔧 Advanced Configuration

### Custom User Properties

Extend the `User` class to add custom properties:

```csharp
// This is not directly supported in v0.0.1
// Consider creating a separate UserProfile table linked to User.Id
```

### Customize UI Components

The components use MudBlazor, so you can customize the theme in your app.

### Using a Different UI Framework

**Don't want to use MudBlazor?** No problem! The library's core services (`IAuthService`, `IUserReader`, `IUserWriter`) are completely UI-agnostic. You can build your own UI components using any Blazor UI framework (Radzen, Ant Design, FluentUI, or plain Bootstrap) and simply consume the same services:

```csharp
@inject IAuthService AuthService
@inject NavigationManager Navigation

<YourCustomLoginForm>
    <button @onclick="HandleLogin">Login</button>
</YourCustomLoginForm>

@code {
    private async Task HandleLogin()
    {
        var result = await AuthService.LoginAsync(email, password);
        if (result.Success)
        {
            Navigation.NavigateTo("/");
        }
    }
}
```

The included MudBlazor components are just a reference implementation - feel free to replace them entirely with your own UI while keeping all the authentication logic, database models, and services!

## 📝 License

MIT License - feel free to use in personal and commercial projects!

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

## 📧 Support

For issues and questions, please use the GitHub issue tracker.

