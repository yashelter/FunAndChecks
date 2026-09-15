namespace FunAndChecks.Application.Common.Exceptions;

/// <summary>Действие запрещено для текущего пользователя (HTTP 403).</summary>
public class ForbiddenException(
    string message,
    string code = "access.forbidden",
    IReadOnlyDictionary<string, object?>? arguments = null)
    : ApplicationExceptionBase(message, code, arguments);
