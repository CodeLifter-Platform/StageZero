# Reverse Proxy Library - Installation Guide

Project Name: StageZero
Library Namespace: StageZero.ReverseProxy

## Step 1: Create the Class Library Project

`ash
dotnet new classlib -n StageZero.ReverseProxy
dotnet sln add StageZero.ReverseProxy
dotnet add StageZero reference StageZero.ReverseProxy
`

## Step 2: Files Already Created

This script has created:
- StageZero.ReverseProxy/ - All library files with correct namespaces
- BlazorUIPages/ - Razor pages ready to copy

## Step 3: Copy UI Pages

Copy the Razor files from BlazorUIPages/ to your Blazor project:
`
BlazorUIPages/ProxyHosts.razor     → StageZero/Pages/Admin/
BlazorUIPages/ProxyHostEdit.razor  → StageZero/Pages/Admin/
`

## Step 4: Update Your Blazor Project

Add package references to your StageZero.csproj:
`xml
<ItemGroup>
  <PackageReference Include="Yarp.ReverseProxy" Version="2.1.0" />
  <PackageReference Include="Certes" Version="3.0.0" />
</ItemGroup>
`

## Step 5: Update Your DbContext

Add to your existing DbContext class:
`csharp
using StageZero.ReverseProxy.Models;

public class YourDbContext : DbContext
{
    // Add this:
    public DbSet<ProxyHost> ProxyHosts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Add this:
        modelBuilder.Entity<ProxyHost>(entity =>
        {
            entity.HasIndex(e => e.DomainName).IsUnique();
            entity.HasIndex(e => e.IsEnabled);
        });
    }
}
`

## Step 6: Update Program.cs

`csharp
using StageZero.ReverseProxy.Extensions;
using StageZero.ReverseProxy.Services;

// Register DbContext as base class
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<YourDbContext>());

// Register config provider as singleton
builder.Services.AddSingleton<DatabaseProxyConfigProvider>();

// Add reverse proxy management
builder.Services.AddReverseProxyManagement();

// After app.Build(), initialize config
using (var scope = app.Services.CreateScope())
{
    var proxyConfigService = scope.ServiceProvider.GetRequiredService<IProxyConfigurationService>();
    await proxyConfigService.ReloadConfigurationAsync();
}

// Add BEFORE MapBlazorHub
app.MapReverseProxy();
`

## Step 7: Create and Run Migration

`ash
dotnet ef migrations add AddProxyHosts
dotnet ef database update
`

## Step 8: Run and Test

Navigate to: /admin/proxy-hosts
