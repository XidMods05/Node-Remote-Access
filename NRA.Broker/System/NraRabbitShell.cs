using System.Collections.Concurrent;
using System.Text;
using Newtonsoft.Json;
using NRA.Broker.RabbitMq;
using NRA.Broker.System.Abstract;
using NRA.Broker.System.IO;

namespace NRA.Broker.System;

/// <summary>
///     Represents a shell for communication with a remote server using RabbitMQ.
/// </summary>
public class NraRabbitShell : IShell
{
    private readonly Action<ShellRequest> _actionRequest;
    private readonly RabbitMqProducer _remoteConsumer;

    private readonly ConcurrentDictionary<Guid, Action<ShellResponse>> _responseActions = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="NraRabbitShell" /> class.
    /// </summary>
    /// <param name="serverConsumer">The RabbitMQ consumer for receiving messages from the server.</param>
    /// <param name="remoteConsumer">The RabbitMQ producer for sending messages to the remote server.</param>
    /// <param name="actionRequest">The action to be executed when a request is received.</param>
    public NraRabbitShell(RabbitMqConsumer serverConsumer, RabbitMqProducer remoteConsumer,
        Action<ShellRequest> actionRequest)
    {
        if (serverConsumer.State != 0) throw new InvalidDataException("Incorrect serverConsumer.State!");
        if (remoteConsumer.State != 0) throw new InvalidDataException("Incorrect remoteConsumer.State!");
        _remoteConsumer = remoteConsumer;
        _actionRequest = actionRequest;

        serverConsumer.Create();
        serverConsumer.ConsumeMessages(OnReceived, false);

        remoteConsumer.Create();
    }

    /// <summary>
    ///     Adds a request to the shell and associates it with a response action.
    /// </summary>
    /// <param name="request">The request to be sent to the remote server.</param>
    /// <param name="response">The action to be executed when a response is received for the given request.</param>
    public void AddReq(ShellRequest request, Action<ShellResponse> response)
    {
        _remoteConsumer.SendMessage(new byte[] { 200, 7, 0, 5, 3, 80, 85, 90, 2, 10 }
            .Concat(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request))).ToArray());
        _responseActions.TryAdd(request.Id, response);
    }

    /// <summary>
    ///     Handles incoming messages from the RabbitMQ server.
    /// </summary>
    /// <param name="data">The byte array representing the incoming message.</param>
    private void OnReceived(byte[] data)
    {
        if (data.Take(10).SequenceEqual(new byte[] { 200, 7, 0, 5, 3, 80, 85, 90, 2, 10 }))
        {
            data = data.Skip(10).ToArray();

            _actionRequest(JsonConvert.DeserializeObject<ShellRequest>(Encoding.UTF8.GetString(data)));
            return;
        }

        if (!data.Take(10).SequenceEqual(new byte[] { 55, 80, 7, 9, 4, 11, 240, 8, 0, 1 })) return;
        data = data.Skip(10).ToArray();

        var r = JsonConvert.DeserializeObject<ShellResponse>(Encoding.UTF8.GetString(data));

        if (!_responseActions.TryRemove(r.Id, out var response)) return;
        if (response == null!) return;

        response(r);
    }

    /// <summary>
    ///     Adds a response to the shell for a given request.
    /// </summary>
    /// <param name="request">The original request for which the response is being sent.</param>
    /// <param name="response">The response to be sent to the remote server.</param>
    public void AddRes(ShellRequest request, ShellResponse response)
    {
        response.Id = request.Id;

        _remoteConsumer.SendMessage(new byte[] { 55, 80, 7, 9, 4, 11, 240, 8, 0, 1 }
            .Concat(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response))).ToArray());
    }
}