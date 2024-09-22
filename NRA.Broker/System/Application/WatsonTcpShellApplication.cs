using NRA.Broker.System.Abstract;
using WatsonTcp;

namespace NRA.Broker.System.Application;

/// <summary>
///     Represents an application shell for communication using WatsonTcp.
/// </summary>
public class WatsonTcpShellApplication
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WatsonTcpShellApplication" /> class.
    ///     This class is responsible for managing a WatsonTcpShell instance, either as a server or client.
    /// </summary>
    /// <param name="host">The host address for the WatsonTcpShell instance.</param>
    /// <param name="port">The port number for the WatsonTcpShell instance.</param>
    /// <param name="isServer">A boolean indicating whether the instance should be a server or client.</param>
    /// <param name="reqFunc">A function that handles incoming requests and returns responses.</param>
    /// <param name="key">A byte array representing the encryption key (32 len) for the WatsonTcpShell instance.</param>
    /// <param name="nonce">A byte array representing the nonce (8 len) for the WatsonTcpShell instance.</param>
    public WatsonTcpShellApplication(string host, int port, bool isServer,
        Func<SyncRequest, Task<SyncResponse>> reqFunc, byte[] key, byte[] nonce)
    {
        if (isServer)
        {
            var server = new NraWatsonShell(host, port, key, nonce);
            server.CreateServer(reqFunc);

            Shell = server;
            Id = server.Id;

            return;
        }

        var client = new NraWatsonShell(host, port, key, nonce);
        client.CreateClient(reqFunc);

        Id = client.Id;
        Shell = client;
    }

    /// <summary>
    ///     Gets the shell instance associated with this application.
    /// </summary>
    public IShell Shell { get; }

    /// <summary>
    ///     Gets the unique identifier of the shell instance associated with this application.
    /// </summary>
    public Guid Id { get; }
}