namespace FunAndChecks.Domain.Entities;

public class SurveyOption
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public int Order { get; set; }
    public required string Text { get; set; }
}
