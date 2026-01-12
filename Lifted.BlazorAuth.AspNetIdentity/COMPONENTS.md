# Component Reference

This document provides a comprehensive reference for all UI components included in Lifted.BlazorAuth.AspNetIdentity.

## Public Authentication Pages

### Login (`/identity/login`)
**File**: `Components/Login.razor`

Full-featured login page with:
- Email and password inputs
- Remember me checkbox
- Loading states and error handling
- Automatic redirect to return URL
- Two-factor authentication redirect
- Account lockout handling
- Link to registration and password reset

**Query Parameters**:
- `returnUrl` - URL to redirect to after successful login

**Usage**:
```razor
<Login />
```

---

### Register (`/identity/register`)
**File**: `Components/Register.razor`

User registration page with:
- First name and last name fields
- Email and password inputs
- Password confirmation
- Client-side validation
- Automatic email verification trigger
- Redirect to login after success

**Query Parameters**:
- `returnUrl` - URL to redirect to after registration (optional)

**Usage**:
```razor
<Register />
```

---

### Forgot Password (`/identity/forgot-password`)
**File**: `Components/ForgotPassword.razor`

Password reset request page with:
- Email input
- Security-conscious messaging (doesn't reveal if email exists)
- Success confirmation
- Resend option
- Link back to login

**Usage**:
```razor
<ForgotPassword />
```

---

### Reset Password (`/identity/reset-password`)
**File**: `Components/ResetPassword.razor`

Password reset page with token validation:
- Email and token from query parameters
- New password input with confirmation
- Password strength requirements
- Success message with login redirect
- Token expiration handling

**Query Parameters**:
- `email` - User's email address
- `token` - Password reset token from email

**Usage**:
```razor
<ResetPassword />
```

---

### Two-Factor Authentication (`/identity/two-factor`)
**File**: `Components/TwoFactor.razor`

Two-factor authentication verification page:
- 6-digit code input
- Remember device option
- Resend code functionality
- Return URL support
- Session expiration handling

**Query Parameters**:
- `returnUrl` - URL to redirect to after verification
- `email` - User's email (for session recovery)

**Usage**:
```razor
<TwoFactor />
```

---

### Account Setup (`/identity/setup`)
**File**: `Components/AccountSetup.razor`

Initial administrator account creation:
- First-time setup detection
- Admin user creation form
- Automatic Admin role assignment
- Email auto-confirmation for admin
- Prevents access when users already exist

**Usage**:
```razor
<AccountSetup />
```

---

## Account Management Components

### Profile Page (`/identity/account/profile`)
**File**: `Components/Account/Profile.razor`

Complete user profile management page:
- Profile information editing (first/last name)
- Account creation and last login display
- Integrated security settings
- Role display
- Embedded ChangePassword, ChangeEmail, and TwoFactorSetup components

**Usage**:
```razor
<Profile />
```

---

### Change Password
**File**: `Components/Account/ChangePassword.razor`

Standalone password change component:
- Current password verification
- New password with confirmation
- Password strength requirements
- Inline error handling
- Success notification

**Usage**:
```razor
<ChangePassword />
```

---

### Change Email
**File**: `Components/Account/ChangeEmail.razor`

Email address update component:
- Current email display
- New email input
- Password verification (placeholder)
- Email verification trigger
- Success notification

**Usage**:
```razor
<ChangeEmail />
```

---

### Two-Factor Setup
**File**: `Components/Account/TwoFactorSetup.razor`

Two-factor authentication management:
- Current 2FA status display
- Enable/disable 2FA buttons
- Security warnings
- Success/error notifications

**Usage**:
```razor
<TwoFactorSetup />
```

---

## Component Dependencies

All components require:
- `IAuthService` - Injected authentication service
- `ISnackbar` - MudBlazor snackbar for notifications
- `NavigationManager` - For redirects

Some components also require:
- `UserManager<ApplicationUser>` - For direct user management
- `RoleManager<IdentityRole>` - For role management (AccountSetup)

## Styling and Theming

All components use MudBlazor components and follow Material Design principles. They automatically adapt to your MudBlazor theme configuration.

### Common MudBlazor Components Used:
- `MudContainer` - Page containers
- `MudPaper` - Card-like containers
- `MudTextField` - Input fields
- `MudButton` - Buttons
- `MudAlert` - Error/success messages
- `MudProgressCircular` - Loading indicators
- `MudIcon` - Icons
- `MudText` - Typography

## Customization

To customize components:

1. **Copy the component** to your project
2. **Modify as needed** - Change styling, add fields, etc.
3. **Use your custom component** instead of the library version

Example:
```razor
@* Your custom login page *@
@page "/custom-login"
@using Lifted.BlazorAuth.AspNetIdentity.Services

@* Your custom implementation using IAuthService *@
```

