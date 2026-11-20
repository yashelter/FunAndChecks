using FluentValidation;
using FunAndChecks.Application.Admins.Interfaces;
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
    IResultsCacheService cache,
    IAdminPermissionService permissions,
    IValidator<CreateAdminRequest> createValidator,
    IValidator<UpdateAdminRequest> updateValidator)
    : IAdminService
{
    public Task<List<AdminDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        db.Admins
            .OrderBy(a => a.LastName).ThenBy(a => a.FirstName)
            .Select(a => new AdminDto(a.Id, a.FirstName, a.LastName, a.Color, a.Letter))
            .ToListAsync(cancellationToken);

    public async Task<List<AdminDetailsDto>> GetDetailsAsync(CancellationToken cancellationToken = default)
    {
        var admins = await GetAllAsync(cancellationToken);
        var emails = await identityService.GetEmailsAsync(admins.Select(a => a.Id));
        var superAdmins = (await identityService.GetUserIdsInRoleAsync(Roles.SuperAdmin, cancellationToken)).ToHashSet();

        return admins
            .Select(a => new AdminDetailsDto(
                a.Id, a.FirstName, a.LastName, a.Color, a.Letter,
                emails.GetValueOrDefault(a.Id), superAdmins.Contains(a.Id)))
            .ToList();
    }

    public async Task<Guid> CreateAsync(Guid actingAdminId, CreateAdminRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);
        Guid adminId = Guid.NewGuid();
        await permissions.ManageAsync(actingAdminId, null, request.IsSuperAdmin,
            token => CreateCoreAsync(adminId, request, token), cancellationToken);
        return adminId;
    }

    private async Task CreateCoreAsync(Guid adminId, CreateAdminRequest request, CancellationToken cancellationToken)
    {
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

    public async Task UpdateAsync(
        Guid actingAdminId, Guid adminId, UpdateAdminRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        await permissions.ManageAsync(actingAdminId, adminId, request.IsSuperAdmin.HasValue,
            token => UpdateCoreAsync(actingAdminId, adminId, request, token), cancellationToken);

        List<int> affectedSubjectIds = await db.Submissions
            .Where(s => s.AdminId == adminId)
            .Select(s => s.Task.SubjectId)
            .Concat(db.StudentGrades.Where(g => g.AdminId == adminId).Select(g => g.GradeComponent.SubjectId))
            .Distinct().ToListAsync(cancellationToken);

        foreach (int subjectId in affectedSubjectIds)
        {
            cache.Invalidate(subjectId);
        }
    }

    private async Task UpdateCoreAsync(Guid actingAdminId, Guid adminId, UpdateAdminRequest request,
        CancellationToken cancellationToken)
    {
        var admin = await db.Admins.FindAsync([adminId], cancellationToken)
                    ?? throw new NotFoundException($"Admin with ID {adminId} not found.");

        // Сначала проверки и то, что может отказать (занятая почта, политика паролей), потом роль и профиль.
        var roleChanged = request.IsSuperAdmin is bool makeSuperAdmin
                          && await EnsureSuperAdminRoleChangeAllowedAsync(actingAdminId, adminId, makeSuperAdmin, cancellationToken);

        var newEmail = request.Email?.Trim();
        var hasNewPassword = !string.IsNullOrEmpty(request.NewPassword);
        if (!string.IsNullOrEmpty(newEmail) || hasNewPassword)
        {
            var currentEmail = await identityService.GetEmailAsync(adminId)
                               ?? throw new NotFoundException($"Account for admin {adminId} not found.");
            var emailChanged = !string.IsNullOrEmpty(newEmail)
                               && !string.Equals(newEmail, currentEmail, StringComparison.OrdinalIgnoreCase);
            // Сброс учётки отзывает refresh-токены — не трогаем её, если почта и пароль те же.
            if (emailChanged || hasNewPassword)
                await identityService.UpdateAccountAdminAsync(
                    adminId, emailChanged ? newEmail! : currentEmail, hasNewPassword ? request.NewPassword : null);
        }

        if (roleChanged)
            await identityService.SetRoleAsync(adminId, Roles.SuperAdmin, request.IsSuperAdmin!.Value, cancellationToken);

        admin.FirstName = request.FirstName;
        admin.LastName = request.LastName;
        admin.Color = request.Color;
        admin.Letter = request.Letter;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>true — роль нужно поменять; бросает, если снимать её нельзя.</summary>
    private async Task<bool> EnsureSuperAdminRoleChangeAllowedAsync(
        Guid actingAdminId, Guid adminId, bool makeSuperAdmin, CancellationToken cancellationToken)
    {
        var superAdmins = await identityService.GetUserIdsInRoleAsync(Roles.SuperAdmin, cancellationToken);
        if (superAdmins.Contains(adminId) == makeSuperAdmin)
            return false;

        if (!makeSuperAdmin)
        {
            if (adminId == actingAdminId)
                throw new ConflictException("You cannot revoke your own super admin role.", "admin.cannot_demote_self");
            if (!await db.Admins.AnyAsync(other => other.Id != adminId && superAdmins.Contains(other.Id), cancellationToken))
                throw new ConflictException("The last super admin cannot be demoted.", "admin.last_superadmin");
        }

        return true;
    }

    public Task DeleteAsync(Guid actingAdminId, Guid adminId, CancellationToken cancellationToken = default) =>
        permissions.ManageAsync(actingAdminId, adminId, false,
            token => DeleteCoreAsync(actingAdminId, adminId, token), cancellationToken);

    private async Task DeleteCoreAsync(Guid actingAdminId, Guid adminId, CancellationToken cancellationToken)
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

        IReadOnlyCollection<Guid> superAdmins = await identityService.GetUserIdsInRoleAsync(Roles.SuperAdmin, cancellationToken);
        if (superAdmins.Contains(adminId) && !await db.Admins.AnyAsync(
            other => other.Id != adminId && superAdmins.Contains(other.Id), cancellationToken))
        {
            throw new ConflictException("The last super admin cannot be deleted.", "admin.last_superadmin");
        }

        db.Admins.Remove(admin);
        await db.SaveChangesAsync(cancellationToken);

        // Учётная запись завязана на тот же Id — удаляем следом.
        await identityService.DeleteAccountAsync(adminId, cancellationToken);
    }
}
