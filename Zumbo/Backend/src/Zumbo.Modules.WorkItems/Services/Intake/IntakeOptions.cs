using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class IntakeOptions
{
    public int MaxFields { get; init; } = 40;
    public int MaxValues { get; init; } = 40;
    public int MaxAttachments { get; init; } = 5;
    public long MaxAttachmentBytes { get; init; } = 10 * 1024 * 1024;
    public long MaxTotalAttachmentBytes { get; init; } = 25 * 1024 * 1024;
    public int MaxValueCharacters { get; init; } = 4_000;
    public int MaxTotalValueCharacters { get; init; } = 20_000;
}
