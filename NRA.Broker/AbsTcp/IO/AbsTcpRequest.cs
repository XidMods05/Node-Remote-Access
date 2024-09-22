using NRA.Broker.AbsTcp.Interface;

namespace NRA.Broker.AbsTcp.IO;

public struct AbsTcpRequest : IAbsTcpRequest
{
    public Guid? ReqGuid { get; set; }
    public byte[] ReqData { get; set; }
}