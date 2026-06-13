namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Учебная группа. Номер группы и год поступления при необходимости
/// разбираются из имени (формат M8О-XYY-ZZ) на стороне клиента —
/// в доменной модели хранится только само имя.
/// </summary>
public class Group
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public ICollection<Student> Students { get; set; } = new List<Student>();
    public ICollection<GroupSubject> GroupSubjects { get; set; } = new List<GroupSubject>();
}
