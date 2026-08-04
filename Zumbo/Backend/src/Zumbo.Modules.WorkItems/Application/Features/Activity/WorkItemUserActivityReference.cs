using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemUserActivityReference(
    string WorkItemId,
    bool CommentAuthor,
    bool CommentRevision,
    bool Mention,
    bool WorkLog,
    bool Approval,
    bool Timeline);
