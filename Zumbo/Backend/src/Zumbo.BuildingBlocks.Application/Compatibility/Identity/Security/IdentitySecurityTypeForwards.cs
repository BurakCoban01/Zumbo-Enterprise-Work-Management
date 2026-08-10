using System.Runtime.CompilerServices;
using Zumbo.BuildingBlocks.Application.Security;

[assembly: TypeForwardedTo(typeof(ApiKeyScopes))]
[assembly: TypeForwardedTo(typeof(IPasswordHasher))]
[assembly: TypeForwardedTo(typeof(ITokenIssuer))]
[assembly: TypeForwardedTo(typeof(JwtOptions))]
[assembly: TypeForwardedTo(typeof(PermissionCatalog))]
[assembly: TypeForwardedTo(typeof(TokenUser))]
