namespace FunAndChecks.Application.Account;

/// <summary>Самостоятельное изменение учётной записи студентом: имя и почта.</summary>
public interface IAccountService
{
    /// <summary>Меняет фамилию и имя студента; таблицы результатов его предметов перестраиваются.</summary>
    Task ChangeNameAsync(Guid studentId, ChangeNameRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверяет пароль и отправляет код на новый адрес. Почта не меняется, пока код не введён.
    /// Не чаще одного письма в минуту на адрес.
    /// </summary>
    Task RequestEmailChangeAsync(Guid userId, RequestEmailChangeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Меняет почту по коду и сообщает об этом на прежний адрес.</summary>
    Task ConfirmEmailChangeAsync(Guid userId, ConfirmEmailChangeRequest request, CancellationToken cancellationToken = default);
}
