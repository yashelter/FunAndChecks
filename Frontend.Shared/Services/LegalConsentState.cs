namespace Frontend.Shared.Services;

/// <summary>
/// Для кого в этой вкладке уже подтверждено согласие с правовыми документами — чтобы не спрашивать статус
/// на каждой странице. Хранится Id, а не флаг: после выхода и входа другим студентом проверка повторится.
/// </summary>
public sealed class LegalConsentState
{
    public Guid? ConfirmedFor { get; set; }
}
