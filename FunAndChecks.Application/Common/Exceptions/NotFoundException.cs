namespace FunAndChecks.Application.Common.Exceptions;

/// <summary>Запрошенная сущность не найдена (HTTP 404).</summary>
public class NotFoundException(
    string message,
    string code = "resource.not_found",
    IReadOnlyDictionary<string, object?>? arguments = null)
    : ApplicationExceptionBase(message, code, arguments);
