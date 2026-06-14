namespace FunAndChecks.Application.Common.Exceptions;

/// <summary>Превышен лимит частоты (например, писем на email). Маппится в HTTP 429.</summary>
public class RateLimitException(string message) : Exception(message);
