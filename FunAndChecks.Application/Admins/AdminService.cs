using FluentValidation;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Students;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Admins;

public class AdminService(
    IApplicationDbContext db,
    IIdentityService identityService,
    IValidator<CreateAdminRequest> createValidator,
    IValidator<UpdateAdminRequest> updateValidator)
    : IAdminService
{
    public Task<List<AdminDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        db.Admins
            .OrderBy(a => a.LastName).ThenBy(a => a.FirstName)
            .Select(a => new AdminDto(a.Id, a.FirstName, a.LastName, a.Color, a.Letter))
            .ToListAsync(cancellationToken);

    public async Task<Guid> CreateAsync(CreateAdminRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var adminId = Guid.NewGuid();
        var roles = request.IsSuperAdmin ? new[] { Roles.Admin, Roles.SuperAdmin } : [Roles.Admin];

        var accountResult = await identityService.CreateAccountAsync(
            adminId,
            request.Email,
            request.Password,
            roles,
            emailConfirmed: true,
            cancellationToken);

        if (!accountResult.Succeeded)
            throw new ValidationException(string.Join(" ", accountResult.Errors));

        try
        {
            db.Admins.Add(new Admin
            {
                Id = adminId,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Color = request.Color,
                Letter = request.Letter,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await identityService.DeleteAccountAsync(adminId, CancellationToken.None);
            throw;
        }

        return adminId;
    }

    public async Task UpdateAsync(Guid adminId, UpdateAdminRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var admin = await db.Admins.FindAsync([adminId], cancellationToken)
                    ?? throw new NotFoundException($"Admin with ID {adminId} not found.");

        admin.FirstName = request.FirstName;
        admin.LastName = request.LastName;
        admin.Color = request.Color;
        admin.Letter = request.Letter;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid actingAdminId, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (actingAdminId == adminId)
            throw new ConflictException("You cannot delete your own account.");

        var admin = await db.Admins.FindAsync([adminId], cancellationToken)
                    ?? throw new NotFoundException($"Admin with ID {adminId} not found.");

        // История проверок и оценок защищена FK (Restrict) — иначе потеряем авторство.
        var hasSubmissions = await db.Submissions.AnyAsync(s => s.AdminId == adminId, cancellationToken);
        var hasGrades = await db.StudentGrades.AnyAsync(g => g.AdminId == adminId, cancellationToken);
        if (hasSubmissions || hasGrades)
            throw new ConflictException("Cannot delete an admin who has recorded submissions or grades.");

        db.Admins.Remove(admin);
        await db.SaveChangesAsync(cancellationToken);

        // Учётная запись завязана на тот же Id — удаляем следом.
        await identityService.DeleteAccountAsync(adminId, cancellationToken);
    }
}
