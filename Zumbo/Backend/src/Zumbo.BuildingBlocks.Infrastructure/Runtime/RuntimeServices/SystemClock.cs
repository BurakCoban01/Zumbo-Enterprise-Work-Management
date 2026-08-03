using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Zumbo.SharedKernel;

namespace Zumbo.BuildingBlocks.Infrastructure.Runtime;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
