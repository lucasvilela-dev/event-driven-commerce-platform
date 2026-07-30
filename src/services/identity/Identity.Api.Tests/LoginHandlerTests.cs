using System.Net;
using Identity.Application.Abstractions;
using Identity.Application.Commands.Login;
using Identity.Application.Exceptions;
using Identity.Domain.Entities;
using Identity.Domain.ValueObjects;

namespace Identity.Api.Tests;

public class LoginHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenIssuer _issuer = Substitute.For<ITokenIssuer>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly LoginHandler _sut;

    public LoginHandlerTests()
    {
        _sut = new LoginHandler(_users, _hasher, _issuer, _unitOfWork);
    }

    [Fact]
    public async Task Handle_throws_InvalidCredentialsException_when_user_not_found()
    {
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ApplicationUser?)null);

        var act = async () => await _sut.Handle(new LoginCommand("x@y.com", "p"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
        _ = _issuer.DidNotReceive().Issue(Arg.Any<ApplicationUser>(), Arg.Any<IReadOnlyCollection<string>>());
    }

    [Fact]
    public async Task Handle_throws_InvalidCredentialsException_when_password_wrong()
    {
        var user = BuildUser();
        _users.FindByEmailAsync("USER@EXAMPLE.COM", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify(user, "wrong", user.PasswordHash).Returns(PasswordVerificationResult.Failed);

        var act = async () => await _sut.Handle(new LoginCommand("user@example.com", "wrong"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
        _ = _issuer.DidNotReceive().Issue(Arg.Any<ApplicationUser>(), Arg.Any<IReadOnlyCollection<string>>());
    }

    [Fact]
    public async Task Handle_issues_tokens_updates_last_login_and_persists()
    {
        var user = BuildUser();
        _users.FindByEmailAsync("USER@EXAMPLE.COM", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify(user, "P@ssw0rd!", user.PasswordHash).Returns(PasswordVerificationResult.Success);
        var expectedTokens = new TokenPair("access-jwt", "refresh-jwt")
        {
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
            RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        };
        _issuer.Issue(user, Arg.Any<IReadOnlyCollection<string>>()).Returns(expectedTokens);

        var result = await _sut.Handle(new LoginCommand("user@example.com", "P@ssw0rd!"), CancellationToken.None);

        result.AccessToken.Should().Be("access-jwt");
        result.RefreshToken.Should().Be("refresh-jwt");
        user.LastLoginAt.Should().NotBeNull();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static ApplicationUser BuildUser()
    {
        var customerRoleId = Guid.Parse("a1b2c3d4-0001-0001-0001-000000000001");
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            NormalizedEmail = "USER@EXAMPLE.COM",
            PasswordHash = "hashed",
            Roles =
            [
                new UserRole { RoleId = customerRoleId, Role = new Role { Id = customerRoleId, Name = "customer", NormalizedName = "CUSTOMER" } },
            ],
        };
    }
}