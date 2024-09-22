using System.Collections.Concurrent;
using System.Text;
using Newtonsoft.Json;
using NRA.Broker.AbsTcp.Interface;
using NRA.Broker.AbsTcp.IO;
using NRA.Broker.AbsTcp.Netbase;
using NRA.Project.Security;

namespace NRA.Broker.AbsTcp;

/// <summary>
///     Represents a client for the AbsTcp protocol.
/// </summary>
/// <param name="address">The address of the server to connect to.</param>
/// <param name="port">The port number of the server to connect to.</param>
/// <param name="key">The encryption key (32 len) for the communication.</param>
/// <param name="nonce">The nonce (8 len) for the communication.</param>
public class AbsTcpClient(string address, int port, byte[] key, byte[] nonce) : TcpClient(address, port)
{
    private readonly ConcurrentDictionary<Guid, AbsTcpResponse?> _absTcpResponses = new();

    /// <summary>
    ///     Sends an AbsTcp request and waits for the response.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="timeoutSeconds">The timeout for waiting for the response in seconds. Default is 3 seconds.</param>
    /// <returns>The response received from the server, or null if the response is not received within the timeout.</returns>
    private AbsTcpResponse? SendAndWaitInternal(AbsTcpRequest request, int timeoutSeconds = 3)
    {
        request.ReqGuid ??= Guid.NewGuid();
        _absTcpResponses.TryAdd(request.ReqGuid.Value, null);

        SendAsync(Cipher.SymHide(key, nonce,
            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request))));

        var endTime = DateTime.Now.AddSeconds(timeoutSeconds);
        while (DateTime.Now < endTime && _absTcpResponses[request.ReqGuid.Value] == null) Thread.Yield();

        return !_absTcpResponses.TryRemove(request.ReqGuid.Value, out var value1) ? default : value1;
    }

    /// <summary>
    ///     Sends an AbsTcp request and waits for the response.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="timeoutSeconds">The timeout for waiting for the response in seconds. Default is 3 seconds.</param>
    /// <returns>The response received from the server, or null if the response is not received within the timeout.</returns>
    public IAbsTcpResponse? SendAndWait(AbsTcpRequest request, int timeoutSeconds = 3)
    {
        return SendAndWaitInternal(request, timeoutSeconds);
    }

    /// <summary>
    ///     Called when data is received from the server.
    /// </summary>
    /// <param name="buffer">The received data buffer.</param>
    /// <param name="offset">The offset in the buffer where the data starts.</param>
    /// <param name="size">The size of the received data.</param>
    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        buffer = buffer.Take((int)size).ToArray();
        buffer = Cipher.SymHide(key, nonce, buffer);

        var response = JsonConvert.DeserializeObject<AbsTcpResponse>(Encoding.UTF8.GetString(buffer));

        if (_absTcpResponses.ContainsKey(response.ResGuid))
            _absTcpResponses[response.ResGuid] = response;
    }
}