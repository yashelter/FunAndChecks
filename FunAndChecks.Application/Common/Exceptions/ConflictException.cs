namespace FunAndChecks.Application.Common.Exceptions;

/// <summary>Операция конфликтует с текущим состоянием (HTTP 409).</summary>
public class ConflictException(string message) : Exception(message);
