using StageZero.DataAdapters.Users;
using StageZero.Models;
using StageZero.Services.Email;

namespace StageZero.Services.Auth;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password);
    Task LogoutAsync();
    Task<User?> GetCurrentUserAsync();
    Task<bool> ChangePasswordAsync(string currentPassword, string newPassword);
    Task<bool> SendEmailVerificationCodeAsync(string email);
    Task<bool> VerifyEmailCodeAsync(string code);
    Task<bool> UpdateEmailAsync(string email);
    Task<bool> SendPasswordResetCodeAsync(string email);
    Task<bool> VerifyPasswordResetCodeAsync(string email, string code);
    Task<bool> ResetPasswordAsync(string email, string code, string newPassword);
    bool IsAuthenticated { get; }
    bool RequiresPasswordChange { get; }
    bool RequiresEmailVerification { get; }
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class AuthServiceException : Exception
{
    public AuthServiceException(string message) : base(message) { }
    public AuthServiceException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class AuthService : IAuthService
{
    private readonly ILogger<AuthService> _logger;
    private readonly IUserReader _userReader;
    private readonly IUserWriter _userWriter;
    private readonly IEmailService _emailService;
    private User? _currentUser;

    public AuthService(
        ILogger<AuthService> logger,
        IUserReader userReader,
        IUserWriter userWriter,
        IEmailService emailService)
    {
        _logger = logger;
        _userReader = userReader;
        _userWriter = userWriter;
        _emailService = emailService;
    }

    public bool IsAuthenticated => _currentUser != null;

    public bool RequiresPasswordChange => _currentUser?.RequiresPasswordChange ?? false;

    public bool RequiresEmailVerification => _currentUser != null && !_currentUser.EmailVerified;

    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        try
        {
            _logger.LogDebug("Login attempt for user {Email}", email);

            // Find user by email
            var user = await _userReader.GetByEmailAsync(email);

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

            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                _logger.LogWarning("Login failed: invalid password for user {Email}", email);
                return AuthResult.Failed("Invalid email or password");
            }

            // Update last login time
            user.LastLoginAt = DateTime.UtcNow;
            await _userWriter.UpdateAsync(user);

            _currentUser = user;
            _logger.LogInformation("User {Email} logged in successfully", email);
            return AuthResult.Succeeded(user);
        }
        catch (AuthServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for user {Email}", email);
            throw new AuthServiceException("Could not complete login", ex);
        }
    }

    public Task LogoutAsync()
    {
        if (_currentUser != null)
        {
            _logger.LogInformation("User {Email} logged out", _currentUser.Email);
            _currentUser = null;
        }
        return Task.CompletedTask;
    }

    public Task<User?> GetCurrentUserAsync()
    {
        return Task.FromResult(_currentUser);
    }

    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        try
        {
            if (_currentUser == null)
            {
                _logger.LogWarning("Cannot change password: no user is logged in");
                return false;
            }

            // Verify current password
            if (!BCrypt.Net.BCrypt.Verify(currentPassword, _currentUser.PasswordHash))
            {
                _logger.LogWarning("Password change failed: invalid current password for user {Email}", _currentUser.Email);
                return false;
            }

            // Update password
            _currentUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            _currentUser.RequiresPasswordChange = false;
            await _userWriter.UpdateAsync(_currentUser);

            _logger.LogInformation("Password changed successfully for user {Email}", _currentUser.Email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change password for user {Email}", _currentUser?.Email);
            throw new AuthServiceException("Could not change password", ex);
        }
    }

    public async Task<bool> SendEmailVerificationCodeAsync(string email)
    {
        try
        {
            if (_currentUser == null)
            {
                _logger.LogWarning("Cannot send verification code: no user is logged in");
                return false;
            }

            // Generate 6-digit code
            var code = new Random().Next(100000, 999999).ToString();
            var expiry = DateTime.UtcNow.AddMinutes(15);

            // Update user with code and expiry
            _currentUser.EmailVerificationCode = code;
            _currentUser.EmailVerificationCodeExpiry = expiry;
            await _userWriter.UpdateAsync(_currentUser);

            // Send email
            await _emailService.SendVerificationCodeAsync(email, code);

            _logger.LogInformation("Verification code sent to {Email}", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification code for user {Email}", _currentUser?.Email);
            throw new AuthServiceException("Could not send verification code", ex);
        }
    }

    public async Task<bool> VerifyEmailCodeAsync(string code)
    {
        try
        {
            if (_currentUser == null)
            {
                _logger.LogWarning("Cannot verify code: no user is logged in");
                return false;
            }

            if (string.IsNullOrEmpty(_currentUser.EmailVerificationCode))
            {
                _logger.LogWarning("No verification code found for user {Email}", _currentUser.Email);
                return false;
            }

            if (_currentUser.EmailVerificationCodeExpiry == null ||
                _currentUser.EmailVerificationCodeExpiry < DateTime.UtcNow)
            {
                _logger.LogWarning("Verification code expired for user {Email}", _currentUser.Email);
                return false;
            }

            if (_currentUser.EmailVerificationCode != code)
            {
                _logger.LogWarning("Invalid verification code for user {Email}", _currentUser.Email);
                return false;
            }

            // Mark email as verified
            _currentUser.EmailVerified = true;
            _currentUser.EmailVerificationCode = null;
            _currentUser.EmailVerificationCodeExpiry = null;
            await _userWriter.UpdateAsync(_currentUser);

            _logger.LogInformation("Email verified successfully for user {Email}", _currentUser.Email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify email code for user {Email}", _currentUser?.Email);
            throw new AuthServiceException("Could not verify email code", ex);
        }
    }

    public async Task<bool> UpdateEmailAsync(string email)
    {
        try
        {
            if (_currentUser == null)
            {
                _logger.LogWarning("Cannot update email: no user is logged in");
                return false;
            }

            _currentUser.Email = email;
            _currentUser.EmailVerified = false;
            await _userWriter.UpdateAsync(_currentUser);

            _logger.LogInformation("Email updated to {Email}", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update email for user {Email}", _currentUser?.Email);
            throw new AuthServiceException("Could not update email", ex);
        }
    }

    public async Task<bool> SendPasswordResetCodeAsync(string email)
    {
        try
        {
            _logger.LogDebug("Password reset code requested for email {Email}", email);

            var user = await _userReader.GetByEmailAsync(email);
            if (user == null)
            {
                _logger.LogWarning("Password reset requested for non-existent email {Email}", email);
                // Don't reveal that the email doesn't exist for security reasons
                return true;
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Password reset requested for inactive user {Email}", user.Email);
                // Don't reveal that the account is inactive for security reasons
                return true;
            }

            // Generate 6-digit code
            var code = new Random().Next(100000, 999999).ToString();
            var expiry = DateTime.UtcNow.AddMinutes(15);

            // Update user with reset code and expiry
            user.PasswordResetCode = code;
            user.PasswordResetCodeExpiry = expiry;
            await _userWriter.UpdateAsync(user);

            // Send email
            await _emailService.SendPasswordResetCodeAsync(email, code);

            _logger.LogInformation("Password reset code sent to {Email}", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset code for email {Email}", email);
            throw new AuthServiceException("Could not send password reset code", ex);
        }
    }

    public async Task<bool> VerifyPasswordResetCodeAsync(string email, string code)
    {
        try
        {
            var user = await _userReader.GetByEmailAsync(email);
            if (user == null)
            {
                _logger.LogWarning("Password reset verification failed: email {Email} not found", email);
                return false;
            }

            if (string.IsNullOrEmpty(user.PasswordResetCode))
            {
                _logger.LogWarning("No password reset code found for email {Email}", email);
                return false;
            }

            if (user.PasswordResetCodeExpiry == null ||
                user.PasswordResetCodeExpiry < DateTime.UtcNow)
            {
                _logger.LogWarning("Password reset code expired for email {Email}", email);
                return false;
            }

            if (user.PasswordResetCode != code)
            {
                _logger.LogWarning("Invalid password reset code for email {Email}", email);
                return false;
            }

            _logger.LogInformation("Password reset code verified for email {Email}", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify password reset code for email {Email}", email);
            throw new AuthServiceException("Could not verify password reset code", ex);
        }
    }

    public async Task<bool> ResetPasswordAsync(string email, string code, string newPassword)
    {
        try
        {
            var user = await _userReader.GetByEmailAsync(email);
            if (user == null)
            {
                _logger.LogWarning("Password reset failed: email {Email} not found", email);
                return false;
            }

            // Verify the code first
            if (!await VerifyPasswordResetCodeAsync(email, code))
            {
                _logger.LogWarning("Password reset failed: invalid or expired code for email {Email}", email);
                return false;
            }

            // Update password and clear reset code
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.PasswordResetCode = null;
            user.PasswordResetCodeExpiry = null;
            user.RequiresPasswordChange = false;
            user.EmailVerified = false; // Require email verification after password reset
            await _userWriter.UpdateAsync(user);

            _logger.LogInformation("Password reset successfully for email {Email}. Email verification required.", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset password for email {Email}", email);
            throw new AuthServiceException("Could not reset password", ex);
        }
    }
}

