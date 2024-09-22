using NRA.Broker.AbsTcp.Interface;

namespace NRA.Broker.AbsTcp.IO;

public struct AbsTcpResponse : IAbsTcpResponse
{
    public Guid ResGuid { get; set; }
    public byte[] ResData { get; set; }
}