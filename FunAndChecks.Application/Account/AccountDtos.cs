namespace FunAndChecks.Application.Account;

/// <summary>Студент сам меняет фамилию и имя.</summary>
public record ChangeNameRequest(string FirstName, string LastName);

/// <summary>Шаг 1 смены почты: новый адрес и текущий пароль. Код уходит на новый адрес.</summary>
public record RequestEmailChangeRequest(string NewEmail, string CurrentPassword);

/// <summary>Шаг 2 смены почты: код из письма на новый адрес.</summary>
public record ConfirmEmailChangeRequest(string NewEmail, string Code);
