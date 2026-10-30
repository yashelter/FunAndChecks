using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Набор вариантов предмета («ЛР 1–4», «Курсовая»): свой пул, правило повторов и срок выбора.
/// </summary>
public class VariantSet
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
    public required string Name { get; set; }
    public VariantUniqueness Uniqueness { get; set; }
    /// <summary>Студенты могут брать варианты сами. Выключение — ручное закрытие выбора.</summary>
    public bool ClaimsOpen { get; set; }
    /// <summary>После этого момента (UTC) выбор закрыт, даже если <see cref="ClaimsOpen"/>.</summary>
    public DateTime? ClaimDeadline { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<Variant> Variants { get; set; } = new List<Variant>();
    public ICollection<VariantAssignment> Assignments { get; set; } = new List<VariantAssignment>();

    public bool IsClaimOpen(DateTime utcNow) => ClaimsOpen && (ClaimDeadline is null || utcNow < ClaimDeadline);
}
