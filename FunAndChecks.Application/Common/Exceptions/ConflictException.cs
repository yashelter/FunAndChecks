namespace FunAndChecks.Application.Common.Exceptions;

/// <summary>Операция конфликтует с текущим состоянием (HTTP 409).</summary>
public class ConflictException(
    string message,
    string code = "state.conflict",
    IReadOnlyDictionary<string, object?>? arguments = null)
    : ApplicationExceptionBase(message, code, arguments);
