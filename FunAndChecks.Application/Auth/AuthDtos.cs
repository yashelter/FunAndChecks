namespace FunAndChecks.Application.Auth;

public record RegisterStudentRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    int GroupId,
    bool AcceptedPrivacyPolicy,
    bool AcceptedTerms);

public record LoginRequest(string Email, string Password);

public record AuthResponse(string AccessToken, string RefreshToken);

public record RefreshRequest(string RefreshToken);

public record ConfirmEmailRequest(string Email, string Code);

public record ResendConfirmationRequest(string Email);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Code, string NewPassword);

public record PrivacyPolicyStatusDto(bool Accepted, DateTime? AcceptedAt, bool TermsAccepted, DateTime? TermsAcceptedAt);

public record AcceptLegalRequest(bool AcceptedPrivacyPolicy, bool AcceptedTerms);
