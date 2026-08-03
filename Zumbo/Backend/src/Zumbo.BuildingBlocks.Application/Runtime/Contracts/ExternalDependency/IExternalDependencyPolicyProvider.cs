namespace Zumbo.BuildingBlocks.Application.Runtime;

public interface IExternalDependencyPolicyProvider
{
    IExternalDependencyPolicy Get(string dependency);
    IReadOnlyList<ExternalDependencySnapshot> GetSnapshots();
}
