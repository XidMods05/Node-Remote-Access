using RabbitMQ.Client;

namespace NRA.Broker.RabbitMq;

/// <summary>
///     Represents a RabbitMQ producer that sends messages to a specified node.
/// </summary>
/// <param name="hostName">The hostname of the RabbitMQ server.</param>
/// <param name="node">The name of the node to which messages will be sent.</param>
public class RabbitMqProducer(string hostName, string node)
{
    private readonly object _providerLock = new();

    /// <summary>
    ///     Gets or sets the RabbitMQ channel for sending messages.
    /// </summary>
    public IModel Channel { get; private set; } = null!;

    /// <summary>
    ///     Gets or sets the state of the producer.
    /// </summary>
    public int State { get; private set; }

    /// <summary>
    ///     Creates a RabbitMQ connection and channel for sending messages.
    /// </summary>
    public void Create()
    {
        var factory = new ConnectionFactory { HostName = hostName };
        var connection = factory.CreateConnection();

        Channel = connection.CreateModel();
        State = 1;
    }

    /// <summary>
    ///     Sends a message to the specified RabbitMQ node.
    /// </summary>
    /// <param name="message">The message to be sent.</param>
    /// <returns>The sent message.</returns>
    public byte[] SendMessage(byte[] message)
    {
        lock (_providerLock)
        {
            Channel.BasicPublish("", node, null, message);
        }

        return message;
    }
}