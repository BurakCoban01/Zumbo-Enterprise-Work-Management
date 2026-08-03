using System.Linq.Expressions;

namespace Zumbo.BuildingBlocks.Application.Persistence;

public interface IVersionedDocument : IDocument
{
    long Version { get; set; }
}
