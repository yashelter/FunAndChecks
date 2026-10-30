namespace Frontend.Shared.Models;

public enum VariantUniqueness { AllowRepeats = 0, UniqueInSubject = 1, UniqueInGroup = 2 }

public record VariantDto(int Id, int Number, string? Title, string? Description, int AssignedCount);
public record VariantSetDto(int Id, int SubjectId, string Name, VariantUniqueness Uniqueness, bool ClaimsOpen,
    DateTime? ClaimDeadline, bool IsOpen, List<VariantDto> Variants);
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

public record StudentVariantDto(int Id, int Number, string? Title, string? Description, bool Taken);
public record StudentVariantSetDto(int Id, string Name, VariantUniqueness Uniqueness, bool IsOpen, DateTime? ClaimDeadline,
    StudentVariantDto? Mine, bool SelfClaimed, List<StudentVariantDto> Variants);
