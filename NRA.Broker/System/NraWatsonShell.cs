using System.Text;
using NRA.Broker.System.Abstract;
using NRA.Broker.System.IO;
using NRA.Broker.WatsonTcp;
using NRA.Project.Security;
using WatsonTcp;

namespace NRA.Broker.System;

/// <summary>
///     Represents a shell for communication using WatsonTcp.
/// </summary>
/// <param name="host">The host address for the WatsonTcp server.</param>
/// <param name="port">The port number for the WatsonTcp server.</param>
/// <param name="key">The encryption key (32 len) for symmetric encryption.</param>
/// <param name="nonce">The nonce (8 len) for symmetric encryption.</param>
public class NraWatsonShell(string host, int port, byte[] key, byte[] nonce) : IShell
{
    private WatsonTcpProducer? _watsonTcpC;
    private WatsonTcpConsumer? _watsonTcpS;

    /// <summary>
    ///     Gets the unique identifier of the shell.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    ///     Adds a request to the shell for processing.
    /// </summary>
    /// <param name="request">The request to be processed.</param>
    /// <param name="response">The action to be executed when the response is received.</param>
    public void AddReq(ShellRequest request, Action<ShellResponse> response)
    {
        var data = Cipher.SymHide(key, nonce, Encoding.UTF8.GetBytes(request.JsonData));

        if (_watsonTcpS != null)
        {
            var r = _watsonTcpS!.WatsonTcpServer.SendAndWaitAsync(3000, request.Id, data);
            response(new ShellResponse { Id = request.Id, JsonData = Encoding.UTF8.GetString(r.Result.Data) });
            return;
        }

        if (_watsonTcpC == null) throw new Exception("Client or Server not created!");

        var tr = _watsonTcpC.WatsonTcpClient.SendAndWaitAsync(3000, data);
        response(new ShellResponse { Id = request.Id, JsonData = Encoding.UTF8.GetString(tr.Result.Data) });
    }

    /// <summary>
    ///     Creates a WatsonTcp server for the shell.
    /// </summary>
    /// <param name="func">The function to handle incoming requests.</param>
    public void CreateServer(Func<SyncRequest, Task<SyncResponse>> func)
    {
        if (_watsonTcpC != null) throw new Exception("Client already created!");

        _watsonTcpS = new WatsonTcpConsumer(host, port, func, key, nonce);
        _watsonTcpS.Start();

        Id = _watsonTcpS.WatsonTcpServer.Settings.Guid;
    }

    /// <summary>
    ///     Creates a WatsonTcp client for the shell.
    /// </summary>
    /// <param name="func">The function to handle outgoing requests.</param>
    public void CreateClient(Func<SyncRequest, Task<SyncResponse>> func)
    {
        if (_watsonTcpS != null) throw new Exception("Server already created!");

        _watsonTcpC = new WatsonTcpProducer(host, port, func, key, nonce);

        Id = _watsonTcpC.WatsonTcpClient.Settings.Guid;
    }
}