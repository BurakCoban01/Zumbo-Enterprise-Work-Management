using System.Globalization;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

internal sealed class IfMatchValidationException()
    : ZumboException("IF_MATCH_INVALID", "If-Match must contain a positive numeric resource version.");
