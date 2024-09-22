using System.Net.Sockets;
using NRA.Broker.AbsTcp.IO;
using NRA.Broker.AbsTcp.Netbase;
using NRA.Broker.AbsTcp.Netbase.WWW;
using NRA.Utilities.Cons;

namespace NRA.Broker.AbsTcp;

/// <summary>
///     Represents an abstract TCP server that handles incoming requests and responses.
/// </summary>
/// <param name="address">The IP address or hostname to listen on.</param>
/// <param name="port">The port number to listen on.</param>
/// <param name="reqFunc">A function that processes incoming requests and returns responses.</param>
/// <param name="key">A byte array representing the encryption key (32 len).</param>
/// <param name="nonce">A byte array representing the nonce (8 len) for encryption.</param>
public class AbsTcpServer(
    string address,
    int port,
    Func<AbsTcpRequest, AbsTcpResponse> reqFunc,
    byte[] key,
    byte[] nonce)
    : TcpServer(address, port)
{
    /// <summary>
    ///     Starts the server and listens for incoming connections.
    /// </summary>
    /// <returns>True if the server started successfully; otherwise, false.</returns>
    public override bool Start()
    {
        OptionAcceptorBacklog = int.MaxValue;
        OptionNoDelay = true;

        var r = base.Start();

        Logger.Log(Logger.Prefixes.Start,
            $"New AbsTcpServer started! Listening endpoint: {Endpoint}.");
        return r;
    }

    /// <summary>
    ///     Creates a new session for handling incoming connections.
    /// </summary>
    /// <returns>A new instance of <see cref="AbsTcpSession" />.</returns>
    protected override TcpSession CreateSession()
    {
        return new AbsTcpSession(this, reqFunc, key, nonce);
    }

    /// <summary>
    ///     Handles errors that occur during the server's operation.
    /// </summary>
    /// <param name="error">The type of socket error that occurred.</param>
    protected override void OnError(SocketError error)
    {
        base.OnError(error);

        Logger.Log(Logger.Prefixes.Error,
            $"New error handled in AbsTcpServer-({Endpoint})! Error: {error}.");
    }
}