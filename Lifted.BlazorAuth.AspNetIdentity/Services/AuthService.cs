using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Lifted.BlazorAuth.AspNetIdentity.Models;

namespace Lifted.BlazorAuth.AspNetIdentity.Services;

// ═══════════════════════════════════════════════════════════════
// EXCEPTIONS
// ═══════════════════════════════════════════════════════════════

public class AuthServiceException : Exception
{
    public AuthServiceException(string message) : base(message) { }
    public AuthServiceException(string message, Exception innerException) : base(message, innerException) { }
}

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, bool rememberMe = false);
    Task LogoutAsync();
    Task<ApplicationUser?> GetCurrentUserAsync();
    Task<bool> ChangePasswordAsync(string currentPassword, string newPassword);
    Task<bool> SendEmailVerificationCodeAsync(string email);
    Task<bool> VerifyEmailAsync(string userId, string token);
    Task<bool> UpdateEmailAsync(string newEmail);
    Task<bool> SendPasswordResetTokenAsync(string email);
    Task<bool> ResetPasswordAsync(string email, string token, string newPassword);
    Task<IdentityResult> CreateUserAsync(ApplicationUser user, string password);
    Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role);
    Task<IdentityResult> RemoveFromRoleAsync(ApplicationUser user, string role);
    Task<IList<string>> GetUserRolesAsync(ApplicationUser user);
    Task<bool> IsInRoleAsync(ApplicationUser user, string role);
    Task<bool> EnableTwoFactorAsync(ApplicationUser user);
    Task<bool> DisableTwoFactorAsync(ApplicationUser user);
    Task<string> GenerateTwoFactorTokenAsync(ApplicationUser user);
    Task<bool> VerifyTwoFactorTokenAsync(ApplicationUser user, string token);
    bool IsAuthenticated { get; }
    bool RequiresPasswordChange { get; }
    bool RequiresEmailVerification { get; }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<AuthService> _logger;
    private readonly IEmailService? _emailService;
    private ApplicationUser? _currentUser;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<AuthService> logger,
        IEmailService? emailService = null)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
        _emailService = emailService;
    }

    public bool IsAuthenticated => _currentUser != null;
    public bool RequiresPasswordChange => _currentUser?.RequiresPasswordChange ?? false;
    public bool RequiresEmailVerification => _currentUser != null && !_currentUser.EmailConfirmed;

    public async Task<AuthResult> LoginAsync(string email, string password, bool rememberMe = false)
    {
        try
        {
            _logger.LogDebug("Login attempt for user {Email}", email);

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                _logger.LogWarning("Login failed: user {Email} not found", email);
                return AuthResult.Failed("Invalid email or password");
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Login failed: user {Email} is inactive", email);
                return AuthResult.Failed("Account is inactive");
            }

            var result = await _signInManager.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);

                _currentUser = user;
                _logger.LogInformation("User {Email} logged in successfully", email);
                return AuthResult.Succeeded(user);
            }

            if (result.RequiresTwoFactor)
            {
                _logger.LogInformation("User {Email} requires two-factor authentication", email);
                return AuthResult.TwoFactorRequired();
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("User {Email} is locked out", email);
                return AuthResult.LockedOut();
            }

            if (result.IsNotAllowed)
            {
                _logger.LogWarning("User {Email} is not allowed to sign in", email);
                return AuthResult.NotAllowed();
            }

            _logger.LogWarning("Login failed for user {Email}", email);
            return AuthResult.Failed("Invalid email or password");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for user {Email}", email);
            throw new AuthServiceException("Could not complete login", ex);
        }
    }

    public async Task LogoutAsync()
    {
        if (_currentUser != null)
        {
            _logger.LogInformation("User {Email} logged out", _currentUser.Email);
            await _signInManager.SignOutAsync();
            _currentUser = null;
        }
    }

    public Task<ApplicationUser?> GetCurrentUserAsync()
    {
        return Task.FromResult(_currentUser);
    }

    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        try
        {
            if (_currentUser == null)
            {
                throw new AuthServiceException("No user is currently logged in");
            }

            var result = await _userManager.ChangePasswordAsync(_currentUser, currentPassword, newPassword);

            if (result.Succeeded)
            {
                _currentUser.RequiresPasswordChange = false;
                await _userManager.UpdateAsync(_currentUser);
                _logger.LogInformation("Password changed for user {Email}", _currentUser.Email);
                return true;
            }

            _logger.LogWarning("Password change failed for user {Email}: {Errors}",
                _currentUser.Email, string.Join(", ", result.Errors.Select(e => e.Description)));
            return false;
        }
        catch (AuthServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            throw new AuthServiceException("Could not change password", ex);
        }
    }

    public async Task<bool> SendEmailVerificationCodeAsync(string email)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                _logger.LogWarning("Email verification requested for non-existent user {Email}", email);
                return false;
            }

            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

            if (_emailService != null)
            {
                await _emailService.SendEmailVerificationAsync(email, token);
                _logger.LogInformation("Email verification token sent to {Email}", email);
            }
            else
            {
                _logger.LogWarning("Email service not configured - verification token: {Token}", token);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email verification");
            throw new AuthServiceException("Could not send email verification", ex);
        }
    }

    public async Task<bool> VerifyEmailAsync(string userId, string token)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("Email verification failed: user {UserId} not found", userId);
                return false;
            }

            var result = await _userManager.ConfirmEmailAsync(user, token);

            if (result.Succeeded)
            {
                _logger.LogInformation("Email verified for user {Email}", user.Email);
                return true;
            }

            _logger.LogWarning("Email verification failed for user {Email}", user.Email);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying email");
            throw new AuthServiceException("Could not verify email", ex);
        }
    }

    public async Task<bool> UpdateEmailAsync(string newEmail)
    {
        try
        {
            if (_currentUser == null)
            {
                throw new AuthServiceException("No user is currently logged in");
            }

            var token = await _userManager.GenerateChangeEmailTokenAsync(_currentUser, newEmail);
            var result = await _userManager.ChangeEmailAsync(_currentUser, newEmail, token);

            if (result.Succeeded)
            {
                _logger.LogInformation("Email updated for user {OldEmail} to {NewEmail}", _currentUser.Email, newEmail);
                return true;
            }

            _logger.LogWarning("Email update failed for user {Email}", _currentUser.Email);
            return false;
        }
        catch (AuthServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating email");
            throw new AuthServiceException("Could not update email", ex);
        }
    }

    public async Task<bool> SendPasswordResetTokenAsync(string email)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                _logger.LogWarning("Password reset requested for non-existent user {Email}", email);
                return false;
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            if (_emailService != null)
            {
                await _emailService.SendPasswordResetAsync(email, token);
                _logger.LogInformation("Password reset token sent to {Email}", email);
            }
            else
            {
                _logger.LogWarning("Email service not configured - password reset token: {Token}", token);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending password reset token");
            throw new AuthServiceException("Could not send password reset token", ex);
        }
    }

    public async Task<bool> ResetPasswordAsync(string email, string token, string newPassword)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                _logger.LogWarning("Password reset failed: user {Email} not found", email);
                return false;
            }

            var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

            if (result.Succeeded)
            {
                _logger.LogInformation("Password reset successful for user {Email}", email);
                return true;
            }

            _logger.LogWarning("Password reset failed for user {Email}", email);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting password");
            throw new AuthServiceException("Could not reset password", ex);
        }
    }

    public async Task<IdentityResult> CreateUserAsync(ApplicationUser user, string password)
    {
        return await _userManager.CreateAsync(user, password);
    }

    public async Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role)
    {
        return await _userManager.AddToRoleAsync(user, role);
    }

    public async Task<IdentityResult> RemoveFromRoleAsync(ApplicationUser user, string role)
    {
        return await _userManager.RemoveFromRoleAsync(user, role);
    }

    public async Task<IList<string>> GetUserRolesAsync(ApplicationUser user)
    {
        return await _userManager.GetRolesAsync(user);
    }

    public async Task<bool> IsInRoleAsync(ApplicationUser user, string role)
    {
        return await _userManager.IsInRoleAsync(user, role);
    }

    public async Task<bool> EnableTwoFactorAsync(ApplicationUser user)
    {
        var result = await _userManager.SetTwoFactorEnabledAsync(user, true);
        return result.Succeeded;
    }

    public async Task<bool> DisableTwoFactorAsync(ApplicationUser user)
    {
        var result = await _userManager.SetTwoFactorEnabledAsync(user, false);
        return result.Succeeded;
    }

    public async Task<string> GenerateTwoFactorTokenAsync(ApplicationUser user)
    {
        return await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
    }

    public async Task<bool> VerifyTwoFactorTokenAsync(ApplicationUser user, string token)
    {
        return await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider, token);
    }
}
