using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Common.Interfaces;

/// <summary>
/// Абстракция над персистентностью для прикладного слоя.
/// Реализуется EF Core DbContext-ом в Infrastructure.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Student> Students { get; }
    DbSet<Admin> Admins { get; }
    DbSet<Group> Groups { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<CourseTask> Tasks { get; }
    DbSet<TaskGroup> TaskGroups { get; }
    DbSet<Submission> Submissions { get; }
    DbSet<QueueEvent> QueueEvents { get; }
    DbSet<QueueEntry> QueueEntries { get; }
    DbSet<GroupSubject> GroupSubjects { get; }
    DbSet<GradeComponent> GradeComponents { get; }
    DbSet<StudentGrade> StudentGrades { get; }
    DbSet<AdminSubjectAccess> AdminSubjectAccesses { get; }
    DbSet<AdminGroupAccess> AdminGroupAccesses { get; }
    DbSet<AttendanceSession> AttendanceSessions { get; }
    DbSet<AttendanceRecord> AttendanceRecords { get; }
    DbSet<UserProfile> UserProfiles { get; }
    DbSet<ProfileMediaAsset> ProfileMediaAssets { get; }
    DbSet<AchievementDefinition> AchievementDefinitions { get; }
    DbSet<AchievementAward> AchievementAwards { get; }
    DbSet<PinnedAchievement> PinnedAchievements { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<ForumTopic> ForumTopics { get; }
    DbSet<ForumPost> ForumPosts { get; }
    DbSet<ForumReaction> ForumReactions { get; }
    DbSet<ForumTopicRead> ForumTopicReads { get; }
    DbSet<ForumSubscription> ForumSubscriptions { get; }
    DbSet<ForumReport> ForumReports { get; }
    DbSet<ForumBan> ForumBans { get; }
    DbSet<Survey> Surveys { get; }
    DbSet<SurveyTargetGroup> SurveyTargetGroups { get; }
    DbSet<SurveyQuestion> SurveyQuestions { get; }
    DbSet<SurveyOption> SurveyOptions { get; }
    DbSet<SurveyResponse> SurveyResponses { get; }
    DbSet<SurveyAnswer> SurveyAnswers { get; }
    DbSet<SurveyParticipation> SurveyParticipations { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs a consistency-sensitive use case in a serializable transaction with bounded retries.</summary>
    Task ExecuteSerializableAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
}
