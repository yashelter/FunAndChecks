namespace FunAndChecks.Application.Common.Exceptions;

/// <summary>
/// Почтовый сервер окончательно отверг адрес получателя (5xx: домен не принимает почту, ящика нет).
/// Ошибка во вводе пользователя, а не сбой сервера (HTTP 400).
/// </summary>
public class EmailDeliveryException(
    string message,
    string code = "email.recipient_rejected",
    IReadOnlyDictionary<string, object?>? arguments = null)
    : ApplicationExceptionBase(message, code, arguments);
