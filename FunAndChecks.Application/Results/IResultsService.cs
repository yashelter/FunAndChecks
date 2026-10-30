using FunAndChecks.Application.Students;

namespace FunAndChecks.Application.Results;

public interface IResultsService
{
    /// <summary>
    /// Таблица результатов по предмету. Берётся из кэша; при промахе строится лениво
    /// и кладётся в кэш. Бросает NotFoundException, если предмета нет.
    /// </summary>
    Task<SubjectResultsDto> GetSubjectResultsAsync(int subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Цвета студентов предмета — лёгкий снимок для живой подписки таблицы (вместо повторной загрузки всей таблицы).
    /// Бросает NotFoundException, если предмета нет.
    /// </summary>
    Task<IReadOnlyList<StudentColorUpdateDto>> GetStudentColorsAsync(int subjectId, CancellationToken cancellationToken = default);

    /// <summary>Детальные результаты одного студента по предмету.</summary>
    Task<StudentSubjectResultsDto> GetStudentResultsAsync(Guid studentId, int subjectId, CancellationToken cancellationToken = default);
}
