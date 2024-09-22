using System.Text;
using NRA.Project.Security;
using NRA.Utilities.Cons;
using WatsonTcp;

namespace NRA.Broker.WatsonTcp;

/// <summary>
///     Represents a Watson TCP consumer for handling client connections and messages.
/// </summary>
public class WatsonTcpConsumer
{
    private readonly int _port;

    /// <summary>
    ///     The Watson TCP server instance.
    /// </summary>
    public readonly WatsonTcpServer WatsonTcpServer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WatsonTcpConsumer" /> class.
    /// </summary>
    /// <param name="host">The host IP or name.</param>
    /// <param name="port">The port number to listen on.</param>
    /// <param name="syncFunc">The function to handle synchronous requests.</param>
    /// <param name="key">The encryption key (32 len).</param>
    /// <param name="nonce">The nonce for encryption (8 len).</param>
    public WatsonTcpConsumer(string host, int port, Func<SyncRequest, Task<SyncResponse>> syncFunc, byte[] key,
        byte[] nonce)
    {
        _port = port;

        WatsonTcpServer = new WatsonTcpServer(host, port);
        {
            WatsonTcpServer.Events.ClientConnected += Cc!;
            WatsonTcpServer.Events.ClientDisconnected += Cd!;
            WatsonTcpServer.Events.MessageReceived += Mr!;
        }

        WatsonTcpServer.Callbacks.SyncRequestReceivedAsync += f => syncFunc(new SyncRequest(f.Client,
            f.ConversationGuid, f.ExpirationUtc, f.Metadata,
            Cipher.SymHide(key, nonce, f.Data)));
    }

    /// <summary>
    ///     Starts the Watson TCP server.
    /// </summary>
    public void Start()
    {
        WatsonTcpServer.Start();

        Logger.Log(Logger.Prefixes.Start, $"Watson consumer ({_port}) launched! {WatsonTcpServer.Settings.Guid}.");
    }

    /// <summary>
    ///     Stops the Watson TCP server.
    /// </summary>
    public void Stop()
    {
        WatsonTcpServer.Stop();

        Logger.Log(Logger.Prefixes.Stop, $"Watson consumer ({_port}) stopped! {WatsonTcpServer.Settings.Guid}.");
    }

    private void Cc(object sender, ConnectionEventArgs args)
    {
        Logger.Log(Logger.Prefixes.Tcp, $"New client connection detected! {args.Client.IpPort}.");
    }

    private void Cd(object sender, DisconnectionEventArgs args)
    {
        Logger.Log(Logger.Prefixes.Tcp, $"New client disconnection detected ({args.Reason})! {args.Client.IpPort}.");
    }

    private void Mr(object sender, MessageReceivedEventArgs args)
    {
        try
        {
            Logger.Log(Logger.Prefixes.Tcp, $"New message received from {args.Client.IpPort}!" +
                                            $"\n  -> Message data length: {args.Data.Length}." +
                                            $"\n  -> Message metadata: {WatsonTcpServer.SerializationHelper.SerializeJson(args.Metadata)}.");

            switch (Encoding.UTF8.GetString(args.Data))
            {
                case "error":
                    Logger.Log(Logger.Prefixes.Error, $"Error received from {args.Client.IpPort}! " +
                                                      $"JData: {WatsonTcpServer.SerializationHelper.SerializeJson(args.Metadata)}.");
                    break;
            }
        }
        catch (Exception e)
        {
            Logger.Log(Logger.Prefixes.Error, e);
        }
    }
}