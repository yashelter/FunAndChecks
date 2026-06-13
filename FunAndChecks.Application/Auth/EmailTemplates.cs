namespace FunAndChecks.Application.Auth;

internal static class EmailTemplates
{
    public const string ConfirmationSubject = "FunAndChecks — подтверждение почты";
    public const string PasswordResetSubject = "FunAndChecks — сброс пароля";

    public static string Confirmation(string code) =>
        $"""
         <p>Здравствуйте!</p>
         <p>Ваш код подтверждения почты в FunAndChecks:</p>
         <p style="font-size: 24px; font-weight: bold; letter-spacing: 4px;">{code}</p>
         <p>Код действует около 10 минут. Если вы не регистрировались — просто проигнорируйте это письмо.</p>
         """;

    public static string PasswordReset(string code) =>
        $"""
         <p>Здравствуйте!</p>
         <p>Ваш код для сброса пароля в FunAndChecks:</p>
         <p style="font-size: 24px; font-weight: bold; letter-spacing: 4px;">{code}</p>
         <p>Код действует около 10 минут. Если вы не запрашивали сброс — просто проигнорируйте это письмо.</p>
         """;
}
