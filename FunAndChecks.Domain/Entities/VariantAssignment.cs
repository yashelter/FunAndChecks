namespace FunAndChecks.Domain.Entities;

/// <summary>Вариант студента в наборе: один на студента, взят сам или выдан преподавателем.</summary>
public class VariantAssignment
{
    public int VariantSetId { get; set; }
    public VariantSet VariantSet { get; set; } = null!;
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public int VariantId { get; set; }
    public Variant Variant { get; set; } = null!;
    public DateTime AssignedAt { get; set; }
    /// <summary>Кто выдал; null — студент взял сам (или преподаватель удалён).</summary>
    public Guid? AssignedByAdminId { get; set; }
    public Admin? AssignedByAdmin { get; set; }
    /// <summary>Студент взял сам. Отдельно от автора: удаление преподавателя не превращает выдачу в самостоятельный выбор.</summary>
    public bool SelfClaimed { get; set; }
}
