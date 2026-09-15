using FluentAssertions;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FunAndChecks.Tests;

public class EmailCodeTokenProviderTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private static UserManager<ApplicationUser> CreateManager(ApplicationDbContext db) =>
        new(
            new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(db),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            null, null, null, null, null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

    private EmailCodeTokenProvider CreateSut(TimeSpan? lifetime = null) =>
        new(_cache, Options.Create(new EmailCodeTokenProviderOptions
        {
            Lifetime = lifetime ?? TimeSpan.FromMinutes(10),
        }));

    private static async Task<(UserManager<ApplicationUser> Manager, ApplicationUser User)> SeedAsync(ApplicationDbContext ctx)
    {
        var group = ctx.Group();
        await ctx.SaveChangesAsync();

        var student = ctx.Student(group, "CodeOwner");
        await ctx.SaveChangesAsync();

        var manager = CreateManager(ctx);
        var user = await manager.FindByIdAsync(student.Id.ToString());
        return (manager, user!);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsSixDigitCode_ValidateAcceptsIt()
    {
        await using var ctx = _db.NewContext();
        var (manager, user) = await SeedAsync(ctx);
        var sut = CreateSut();

        var code = await sut.GenerateAsync("EmailConfirmation", manager, user);

        code.Should().MatchRegex("^\\d{6}$");
        (await sut.ValidateAsync("EmailConfirmation", code, manager, user)).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WrongCodeOrUnknownPurpose_Fails()
    {
        await using var ctx = _db.NewContext();
        var (manager, user) = await SeedAsync(ctx);
        var sut = CreateSut();
        var code = await sut.GenerateAsync("EmailConfirmation", manager, user);
        // «Неверный» код выводим из правильного, чтобы исключить случайное совпадение.
        var wrongCode = ((int.Parse(code) + 1) % 1_000_000).ToString("D6");

        (await sut.ValidateAsync("EmailConfirmation", wrongCode, manager, user)).Should().BeFalse();
        // Код другого назначения не подходит (подтверждение почты ≠ сброс пароля).
        (await sut.ValidateAsync("ResetPassword", code, manager, user)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_AfterLifetime_Expires()
    {
        await using var ctx = _db.NewContext();
        var (manager, user) = await SeedAsync(ctx);
        var sut = CreateSut(lifetime: TimeSpan.FromMilliseconds(50));
        var code = await sut.GenerateAsync("ResetPassword", manager, user);

        await Task.Delay(150);

        (await sut.ValidateAsync("ResetPassword", code, manager, user)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_AfterSecurityStampRotation_Fails()
    {
        // Смена пароля ротирует SecurityStamp — старый код должен погаснуть мгновенно.
        await using var ctx = _db.NewContext();
        var (manager, user) = await SeedAsync(ctx);
        var sut = CreateSut();
        var code = await sut.GenerateAsync("ResetPassword", manager, user);

        await manager.UpdateSecurityStampAsync(user);

        (await sut.ValidateAsync("ResetPassword", code, manager, user)).Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_SecondCode_ReplacesFirst()
    {
        await using var ctx = _db.NewContext();
        var (manager, user) = await SeedAsync(ctx);
        var sut = CreateSut();
        var first = await sut.GenerateAsync("EmailConfirmation", manager, user);
        var second = await sut.GenerateAsync("EmailConfirmation", manager, user);

        (await sut.ValidateAsync("EmailConfirmation", first, manager, user)).Should().BeFalse();
        (await sut.ValidateAsync("EmailConfirmation", second, manager, user)).Should().BeTrue();
    }
}
