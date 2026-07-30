using Identity.Application.Commands.Login;
using Identity.Application.Commands.Register;
using Identity.Application.Exceptions;
using Identity.Api.Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Api.Controllers;

[ApiController]
[Route("api/identity")]
public sealed class IdentityController(IMediator mediator) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var command = new RegisterCommand(request.Email, request.Password);
        try
        {
            var result = await mediator.Send(command, ct);
            return CreatedAtAction(nameof(Login), new { }, new RegisterResponse(result.UserId));
        }
        catch (DuplicateEmailException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (RoleNotFoundException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var command = new LoginCommand(request.Email, request.Password);
        try
        {
            var result = await mediator.Send(command, ct);
            return Ok(new LoginResponse(result.AccessToken, result.RefreshToken, result.AccessTokenExpiresAt, result.RefreshTokenExpiresAt));
        }
        catch (InvalidCredentialsException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }
}