# NuGet Publishing Quick Reference

## 🚀 Quick Publish Checklist

### First Time Setup (One-time)
- [ ] Create NuGet.org account and API key
- [ ] Add `NUGET_API_KEY` to GitHub repository secrets
- [ ] Verify `.github/workflows/basic-auth-nuget-publish.yml` exists
- [ ] Push code to GitHub

### Publishing a New Version

```bash
# 1. Update version in .csproj
# Edit: Lifted.BlazorAuth.Basic/Lifted.BlazorAuth.Basic.csproj
# Change: <Version>X.Y.Z</Version>

# 2. Commit and push
git add Lifted.BlazorAuth.Basic/Lifted.BlazorAuth.Basic.csproj
git commit -m "Bump version to X.Y.Z"
git push

# 3. Create and push tag
git tag vX.Y.Z
git push origin vX.Y.Z

# 4. Watch it publish!
# Go to: https://github.com/YOUR_USERNAME/YOUR_REPO/actions
```

## 📋 Version Numbering Guide

Use [Semantic Versioning](https://semver.org/): `MAJOR.MINOR.PATCH`

- **MAJOR** (1.0.0): Breaking changes
- **MINOR** (0.1.0): New features, backward compatible
- **PATCH** (0.0.1): Bug fixes, backward compatible

### Examples:
- `0.0.1` → `0.0.2`: Bug fix
- `0.0.2` → `0.1.0`: New feature added
- `0.1.0` → `1.0.0`: First stable release or breaking change

## 🔍 Workflow Status

Check workflow status at:
```
https://github.com/YOUR_USERNAME/YOUR_REPO/actions
```

## ⚡ Common Commands

### Build locally
```bash
dotnet build Lifted.BlazorAuth.Basic/Lifted.BlazorAuth.Basic.csproj -c Release
```

### Pack locally
```bash
dotnet pack Lifted.BlazorAuth.Basic/Lifted.BlazorAuth.Basic.csproj -c Release -o ./nupkgs
```

### Publish manually
```bash
dotnet nuget push ./nupkgs/Lifted.BlazorAuth.Basic.*.nupkg \
  --api-key YOUR_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

### List tags
```bash
git tag -l
```

### Delete a tag (if you made a mistake)
```bash
# Delete locally
git tag -d vX.Y.Z

# Delete remotely
git push origin :refs/tags/vX.Y.Z
```

## 🎯 What Gets Published Where

| Action | NuGet.org | GitHub Packages |
|--------|-----------|-----------------|
| Push to main/master | ❌ | ✅ |
| Create version tag | ✅ | ✅ |
| Pull request | ❌ | ❌ |

## 🐛 Troubleshooting

### Package already exists
- Increment version number
- NuGet.org doesn't allow overwriting

### 401 Unauthorized
- Check `NUGET_API_KEY` secret
- Verify API key hasn't expired

### Tag doesn't trigger workflow
- Use format: `v1.2.3` (with 'v' prefix)
- Push tag: `git push origin v1.2.3`

## 📚 Full Documentation

See [GITHUB_ACTIONS_SETUP.md](../../GITHUB_ACTIONS_SETUP.md) for complete setup guide.

