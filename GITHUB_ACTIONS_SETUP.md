# GitHub Actions NuGet Publishing Setup Guide

This guide walks you through setting up automated NuGet package publishing for Lifted.BlazorAuth.Basic using GitHub Actions.

## 📋 Overview

The workflow automatically:
- **Builds** the project on every push/PR
- **Tests** the code (if tests exist)
- **Creates** NuGet packages
- **Publishes** to NuGet.org when you create version tags
- **Publishes** to GitHub Packages on main/master branch

## 🔧 Step-by-Step Setup

### Step 1: Get a NuGet.org API Key

1. Go to [https://www.nuget.org/](https://www.nuget.org/)
2. Sign in (or create an account if you don't have one)
3. Click your username → **API Keys**
4. Click **Create** to make a new API key
5. Configure the key:
   - **Key Name**: `GitHub Actions - Lifted.BlazorAuth.Basic`
   - **Expiration**: Choose your preferred duration (365 days recommended)
   - **Scopes**: Select **Push** and **Push new packages and package versions**
   - **Glob Pattern**: `Lifted.BlazorAuth.Basic` (or `*` for all packages)
6. Click **Create**
7. **IMPORTANT**: Copy the API key immediately - you won't see it again!

### Step 2: Add API Key to GitHub Secrets

1. Go to your GitHub repository
2. Click **Settings** (top menu)
3. In the left sidebar, click **Secrets and variables** → **Actions**
4. Click **New repository secret**
5. Enter:
   - **Name**: `NUGET_API_KEY`
   - **Secret**: Paste your NuGet.org API key
6. Click **Add secret**

### Step 3: Verify Workflow File

The workflow file should already exist at `.github/workflows/basic-auth-nuget-publish.yml`. Verify it's in your repository:

```bash
ls -la .github/workflows/basic-auth-nuget-publish.yml
```

If it doesn't exist, create it from the template in this repository.

### Step 4: Push to GitHub

If you haven't already, push your code to GitHub:

```bash
git add .
git commit -m "Add GitHub Actions workflow for NuGet publishing"
git push origin main
```

### Step 5: Test the Workflow

The workflow will run automatically on push. Check it:

1. Go to your repository on GitHub
2. Click the **Actions** tab
3. You should see a workflow run in progress
4. Click on it to see the build logs

## 🚀 Publishing a New Version

### Option 1: Publish via Git Tag (Recommended)

This publishes to both NuGet.org and GitHub Packages:

```bash
# 1. Update version in Lifted.BlazorAuth.Basic/Lifted.BlazorAuth.Basic.csproj
# Change: <Version>0.0.2</Version>

# 2. Commit the version change
git add Lifted.BlazorAuth.Basic/Lifted.BlazorAuth.Basic.csproj
git commit -m "Bump version to 0.0.2"
git push

# 3. Create and push a version tag
git tag v0.0.2
git push origin v0.0.2
```

The workflow will automatically:
- Build the project
- Create the NuGet package
- Publish to NuGet.org
- Publish to GitHub Packages

### Option 2: Manual Trigger

You can also manually trigger the workflow:

1. Go to **Actions** tab in GitHub
2. Click **Build and Publish NuGet Package** workflow
3. Click **Run workflow**
4. Select the branch
5. Click **Run workflow**

Note: Manual triggers only build and test - they don't publish unless on a tagged commit.

## 📦 Workflow Triggers

The workflow runs on:

| Trigger | Build | Test | Publish to NuGet.org | Publish to GitHub Packages |
|---------|-------|------|---------------------|---------------------------|
| Push to main/master | ✅ | ✅ | ❌ | ✅ |
| Pull Request | ✅ | ✅ | ❌ | ❌ |
| Version Tag (v*.*.*) | ✅ | ✅ | ✅ | ✅ |
| Manual Trigger | ✅ | ✅ | Only if on tag | Only if on main/master |

## 🔍 Monitoring Workflow Runs

1. Go to your repository's **Actions** tab
2. Click on a workflow run to see details
3. Click on individual jobs to see logs
4. Check for errors in the build, test, or publish steps

## ❌ Troubleshooting

### "401 Unauthorized" when publishing to NuGet.org

- Check that `NUGET_API_KEY` secret is set correctly
- Verify the API key hasn't expired
- Ensure the API key has "Push" permissions

### "409 Conflict" when publishing

- The package version already exists on NuGet.org
- Increment the version number in `.csproj`
- NuGet.org doesn't allow overwriting existing versions

### Workflow doesn't trigger on tag

- Ensure tag format is `v*.*.*` (e.g., `v0.0.1`, `v1.2.3`)
- Check that you pushed the tag: `git push origin v0.0.1`

### .NET 10 not found

- The workflow uses .NET 10 (net10.0)
- If .NET 10 isn't available yet, update `DOTNET_VERSION` in the workflow to `8.0.x` or `9.0.x`
- Also update `<TargetFramework>` in the `.csproj` file

## 🔐 Security Best Practices

1. **Never commit API keys** to your repository
2. **Use repository secrets** for sensitive data
3. **Limit API key scope** to only what's needed
4. **Set API key expiration** and rotate regularly
5. **Review workflow permissions** in the YAML file

## 📚 Additional Resources

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [NuGet.org Publishing Guide](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package)
- [GitHub Packages NuGet Guide](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry)

## 🎉 Success!

Once set up, your workflow will automatically build and publish your NuGet package whenever you create a new version tag. No manual steps required!

