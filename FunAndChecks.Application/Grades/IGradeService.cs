namespace FunAndChecks.Application.Grades;

public interface IGradeService
{
    Task<List<GradeComponentDto>> GetComponentsAsync(int subjectId, CancellationToken cancellationToken = default);

    /// <summary>Создаёт оценочную колонку у предмета (билет, курсовая и т.п.).</summary>
    Task<GradeComponentDto> CreateComponentAsync(Guid adminId, int subjectId, CreateGradeComponentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Обновляет название и диапазон баллов колонки.</summary>
    Task<GradeComponentDto> UpdateComponentAsync(Guid adminId, int componentId, UpdateGradeComponentRequest request, CancellationToken cancellationToken = default);

    Task DeleteComponentAsync(Guid adminId, int componentId, CancellationToken cancellationToken = default);

    /// <summary>Выставляет или обновляет баллы студента за колонку.</summary>
    Task SetGradeAsync(Guid adminId, int componentId, Guid studentId, SetGradeRequest request, CancellationToken cancellationToken = default);

    Task DeleteGradeAsync(Guid adminId, int componentId, Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Оценки студента по всем колонкам предмета.</summary>
    Task<List<StudentGradeDto>> GetStudentGradesAsync(Guid studentId, int subjectId, CancellationToken cancellationToken = default);
}
