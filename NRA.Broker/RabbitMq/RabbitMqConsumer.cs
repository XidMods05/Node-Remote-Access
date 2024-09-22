using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NRA.Broker.RabbitMq;

/// <summary>
///     Represents a RabbitMQ consumer.
/// </summary>
/// <param name="hostName">The host name of the RabbitMQ server.</param>
/// <param name="node">The name of the queue to consume messages from.</param>
public class RabbitMqConsumer(string hostName, string node)
{
    /// <summary>
    ///     Gets the RabbitMQ channel for this consumer.
    /// </summary>
    public IModel Channel { get; private set; } = null!;

    /// <summary>
    ///     Gets the current state of the consumer.
    /// </summary>
    public int State { get; private set; }

    /// <summary>
    ///     Creates a new RabbitMQ channel and declares the queue.
    /// </summary>
    public void Create()
    {
        var factory = new ConnectionFactory { HostName = hostName };
        var connection = factory.CreateConnection();
        var channel = connection.CreateModel();

        channel.QueueDeclare(node, true, false, true, null);
        Channel = channel;

        State = 1;
    }

    /// <summary>
    ///     Consumes messages from the RabbitMQ queue.
    /// </summary>
    /// <param name="receivedCallback">The callback to be invoked when a message is received.</param>
    /// <param name="inNewThread">
    ///     If set to <c>true</c>, the received callback will be invoked in a new thread.
    ///     If set to <c>false</c>, the received callback will be invoked in the current thread.
    /// </param>
    public void ConsumeMessages(Action<byte[]> receivedCallback, bool inNewThread)
    {
        State = 2;

        try
        {
            var consumer = new EventingBasicConsumer(Channel);

            consumer.Received += (_, ea) =>
            {
                if (!inNewThread) receivedCallback(ea.Body.ToArray());
                else
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Thread.Yield();
                        receivedCallback(ea.Body.ToArray());
                        Thread.Sleep(10);
                    });
            };

            Channel.BasicConsume(node, true, consumer);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Thread.Sleep(1500);

            ConsumeMessages(receivedCallback, inNewThread);
        }
    }
}