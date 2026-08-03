using System.Security.Cryptography;
using System.Text;
using System.Linq.Expressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemCollaborationResponse(
    string WorkItemId,
    int WatcherCount,
    int VoteCount,
    bool Watching,
    bool Voted,
    long Version);
