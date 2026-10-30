namespace FunAndChecks.Domain.Entities;

/// <summary>Вариант задания: номер в наборе и необязательные название и описание.</summary>
public class Variant
{
    public int Id { get; set; }
    public int VariantSetId { get; set; }
    public VariantSet VariantSet { get; set; } = null!;
    public int Number { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
}
