using Identity.Application.Abstractions;
using Identity.Application.Exceptions;
using Identity.Application.Services;
using Identity.Domain.Entities;
using MediatR;

namespace Identity.Application.Commands.Register;

internal sealed class RegisterHandler(
    IUserRepository users,
    IRoleRepository roles,
    IPasswordHasher hasher,
    IUnitOfWork unitOfWork) : IRequestHandler<RegisterCommand, RegisterResult>
{
    public async Task<RegisterResult> Handle(RegisterCommand request, CancellationToken ct)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);

        var existing = await users.FindByEmailAsync(normalizedEmail, ct);
        if (existing is not null)
        {
            throw new DuplicateEmailException(request.Email);
        }

        var customerRole = await roles.FindByNameAsync(EmailNormalizer.Normalize(DefaultRoles.Customer), ct)
            ?? throw new RoleNotFoundException(DefaultRoles.Customer);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = hasher.Hash(null!, request.Password),
            CreatedAt = DateTimeOffset.UtcNow,
            Roles =
            [
                new UserRole { RoleId = customerRole.Id, Role = customerRole },
            ],
        };

        await users.AddAsync(user, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new RegisterResult(user.Id);
    }
}