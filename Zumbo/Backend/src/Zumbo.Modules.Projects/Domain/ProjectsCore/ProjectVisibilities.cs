namespace Zumbo.Modules.Projects;

public static class ProjectVisibilities
{
    public const string Internal = "Internal";
    public const string Private = "Private";

    public static string Normalize(string? visibility)
    {
        if (string.IsNullOrWhiteSpace(visibility)
            || visibility.Equals(Internal, StringComparison.OrdinalIgnoreCase))
        {
            return Internal;
        }

        if (visibility.Equals(Private, StringComparison.OrdinalIgnoreCase))
        {
            return Private;
        }

        throw new Zumbo.SharedKernel.ValidationException("Project visibility must be Internal or Private.");
    }
}
