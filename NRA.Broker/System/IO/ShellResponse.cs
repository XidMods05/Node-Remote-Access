namespace NRA.Broker.System.IO;

public struct ShellResponse
{
    public Guid Id { get; set; }
    public required string JsonData { get; set; }
}