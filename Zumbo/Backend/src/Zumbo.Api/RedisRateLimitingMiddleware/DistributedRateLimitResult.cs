using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Zumbo.SharedKernel;

internal sealed record DistributedRateLimitResult(
    bool IsAllowed,
    long Remaining,
    TimeSpan RetryAfter);
