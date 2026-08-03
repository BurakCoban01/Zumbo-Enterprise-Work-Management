namespace Zumbo.BuildingBlocks.Application.Messaging;

public static class DurableMessageStates
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string DeadLetter = "DeadLetter";
}
