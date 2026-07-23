using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;

namespace Zumbo.RepositoryContracts;

public abstract class IntakeRepositoryContract
{
    protected abstract IDocumentRepository<IntakeFormDocument> Forms();
    protected abstract IDocumentRepository<IntakeFormVersionDocument> Versions();
    protected abstract IDocumentRepository<IntakeSubmissionDocument> Submissions();

    [Fact]
    public async Task IntakeStores_PreserveTenantVersionIdempotencyAndTriageQueries()
    {
        var forms = Forms();
        var versions = Versions();
        var submissions = Submissions();
        var prefix = "feature001-contract-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var form = new IntakeFormDocument
        {
            Id = prefix + "-form",
            OrganizationId = prefix + "-org",
            ProjectId = prefix + "-project",
            Name = "Contract form",
            PublicId = prefix + "-public",
            State = IntakeFormStates.Published,
            PublishedVersion = 1,
            PublishedAccessPolicy = IntakeAccessPolicies.Public,
            Draft = new IntakeFormDefinitionDocument
            {
                AccessPolicy = IntakeAccessPolicies.Public,
                BoardId = prefix + "-board"
            },
            CreatedByUserId = prefix + "-user",
            UpdatedByUserId = prefix + "-user",
            CreatedAt = now,
            UpdatedAt = now
        };
        try
        {
            form = await forms.CreateAsync(form);
            var published = await versions.CreateAsync(new IntakeFormVersionDocument
            {
                Id = IntakeStableIds.FormVersionId(form.Id, 1),
                OrganizationId = form.OrganizationId,
                FormId = form.Id,
                ProjectId = form.ProjectId,
                DefinitionVersion = 1,
                Name = form.Name,
                Definition = new IntakeFormDefinitionDocument
                {
                    AccessPolicy = form.Draft.AccessPolicy,
                    BoardId = form.Draft.BoardId
                },
                PublishedByUserId = prefix + "-user",
                PublishedAt = now
            });
            var submission = await submissions.CreateAsync(new IntakeSubmissionDocument
            {
                Id = prefix + "-submission",
                OrganizationId = form.OrganizationId,
                FormId = form.Id,
                FormVersion = published.DefinitionVersion,
                ProjectId = form.ProjectId,
                BoardId = form.Draft.BoardId,
                AccessPolicy = IntakeAccessPolicies.Public,
                SubmittedByUserId = "public",
                IdempotencyKeyHash = prefix + "-key",
                RequestFingerprint = prefix + "-fingerprint",
                ConfirmationCode = "ZMB-CONTRACT",
                State = IntakeSubmissionStates.New,
                WorkItemId = prefix + "-work-item",
                CreatedAt = now,
                UpdatedAt = now
            });

            var stale = await forms.SelectAsync(x => x.Id == form.Id);
            form.Name = "Updated form";
            var replaced = await forms.ReplaceByVersionAsync(
                x => x.Id == form.Id && x.OrganizationId == form.OrganizationId,
                form,
                form.Version);
            Assert.True(replaced.Found);
            stale!.Name = "Stale update";
            await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                forms.ReplaceByVersionAsync(
                    x => x.Id == stale.Id,
                    stale,
                    stale.Version));

            Assert.Null(await forms.SelectAsync(
                x => x.Id == form.Id && x.OrganizationId == prefix + "-foreign"));
            Assert.NotNull(await versions.SelectAsync(
                x => x.FormId == form.Id && x.DefinitionVersion == 1));
            var queue = await submissions.ListByFilterAsync(
                x => x.OrganizationId == form.OrganizationId
                    && x.FormId == form.Id
                    && x.State == IntakeSubmissionStates.New,
                x => x.CreatedAt,
                orderDescending: true,
                pageSize: 20);
            Assert.Equal(submission.Id, Assert.Single(queue).Id);
            await Assert.ThrowsAsync<DocumentConflictException>(() =>
                submissions.CreateAsync(new IntakeSubmissionDocument
                {
                    Id = submission.Id,
                    OrganizationId = form.OrganizationId,
                    FormId = form.Id,
                    IdempotencyKeyHash = prefix + "-duplicate"
                }));
        }
        finally
        {
            await submissions.DeleteByFilterAsync(x => x.FormId == form.Id);
            await versions.DeleteByFilterAsync(x => x.FormId == form.Id);
            await forms.DeleteByFilterAsync(x => x.Id == form.Id);
        }
    }
}
