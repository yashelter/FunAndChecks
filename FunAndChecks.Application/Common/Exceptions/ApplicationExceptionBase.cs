namespace FunAndChecks.Application.Common.Exceptions;

/// <summary>Base type for application errors exposed through ProblemDetails.</summary>
public abstract class ApplicationExceptionBase(
    string message,
    string code,
    IReadOnlyDictionary<string, object?>? arguments = null) : Exception(message)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object?> Arguments { get; } =
        arguments ?? new Dictionary<string, object?>();
}

public sealed record ErrorDescriptor(string Code, IReadOnlyDictionary<string, object?> Args);
