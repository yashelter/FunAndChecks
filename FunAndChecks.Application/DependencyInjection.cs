using FluentValidation;
using FunAndChecks.Application.Attendance;
using FunAndChecks.Application.Variants;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Auth;
using FunAndChecks.Application.Grades;
using FunAndChecks.Application.Groups;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Results;
using FunAndChecks.Application.Students;
using FunAndChecks.Application.Subjects;
using FunAndChecks.Application.Submissions;
using FunAndChecks.Application.Profiles;
using FunAndChecks.Application.Attachments;
using FunAndChecks.Application.Forum;
using FunAndChecks.Application.Notifications;
using FunAndChecks.Application.Surveys;
using Microsoft.Extensions.DependencyInjection;

namespace FunAndChecks.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<FunAndChecks.Application.Account.IAccountService, FunAndChecks.Application.Account.AccountService>();
        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddScoped<ISubjectService, SubjectService>();
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IQueueService, QueueService>();
        services.AddScoped<IQueueAutoFillService, QueueAutoFillService>();
        services.AddScoped<ISubmissionService, SubmissionService>();
        services.AddScoped<IResultsService, ResultsService>();
        services.AddScoped<IStudentService, StudentService>();
        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<IGradeService, GradeService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IAdminAccessService, AdminAccessService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<IVariantService, VariantService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IAchievementService, AchievementService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<IForumAccess, ForumAccess>();
        services.AddScoped<IForumService, ForumService>();
        services.AddScoped<IForumModerationService, ForumModerationService>();
        services.AddScoped<ISurveyService, SurveyService>();
        services.AddScoped<ISurveyAdminService, SurveyAdminService>();

        return services;
    }
}
