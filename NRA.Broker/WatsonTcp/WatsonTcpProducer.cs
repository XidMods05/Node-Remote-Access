using System.Text;
using NRA.Project.Security;
using NRA.Utilities.Cons;
using WatsonTcp;

namespace NRA.Broker.WatsonTcp;

/// <summary>
///     Represents a WatsonTcp producer that connects to a remote server and sends messages.
/// </summary>
public class WatsonTcpProducer
{
    /// <summary>
    ///     The WatsonTcp client used for communication.
    /// </summary>
    public readonly WatsonTcpClient WatsonTcpClient;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WatsonTcpProducer" /> class.
    /// </summary>
    /// <param name="host">The host name or IP address of the remote server.</param>
    /// <param name="port">The port number of the remote server.</param>
    /// <param name="syncFunc">A function that handles synchronous requests received from the remote server.</param>
    /// <param name="key">The encryption key (32 len) for symmetric encryption.</param>
    /// <param name="nonce">The nonce (8 len) for symmetric encryption.</param>
    public WatsonTcpProducer(string host, int port, Func<SyncRequest, Task<SyncResponse>> syncFunc, byte[] key,
        byte[] nonce)
    {
        WatsonTcpClient = new WatsonTcpClient(host, port);

        WatsonTcpClient.Events.MessageReceived += Mr!;

        WatsonTcpClient.Callbacks.SyncRequestReceivedAsync += f => syncFunc(new SyncRequest(f.Client,
            f.ConversationGuid, f.ExpirationUtc, f.Metadata,
            Cipher.SymHide(key, nonce, f.Data)));

        WatsonTcpClient.Connect();
    }

    /// <summary>
    ///     Handles message received events from the WatsonTcp client.
    /// </summary>
    /// <param name="sender">The sender of the event.</param>
    /// <param name="args">The event arguments containing the received message data and metadata.</param>
    private void Mr(object sender, MessageReceivedEventArgs args)
    {
        try
        {
            Logger.Log(Logger.Prefixes.Tcp, $"New message received!" +
                                            $"\n  -> Message data length: {args.Data.Length}." +
                                            $"\n  -> Message metadata: {WatsonTcpClient.SerializationHelper.SerializeJson(args.Metadata)}.");

            switch (Encoding.UTF8.GetString(args.Data))
            {
                case "error":
                    Logger.Log(Logger.Prefixes.Error, $"Error received! " +
                                                      $"JData: {WatsonTcpClient.SerializationHelper.SerializeJson(args.Metadata)}.");
                    break;
            }
        }
        catch (Exception e)
        {
            Logger.Log(Logger.Prefixes.Error, e);
        }
    }
}