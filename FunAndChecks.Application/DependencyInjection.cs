using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Auth;
using FunAndChecks.Application.Grades;
using FunAndChecks.Application.Groups;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Results;
using FunAndChecks.Application.Students;
using FunAndChecks.Application.Subjects;
using FunAndChecks.Application.Submissions;
using Microsoft.Extensions.DependencyInjection;

namespace FunAndChecks.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISubjectService, SubjectService>();
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IQueueService, QueueService>();
        services.AddScoped<ISubmissionService, SubmissionService>();
        services.AddScoped<IResultsService, ResultsService>();
        services.AddScoped<IStudentService, StudentService>();
        services.AddScoped<IGradeService, GradeService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IAdminAccessService, AdminAccessService>();

        return services;
    }
}
