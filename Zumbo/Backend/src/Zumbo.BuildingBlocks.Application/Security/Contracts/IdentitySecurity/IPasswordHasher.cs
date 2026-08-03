namespace Zumbo.BuildingBlocks.Application.Security;

public interface IPasswordHasher
{
    string Hash(string plainPassword);
    bool Verify(string plainPassword, string passwordHash);
    bool NeedsRehash(string passwordHash);
}
