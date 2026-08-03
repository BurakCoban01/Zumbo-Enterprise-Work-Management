using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;

public static class ZumboAuthenticationSchemes
{
    public const string Smart = "ZumboAuth";
    public const string ApiKey = "ApiKey";
}
