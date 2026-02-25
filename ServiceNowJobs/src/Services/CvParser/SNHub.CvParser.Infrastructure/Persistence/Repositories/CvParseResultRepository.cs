using Microsoft.EntityFrameworkCore;
using SNHub.CvParser.Application.Interfaces;
using SNHub.CvParser.Domain.Entities;

namespace SNHub.CvParser.Infrastructure.Persistence.Repositories;

public sealed class CvParseResultRepository : ICvParseResultRepository
{
    private readonly CvParserDbContext _db;
    public CvParseResultRepository(CvParserDbContext db) => _db = db;

    public Task<CvParseResult?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.ParseResults.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IEnumerable<CvParseResult>> GetByUserIdAsync(
        Guid userId, CancellationToken ct = default)
        => await _db.ParseResults
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(CvParseResult result, CancellationToken ct = default)
        => await _db.ParseResults.AddAsync(result, ct);

    public Task<int> CountByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _db.ParseResults.CountAsync(r => r.UserId == userId, ct);
}
