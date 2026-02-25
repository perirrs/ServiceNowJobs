using FluentValidation;
using MediatR;
using SNHub.Users.Application.DTOs;
using SNHub.Users.Application.Interfaces;
using SNHub.Users.Application.Queries.GetProfile;

namespace SNHub.Users.Application.Queries.GetAdminUsers;

// ── Get paged user list (admin) ───────────────────────────────────────────────

public sealed record GetAdminUsersQuery(
    string? Search,
    bool?   IsDeleted,
    int     Page     = 1,
    int     PageSize = 20) : IRequest<PagedResult<AdminUserDto>>;

public sealed class GetAdminUsersQueryValidator : AbstractValidator<GetAdminUsersQuery>
{
    public GetAdminUsersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(200).When(x => x.Search != null);
    }
}

public sealed class GetAdminUsersQueryHandler
    : IRequestHandler<GetAdminUsersQuery, PagedResult<AdminUserDto>>
{
    private readonly IUserProfileRepository _repo;
    public GetAdminUsersQueryHandler(IUserProfileRepository repo) => _repo = repo;

    public async Task<PagedResult<AdminUserDto>> Handle(GetAdminUsersQuery req, CancellationToken ct)
    {
        var (items, total) = await _repo.GetPagedAsync(
            req.Search, req.IsDeleted, req.Page, req.PageSize, ct);

        return PagedResult<AdminUserDto>.Create(
            items.Select(UserProfileMapper.ToAdminDto), total, req.Page, req.PageSize);
    }
}

// ── Get single user by ID (admin) ─────────────────────────────────────────────

public sealed record GetAdminUserByIdQuery(Guid UserId) : IRequest<AdminUserDto?>;

public sealed class GetAdminUserByIdQueryHandler
    : IRequestHandler<GetAdminUserByIdQuery, AdminUserDto?>
{
    private readonly IUserProfileRepository _repo;
    public GetAdminUserByIdQueryHandler(IUserProfileRepository repo) => _repo = repo;

    public async Task<AdminUserDto?> Handle(GetAdminUserByIdQuery req, CancellationToken ct)
    {
        var p = await _repo.GetByUserIdAsync(req.UserId, ct);
        return p is null ? null : UserProfileMapper.ToAdminDto(p);
    }
}
