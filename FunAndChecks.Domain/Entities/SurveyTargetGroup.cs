namespace FunAndChecks.Domain.Entities;

public class SurveyTargetGroup
{
    public int SurveyId { get; set; }
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
}
