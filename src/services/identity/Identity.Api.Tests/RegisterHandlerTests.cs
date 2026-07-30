using Identity.Application.Abstractions;
using Identity.Application.Commands.Register;
using Identity.Application.Exceptions;
using Identity.Application.Services;
using Identity.Domain.Entities;

namespace Identity.Api.Tests;

public class RegisterHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RegisterHandler _sut;

    public RegisterHandlerTests()
    {
        _sut = new RegisterHandler(_users, _roles, _hasher, _unitOfWork);
    }

    [Fact]
    public async Task Handle_creates_user_with_customer_role_and_persists()
    {
        var customerRole = new Role { Id = RoleIds.Customer, Name = DefaultRoles.Customer, NormalizedName = "CUSTOMER" };
        _users.FindByEmailAsync("USER@EXAMPLE.COM", Arg.Any<CancellationToken>()).Returns((ApplicationUser?)null);
        _roles.FindByNameAsync("CUSTOMER", Arg.Any<CancellationToken>()).Returns(customerRole);
        _hasher.Hash(Arg.Any<ApplicationUser>(), "P@ssw0rd!").Returns("hashed");

        var result = await _sut.Handle(new RegisterCommand("user@example.com", "P@ssw0rd!"), CancellationToken.None);

        result.UserId.Should().NotBeEmpty();
        await _users.Received(1).AddAsync(Arg.Is<ApplicationUser>(u =>
            u.Email == "user@example.com" &&
            u.NormalizedEmail == "USER@EXAMPLE.COM" &&
            u.PasswordHash == "hashed" &&
            u.Roles.Count == 1 &&
            u.Roles[0].RoleId == RoleIds.Customer), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_throws_DuplicateEmailException_when_email_already_taken()
    {
        var existing = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com", NormalizedEmail = "USER@EXAMPLE.COM" };
        _users.FindByEmailAsync("USER@EXAMPLE.COM", Arg.Any<CancellationToken>()).Returns(existing);

        var act = async () => await _sut.Handle(new RegisterCommand("user@example.com", "anything"), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateEmailException>();
        await _users.DidNotReceive().AddAsync(Arg.Any<ApplicationUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_throws_RoleNotFoundException_when_customer_role_missing()
    {
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ApplicationUser?)null);
        _roles.FindByNameAsync("CUSTOMER", Arg.Any<CancellationToken>()).Returns((Role?)null);

        var act = async () => await _sut.Handle(new RegisterCommand("new@example.com", "P@ssw0rd!"), CancellationToken.None);

        await act.Should().ThrowAsync<RoleNotFoundException>();
    }
}

internal static class RoleIds
{
    public static readonly Guid Customer = Guid.Parse("a1b2c3d4-0001-0001-0001-000000000001");
    public static readonly Guid Admin = Guid.Parse("a1b2c3d4-0001-0001-0001-000000000002");
}