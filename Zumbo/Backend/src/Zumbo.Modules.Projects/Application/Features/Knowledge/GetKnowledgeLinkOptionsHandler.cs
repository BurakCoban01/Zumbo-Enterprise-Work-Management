using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed class GetKnowledgeLinkOptionsHandler(KnowledgeService service)
{
    private GetKnowledgeLinkOptionsSlice? slice;

    public GetKnowledgeLinkOptionsHandler(
        IKnowledgeDirectory directory,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new GetKnowledgeLinkOptionsSlice(directory, currentUser);
    }

    public Task<KnowledgeLinkOptionsResponse> HandleAsync(
        GetKnowledgeLinkOptionsQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.GetLinkOptionsAsync(query.ScopeType, query.ScopeId, query.Query, ct);
}
