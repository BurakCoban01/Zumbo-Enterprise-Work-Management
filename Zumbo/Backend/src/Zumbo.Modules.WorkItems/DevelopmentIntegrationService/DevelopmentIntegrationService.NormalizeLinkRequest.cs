using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static LinkValues NormalizeLinkRequest(
        DevelopmentRepositoryMappingDocument mapping,
        CreateWorkItemDevelopmentLinkRequest request) =>
        new(
            NormalizeKind(request.Kind),
            Required(request.ExternalId, "External development id", 300),
            Required(request.Title, "Development link title", 200),
            NormalizeLinkUrl(mapping.RepositoryUrl, request.Url),
            Optional(request.Branch, "Development branch", 255),
            Optional(request.CommitSha, "Development commit", 128),
            NormalizeStatus(request.Status));

}
