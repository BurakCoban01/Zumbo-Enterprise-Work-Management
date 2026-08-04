using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Infrastructure.Storage;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

internal sealed record InspectedAttachmentContent(
    MemoryStream BufferedContent,
    string FileName,
    string ContentType);
