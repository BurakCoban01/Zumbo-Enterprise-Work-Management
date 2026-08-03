using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Runtime;

public interface IExternalDependencyJitter
{
    TimeSpan Apply(TimeSpan delay, double ratio);
}
