using System.Globalization;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

public sealed class HttpExpectedVersionAccessor(IHttpContextAccessor httpContextAccessor)
    : IExpectedVersionAccessor
{
    public long? ExpectedVersion
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers.IfMatch.ToString().Trim();
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            {
                value = value[2..].Trim();
            }

            value = value.Trim('"');
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                || version <= 0)
            {
                throw new IfMatchValidationException();
            }

            return version;
        }
    }
}
