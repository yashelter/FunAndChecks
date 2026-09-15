namespace FunAndChecks.Application.Common.Exceptions;

/// <summary>Превышен лимит частоты (например, писем на email). Маппится в HTTP 429.</summary>
public class RateLimitException(
    string message,
    string code = "rate_limit.exceeded",
    IReadOnlyDictionary<string, object?>? arguments = null)
    : ApplicationExceptionBase(message, code, arguments);
