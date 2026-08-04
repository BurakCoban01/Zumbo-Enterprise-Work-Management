using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record ProjectMemberRole : ValueObject
{
    private ProjectMemberRole(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public bool IsProjectAdmin => Value == "ProjectAdmin";

    public static ProjectMemberRole Create(string? role)
    {
        if (string.Equals(role, "ProjectAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectMemberRole("ProjectAdmin");
        }

        if (string.IsNullOrWhiteSpace(role)
            || string.Equals(role, "Developer", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectMemberRole("Developer");
        }

        if (string.Equals(role, "Viewer", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectMemberRole("Viewer");
        }

        throw new ValidationException("Project member role must be ProjectAdmin, Developer or Viewer.");
    }

    public override string ToString() => Value;
}
