using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Variants;

public record VariantDto(int Id, int Number, string? Title, string? Description, int AssignedCount);
/// <summary><c>IsOpen</c> — выбор открыт прямо сейчас: включён и срок не прошёл.</summary>
public record VariantSetDto(int Id, int SubjectId, string Name, VariantUniqueness Uniqueness, bool ClaimsOpen,
    DateTime? ClaimDeadline, bool IsOpen, List<VariantDto> Variants);
/// <summary>Создание или изменение набора; диапазон номеров заполняет пул только при создании.</summary>
public record SaveVariantSetRequest(string Name, VariantUniqueness Uniqueness, bool ClaimsOpen, DateTime? ClaimDeadline,
    int? RangeFrom = null, int? RangeTo = null);
public record AddVariantRequest(int Number, string? Title, string? Description);
public record AddVariantRangeRequest(int From, int To);
public record UpdateVariantRequest(string? Title, string? Description);
public record AssignVariantRequest(int VariantId);

public record AssignedVariantDto(int VariantId, int Number, string? Title, bool SelfClaimed);
public record VariantBoardRowDto(Guid StudentId, string FullName, int GroupId, string GroupName,
    Dictionary<int, AssignedVariantDto> Variants);
public record VariantBoardDto(int SubjectId, List<VariantSetDto> Sets, List<VariantBoardRowDto> Students);

/// <summary>Вариант глазами студента: <c>Taken</c> — по правилу набора его уже не взять.</summary>
public record StudentVariantDto(int Id, int Number, string? Title, string? Description, bool Taken);
public record StudentVariantSetDto(int Id, string Name, VariantUniqueness Uniqueness, bool IsOpen, DateTime? ClaimDeadline,
    StudentVariantDto? Mine, bool SelfClaimed, List<StudentVariantDto> Variants);
