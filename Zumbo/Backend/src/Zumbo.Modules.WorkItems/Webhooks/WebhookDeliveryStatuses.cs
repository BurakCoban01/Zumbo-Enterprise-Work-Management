using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public static class WebhookDeliveryStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Delivered = "Delivered";
    public const string DeadLetter = "DeadLetter";
}
