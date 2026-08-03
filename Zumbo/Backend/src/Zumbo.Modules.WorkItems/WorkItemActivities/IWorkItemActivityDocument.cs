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

public interface IWorkItemActivityDocument : IVersionedDocument
{
    string OrganizationId { get; set; }
    string ProjectId { get; set; }
    string WorkItemId { get; set; }
}
