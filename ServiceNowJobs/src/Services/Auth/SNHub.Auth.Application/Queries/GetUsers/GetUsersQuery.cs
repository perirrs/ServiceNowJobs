using FluentValidation;
using MediatR;
using SNHub.Auth.Application.DTOs;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Enums;

namespace SNHub.Auth.Application.Queries.GetUsers;

public sealed record GetUsersQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    bool? IsActive = null,
    UserRole? Role = null) : IRequest<PagedResultDto<UserSummaryDto>>;

public sealed class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100.");
        RuleFor(x => x.Search).MaximumLength(100).When(x => x.Search is not null);
    }
}

public sealed class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, PagedResultDto<UserSummaryDto>>
{
    private readonly IUserRepository _users;

    public GetUsersQueryHandler(IUserRepository users) => _users = users;

    public async Task<PagedResultDto<UserSummaryDto>> Handle(GetUsersQuery request, CancellationToken ct)
    {
        var (items, total) = await _users.GetPagedAsync(
            request.Page, request.PageSize,
            request.Role, request.IsActive, request.Search, ct);

        return new PagedResultDto<UserSummaryDto>(items, total, request.Page, request.PageSize);
    }
}
