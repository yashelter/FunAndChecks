namespace FunAndChecks.Application.Common.Exceptions;

/// <summary>Действие запрещено для текущего пользователя (HTTP 403).</summary>
public class ForbiddenException(string message) : Exception(message);
