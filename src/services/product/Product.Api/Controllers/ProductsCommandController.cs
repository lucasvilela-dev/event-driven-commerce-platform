using MediatR;
using Microsoft.AspNetCore.Mvc;
using Product.Api.Contracts;
using Product.Application.Commands.ActivateProduct;
using Product.Application.Commands.CreateProduct;
using Product.Application.Commands.DeactivateProduct;
using Product.Application.Commands.UpdatePrice;
using Product.Application.Exceptions;

namespace Product.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsCommandController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(CreateProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        var command = new CreateProductCommand(
            request.Name, request.Description, request.Sku,
            request.Price, request.Currency, request.Category,
            request.Featured, request.Priority);
        try
        {
            var result = await mediator.Send(command, ct);
            return Created($"/api/products/{result.Id}", new CreateProductResponse(result.Id));
        }
        catch (DuplicateSkuException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidPriceException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/price")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePrice(Guid id, [FromBody] UpdatePriceRequest request, CancellationToken ct)
    {
        var command = new UpdatePriceCommand(id, request.Price, request.Currency);
        try
        {
            await mediator.Send(command, ct);
            return NoContent();
        }
        catch (ProductNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidPriceException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var command = new ActivateProductCommand(id);
        try
        {
            await mediator.Send(command, ct);
            return NoContent();
        }
        catch (ProductNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var command = new DeactivateProductCommand(id);
        try
        {
            await mediator.Send(command, ct);
            return NoContent();
        }
        catch (ProductNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}