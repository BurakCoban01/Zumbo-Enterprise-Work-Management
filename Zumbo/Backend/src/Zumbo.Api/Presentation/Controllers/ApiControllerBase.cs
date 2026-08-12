using Microsoft.AspNetCore.Mvc;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Presentation.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<ApiResponse<T>> OkEnvelope<T>(T data)
    {
        ApplyEnvelopeHeaders(data);
        return Ok(ApiResponse<T>.Ok(data, HttpContext.TraceIdentifier));
    }

    protected IActionResult OkEnvelopeResult<T>(T data) => OkEnvelope(data).Result!;

    protected IActionResult CreatedEnvelopeResult<T>(T data)
    {
        ApplyEnvelopeHeaders(data);
        return StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<T>.Ok(data, HttpContext.TraceIdentifier));
    }

    private void ApplyEnvelopeHeaders<T>(T data)
    {
        if (data is IVersionedResource { Version: > 0 } versioned)
        {
            Response.Headers.ETag = $"\"{versioned.Version}\"";
        }

        if (!Response.Headers.ContainsKey("X-Correlation-Id"))
        {
            Response.Headers["X-Correlation-Id"] = HttpContext.TraceIdentifier;
        }
    }
}
