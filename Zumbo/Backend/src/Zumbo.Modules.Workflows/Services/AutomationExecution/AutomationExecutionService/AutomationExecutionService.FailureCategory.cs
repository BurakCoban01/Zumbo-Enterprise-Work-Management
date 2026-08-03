using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    private static string FailureCategory(Exception exception) =>
        exception switch
        {
            UnauthorizedException => "AuthenticationUnavailable",
            ForbiddenException => "AuthorizationDenied",
            ValidationException => "ValidationFailed",
            NotFoundException => "ResourceUnavailable",
            ConflictException => "Conflict",
            DocumentConcurrencyException => "Concurrency",
            TimeoutException => "TransientDependency",
            _ => "Unexpected"
        };
}
