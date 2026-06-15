using System.Security.Cryptography;
using System.Text;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Infrastructure.Identity;

public class RefreshTokenService(ApplicationDbContext db, IOptions<JwtOptions> options) : IRefreshTokenService
{
    private readonly TimeSpan _lifetime = TimeSpan.FromDays(options.Value.RefreshTokenDays);

    public async Task<string> IssueAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var raw = GenerateRaw();
        var now = DateTime.UtcNow;

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            TokenHash = Hash(raw),
            UserId = userId,
            CreatedAt = now,
            ExpiresAt = now + _lifetime,
        });
        await db.SaveChangesAsync(cancellationToken);

        return raw;
    }

    public async Task<RefreshRotation?> RotateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var hash = Hash(rawToken);
        var now = DateTime.UtcNow;

        // RepeatableRead: если два запроса параллельно прочитали один токен,
        // PostgreSQL не даст обоим сделать UPDATE — второй получит ошибку сериализации.
        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead, cancellationToken);

        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return null;
        }

        if (!token.IsActive(now))
        {
            // Предъявлен уже отозванный токен — признак кражи: отзываем все токены пользователя.
            if (token.RevokedAt is not null)
            {
                await RevokeAllCoreAsync(token.UserId, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            else
            {
                await tx.RollbackAsync(cancellationToken);
            }
            return null;
        }

        // Ротация: гасим текущий, выпускаем новый.
        token.RevokedAt = now;

        var raw = GenerateRaw();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            TokenHash = Hash(raw),
            UserId = token.UserId,
            CreatedAt = now,
            ExpiresAt = now + _lifetime,
        });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new RefreshRotation(token.UserId, raw);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return;

        var hash = Hash(rawToken);
        var token = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, cancellationToken);
        if (token is null)
            return;

        token.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await RevokeAllCoreAsync(userId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RevokeAllCoreAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in active)
            token.RevokedAt = now;
    }

    private static string GenerateRaw() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
