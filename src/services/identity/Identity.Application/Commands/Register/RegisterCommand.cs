using MediatR;

namespace Identity.Application.Commands.Register;

public sealed record RegisterCommand(string Email, string Password) : IRequest<RegisterResult>;

public sealed record RegisterResult(Guid UserId);