using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class SurveyConfiguration : IEntityTypeConfiguration<Survey>
{
    public void Configure(EntityTypeBuilder<Survey> builder)
    {
        builder.ToTable("Surveys", t => t.HasCheckConstraint("CK_Surveys_TriggerThreshold", "\"TriggerThreshold\" >= 0"));
        builder.Property(s => s.Title).HasMaxLength(200);
        builder.Property(s => s.Description).HasMaxLength(4000);
        builder.Property(s => s.CreatedByName).HasMaxLength(201);
        builder.HasIndex(s => new { s.Status, s.SubjectId });
        builder.HasOne(s => s.Subject).WithMany().HasForeignKey(s => s.SubjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.CreatedById).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(s => s.Questions).WithOne().HasForeignKey(q => q.SurveyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(s => s.TargetGroups).WithOne().HasForeignKey(g => g.SurveyId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SurveyTargetGroupConfiguration : IEntityTypeConfiguration<SurveyTargetGroup>
{
    public void Configure(EntityTypeBuilder<SurveyTargetGroup> builder)
    {
        builder.ToTable("SurveyTargetGroups");
        builder.HasKey(g => new { g.SurveyId, g.GroupId });
        builder.HasOne(g => g.Group).WithMany().HasForeignKey(g => g.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SurveyQuestionConfiguration : IEntityTypeConfiguration<SurveyQuestion>
{
    public void Configure(EntityTypeBuilder<SurveyQuestion> builder)
    {
        builder.ToTable("SurveyQuestions");
        builder.Property(q => q.Text).HasMaxLength(500);
        builder.Property(q => q.Description).HasMaxLength(1000);
        builder.Property(q => q.ScaleMinLabel).HasMaxLength(100);
        builder.Property(q => q.ScaleMaxLabel).HasMaxLength(100);
        builder.HasIndex(q => new { q.SurveyId, q.Order });
        builder.HasMany(q => q.Options).WithOne().HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SurveyOptionConfiguration : IEntityTypeConfiguration<SurveyOption>
{
    public void Configure(EntityTypeBuilder<SurveyOption> builder)
    {
        builder.ToTable("SurveyOptions");
        builder.Property(o => o.Text).HasMaxLength(300);
    }
}

public class SurveyResponseConfiguration : IEntityTypeConfiguration<SurveyResponse>
{
    public void Configure(EntityTypeBuilder<SurveyResponse> builder)
    {
        builder.ToTable("SurveyResponses");
        builder.HasIndex(r => r.SurveyId);
        // Один неанонимный ответ на человека; анонимную однократность обеспечивает SurveyParticipation.
        builder.HasIndex(r => new { r.SurveyId, r.RespondentId }).IsUnique().HasFilter("\"RespondentId\" IS NOT NULL");
        builder.HasOne<Survey>().WithMany().HasForeignKey(r => r.SurveyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.RespondentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Group>().WithMany().HasForeignKey(r => r.GroupId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(r => new { r.SurveyId, r.GroupId });
        builder.HasMany(r => r.Answers).WithOne().HasForeignKey(a => a.ResponseId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SurveyAnswerConfiguration : IEntityTypeConfiguration<SurveyAnswer>
{
    public void Configure(EntityTypeBuilder<SurveyAnswer> builder)
    {
        builder.ToTable("SurveyAnswers");
        builder.Property(a => a.Text).HasMaxLength(4000);
        builder.Property(a => a.Number).HasPrecision(18, 4);
        builder.HasIndex(a => a.QuestionId);
        builder.HasOne<SurveyQuestion>().WithMany().HasForeignKey(a => a.QuestionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SurveyOption>().WithMany().HasForeignKey(a => a.OptionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SurveyParticipationConfiguration : IEntityTypeConfiguration<SurveyParticipation>
{
    public void Configure(EntityTypeBuilder<SurveyParticipation> builder)
    {
        builder.ToTable("SurveyParticipations");
        builder.HasKey(p => new { p.SurveyId, p.UserId });
        builder.HasIndex(p => p.UserId);
        builder.HasOne<Survey>().WithMany().HasForeignKey(p => p.SurveyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
