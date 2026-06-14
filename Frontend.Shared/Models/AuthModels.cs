namespace Frontend.Shared.Models;

public record RegisterStudentRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    int GroupId);

public record LoginRequest(string Email, string Password);

public record AuthResponse(string AccessToken, string RefreshToken);

public record RefreshRequest(string RefreshToken);

public record ConfirmEmailRequest(string Email, string Code);

public record ResendConfirmationRequest(string Email);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Code, string NewPassword);
