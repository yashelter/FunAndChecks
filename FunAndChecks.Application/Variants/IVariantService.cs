namespace FunAndChecks.Application.Variants;

/// <summary>
/// Наборы вариантов предмета. Студент берёт вариант сам, пока выбор открыт (кто первый — того и вариант);
/// преподаватель с доступом к предмету выдаёт и меняет варианты без ограничений.
/// </summary>
public interface IVariantService
{
    Task<List<VariantSetDto>> GetSetsAsync(Guid adminId, int subjectId, CancellationToken ct = default);
    Task<VariantSetDto> CreateSetAsync(Guid adminId, int subjectId, SaveVariantSetRequest request, CancellationToken ct = default);
    Task<VariantSetDto> UpdateSetAsync(Guid adminId, int setId, SaveVariantSetRequest request, CancellationToken ct = default);
    Task DeleteSetAsync(Guid adminId, int setId, CancellationToken ct = default);

    Task<VariantDto> AddVariantAsync(Guid adminId, int setId, AddVariantRequest request, CancellationToken ct = default);
    /// <summary>Добавляет номера from..to, пропуская уже существующие; возвращает число добавленных.</summary>
    Task<int> AddVariantRangeAsync(Guid adminId, int setId, AddVariantRangeRequest request, CancellationToken ct = default);
    Task<VariantDto> UpdateVariantAsync(Guid adminId, int variantId, UpdateVariantRequest request, CancellationToken ct = default);
    Task DeleteVariantAsync(Guid adminId, int variantId, CancellationToken ct = default);

    Task<VariantBoardDto> GetBoardAsync(Guid adminId, int subjectId, CancellationToken ct = default);
    /// <summary>Наборы предмета глазами конкретного студента — для карточки студента у преподавателя.</summary>
    Task<List<StudentVariantSetDto>> GetForStudentAsync(Guid adminId, int subjectId, Guid studentId, CancellationToken ct = default);
    Task AssignAsync(Guid adminId, int setId, Guid studentId, int variantId, CancellationToken ct = default);
    Task UnassignAsync(Guid adminId, int setId, Guid studentId, CancellationToken ct = default);

    Task<List<StudentVariantSetDto>> GetMyAsync(Guid studentId, int subjectId, CancellationToken ct = default);
    Task<StudentVariantSetDto> ClaimAsync(Guid studentId, int setId, int variantId, CancellationToken ct = default);
}
