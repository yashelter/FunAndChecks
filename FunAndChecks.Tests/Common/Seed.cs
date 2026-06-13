using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;

namespace FunAndChecks.Tests.Common;

/// <summary>Хелперы для наполнения тестовой БД.</summary>
public static class Seed
{
    /// <summary>
    /// Профили Student/Admin связаны FK с учётной записью по общему Id —
    /// под SQLite (с включёнными FK) аккаунт обязателен.
    /// </summary>
    public static void AddAccount(ApplicationDbContext db, Guid id)
    {
        var email = $"{id:N}@example.com";
        db.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        });
    }

    public static Admin Admin(this ApplicationDbContext db, string letter = "A", string color = "#112233")
    {
        var admin = new Admin
        {
            Id = Guid.NewGuid(),
            FirstName = "Admin",
            LastName = letter,
            Color = color,
            Letter = letter,
        };
        AddAccount(db, admin.Id);
        db.Admins.Add(admin);
        return admin;
    }

    public static Group Group(this ApplicationDbContext db, string name = "M8O-101")
    {
        var group = new Group { Name = name };
        db.Groups.Add(group);
        return group;
    }

    public static Subject Subject(this ApplicationDbContext db, string name = "Math")
    {
        var subject = new Subject { Name = name };
        db.Subjects.Add(subject);
        return subject;
    }

    public static Student Student(this ApplicationDbContext db, Group group, string last = "Ivanov")
    {
        var student = new Student
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = last,
            GroupId = group.Id,
        };
        AddAccount(db, student.Id);
        db.Students.Add(student);
        return student;
    }

    public static CourseTask Task(this ApplicationDbContext db, Subject subject, int maxPoints = 10, string name = "Task")
    {
        var task = new CourseTask
        {
            Name = name,
            Description = "desc",
            MaxPoints = maxPoints,
            SubjectId = subject.Id,
        };
        db.Tasks.Add(task);
        return task;
    }

    public static GradeComponent Component(this ApplicationDbContext db, Subject subject, int maxPoints = 100, int minPoints = 0, string name = "Exam")
    {
        var component = new GradeComponent { Name = name, MinPoints = minPoints, MaxPoints = maxPoints, SubjectId = subject.Id };
        db.GradeComponents.Add(component);
        return component;
    }

    public static void LinkGroupSubject(this ApplicationDbContext db, Group group, Subject subject) =>
        db.GroupSubjects.Add(new GroupSubject { GroupId = group.Id, SubjectId = subject.Id });
}
