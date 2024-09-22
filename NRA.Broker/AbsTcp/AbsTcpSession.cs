using System.Text;
using Newtonsoft.Json;
using NRA.Broker.AbsTcp.IO;
using NRA.Broker.AbsTcp.Netbase;
using NRA.Broker.AbsTcp.Netbase.WWW;
using NRA.Project.Security;
using NRA.Utilities.Cons;

namespace NRA.Broker.AbsTcp;

/// <summary>
///     Represents an AbsTcpSession that handles incoming and outgoing data for a TCP connection.
/// </summary>
/// <param name="tcpServer">The TCP server that this session is associated with.</param>
/// <param name="reqFunc">A function that processes incoming requests and returns responses.</param>
/// <param name="key">The encryption key (32 len) used for symmetric encryption.</param>
/// <param name="nonce">The nonce (8 len) used for symmetric encryption.</param>
internal class AbsTcpSession(TcpServer tcpServer, Func<AbsTcpRequest, AbsTcpResponse> reqFunc, byte[] key, byte[] nonce)
    : TcpSession(tcpServer)
{
    protected override void OnConnected()
    {
        base.OnConnected();

        try
        {
            Logger.Log(Logger.Prefixes.Tcp, $"New AbsTcpSession created! " +
                                            $"Information: Id = {Id}. " +
                                            $"TcpServer info: Ip = {Server.Address}; Port = {Server.Port}.");
        }
        catch
        {
            Logger.Log(Logger.Prefixes.Cmd,
                $"DOS warning on server: Ip = {Server.Address}; Port = {Server.Port}.");
        }
    }

    protected override void OnDisconnected()
    {
        base.OnDisconnected();

        try
        {
            Logger.Log(Logger.Prefixes.Tcp, $"AbsTcpSession closed! " +
                                            $"Information: Id = {Id}. " +
                                            $"TcpServer info: Ip = {Server.Address}; Port = {Server.Port}.");
        }
        catch
        {
            Logger.Log(Logger.Prefixes.Cmd,
                $"DDOS warning on server: Ip = {Server.Address}; Port = {Server.Port}.");
        }
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        base.OnReceived(buffer, offset, size);

        buffer = buffer.Take((int)size).ToArray();
        buffer = Cipher.SymHide(key, nonce, buffer);

        var req = JsonConvert.DeserializeObject<AbsTcpRequest>(Encoding.UTF8.GetString(buffer));
        var res = reqFunc(req);

        res.ResGuid = req.ReqGuid!.Value;

        SendAsync(Cipher.SymHide(key, nonce, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(res))));
    }
}