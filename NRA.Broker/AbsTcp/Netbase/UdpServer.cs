using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Buffer = NRA.Broker.AbsTcp.Netbase.WWW.Buffer;

namespace NRA.Broker.AbsTcp.Netbase;

internal class UdpServer : IDisposable
{
    /// <summary>
    ///     Initialize UDP server with a given IP address and port number
    /// </summary>
    /// <param name="address">IP address</param>
    /// <param name="port">Port number</param>
    internal UdpServer(IPAddress address, int port) : this(new IPEndPoint(address, port))
    {
    }

    /// <summary>
    ///     Initialize UDP server with a given IP address and port number
    /// </summary>
    /// <param name="address">IP address</param>
    /// <param name="port">Port number</param>
    internal UdpServer(string address, int port) : this(new IPEndPoint(IPAddress.Parse(address), port))
    {
    }

    /// <summary>
    ///     Initialize UDP server with a given DNS endpoint
    /// </summary>
    /// <param name="endpoint">DNS endpoint</param>
    internal UdpServer(DnsEndPoint endpoint) : this(endpoint, endpoint.Host, endpoint.Port)
    {
    }

    /// <summary>
    ///     Initialize UDP server with a given IP endpoint
    /// </summary>
    /// <param name="endpoint">IP endpoint</param>
    internal UdpServer(IPEndPoint endpoint) : this(endpoint, endpoint.Address.ToString(), endpoint.Port)
    {
    }

    /// <summary>
    ///     Initialize UDP server with a given endpoint, address and port
    /// </summary>
    /// <param name="endpoint">Endpoint</param>
    /// <param name="address">Server address</param>
    /// <param name="port">Server port</param>
    internal UdpServer(EndPoint endpoint, string address, int port)
    {
        Id = Guid.NewGuid();
        Address = address;
        Port = port;
        Endpoint = endpoint;
    }

    /// <summary>
    ///     Server Id
    /// </summary>
    internal Guid Id { get; }

    /// <summary>
    ///     UDP server address
    /// </summary>
    internal string Address { get; }

    /// <summary>
    ///     UDP server port
    /// </summary>
    internal int Port { get; }

    /// <summary>
    ///     Endpoint
    /// </summary>
    internal EndPoint Endpoint { get; set; }

    /// <summary>
    ///     Multicast endpoint
    /// </summary>
    internal EndPoint MulticastEndpoint { get; set; } = null!;

    /// <summary>
    ///     Socket
    /// </summary>
    internal Socket Socket { get; set; } = null!;

    /// <summary>
    ///     Number of bytes pending sent by the server
    /// </summary>
    internal long BytesPending { get; set; }

    /// <summary>
    ///     Number of bytes sending by the server
    /// </summary>
    internal long BytesSending { get; set; }

    /// <summary>
    ///     Number of bytes sent by the server
    /// </summary>
    internal long BytesSent { get; set; }

    /// <summary>
    ///     Number of bytes received by the server
    /// </summary>
    internal long BytesReceived { get; set; }

    /// <summary>
    ///     Number of datagrams sent by the server
    /// </summary>
    internal long DatagramsSent { get; set; }

    /// <summary>
    ///     Number of datagrams received by the server
    /// </summary>
    internal long DatagramsReceived { get; set; }

    /// <summary>
    ///     Option: dual mode socket
    /// </summary>
    /// <remarks>
    ///     Specifies whether the Socket is a dual-mode socket used for both IPv4 and IPv6.
    ///     Will work only if socket is bound on IPv6 address.
    /// </remarks>
    internal bool OptionDualMode { get; set; }

    /// <summary>
    ///     Option: reuse address
    /// </summary>
    /// <remarks>
    ///     This option will enable/disable SO_REUSEADDR if the OS support this feature
    /// </remarks>
    internal bool OptionReuseAddress { get; set; }

    /// <summary>
    ///     Option: enables a socket to be bound for exclusive access
    /// </summary>
    /// <remarks>
    ///     This option will enable/disable SO_EXCLUSIVEADDRUSE if the OS support this feature
    /// </remarks>
    internal bool OptionExclusiveAddressUse { get; set; }

    /// <summary>
    ///     Option: receive buffer limit
    /// </summary>
    internal int OptionReceiveBufferLimit { get; set; } = 0;

    /// <summary>
    ///     Option: receive buffer size
    /// </summary>
    internal int OptionReceiveBufferSize { get; set; } = 6144;

    /// <summary>
    ///     Option: send buffer limit
    /// </summary>
    internal int OptionSendBufferLimit { get; set; } = 0;

    /// <summary>
    ///     Option: send buffer size
    /// </summary>
    internal int OptionSendBufferSize { get; set; } = 8192;

    #region Error handling

    /// <summary>
    ///     Send error notification
    /// </summary>
    /// <param name="error">Socket error code</param>
    internal void SendError(SocketError error)
    {
        // Skip disconnect errors
        if (error is SocketError.ConnectionAborted or SocketError.ConnectionRefused or SocketError.ConnectionReset
            or SocketError.OperationAborted or SocketError.Shutdown)
            return;

        OnError(error);
    }

    #endregion

    #region Connect/Disconnect client

    /// <summary>
    ///     Is the server started?
    /// </summary>
    internal bool IsStarted { get; set; }

    /// <summary>
    ///     Create a new socket object
    /// </summary>
    /// <remarks>
    ///     Method may be override if you need to prepare some specific socket object in your implementation.
    /// </remarks>
    /// <returns>Socket object</returns>
    protected virtual Socket CreateSocket()
    {
        return new Socket(Endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
    }

    /// <summary>
    ///     Start the server (synchronous)
    /// </summary>
    /// <returns>'true' if the server was successfully started, 'false' if the server failed to start</returns>
    internal virtual bool Start()
    {
        Debug.Assert(!IsStarted, "UDP server is already started!");
        if (IsStarted)
            return false;

        // Setup buffers
        ReceiveBuffer = new Buffer();
        SendBuffer = new Buffer();

        // Setup event args
        ReceiveEventArg = new SocketAsyncEventArgs();
        ReceiveEventArg.Completed += OnAsyncCompleted!;
        SendEventArg = new SocketAsyncEventArgs();
        SendEventArg.Completed += OnAsyncCompleted!;

        // Create a new server socket
        Socket = CreateSocket();

        // Update the server socket disposed flag
        IsSocketDisposed = false;

        // Apply the option: reuse address
        Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, OptionReuseAddress);
        // Apply the option: exclusive address use
        Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse,
            OptionExclusiveAddressUse);
        // Apply the option: dual mode (this option must be applied before recieving)
        if (Socket.AddressFamily == AddressFamily.InterNetworkV6)
            Socket.DualMode = OptionDualMode;

        // Bind the server socket to the endpoint
        Socket.Bind(Endpoint);
        // Refresh the endpoint property based on the actual endpoint created
        Endpoint = Socket.LocalEndPoint!;

        // Call the server starting handler
        OnStarting();

        // Prepare receive endpoint
        ReceiveEndpoint =
            new IPEndPoint(Endpoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any,
                0);

        // Prepare receive & send buffers
        ReceiveBuffer.Reserve(OptionReceiveBufferSize);

        // Reset statistic
        BytesPending = 0;
        BytesSending = 0;
        BytesSent = 0;
        BytesReceived = 0;
        DatagramsSent = 0;
        DatagramsReceived = 0;

        // Update the started flag
        IsStarted = true;

        // Call the server started handler
        OnStarted();

        return true;
    }

    /// <summary>
    ///     Start the server with a given multicast IP address and port number (synchronous)
    /// </summary>
    /// <param name="multicastAddress">Multicast IP address</param>
    /// <param name="multicastPort">Multicast port number</param>
    /// <returns>'true' if the server was successfully started, 'false' if the server failed to start</returns>
    internal virtual bool Start(IPAddress multicastAddress, int multicastPort)
    {
        return Start(new IPEndPoint(multicastAddress, multicastPort));
    }

    /// <summary>
    ///     Start the server with a given multicast IP address and port number (synchronous)
    /// </summary>
    /// <param name="multicastAddress">Multicast IP address</param>
    /// <param name="multicastPort">Multicast port number</param>
    /// <returns>'true' if the server was successfully started, 'false' if the server failed to start</returns>
    internal virtual bool Start(string multicastAddress, int multicastPort)
    {
        return Start(new IPEndPoint(IPAddress.Parse(multicastAddress), multicastPort));
    }

    /// <summary>
    ///     Start the server with a given multicast endpoint (synchronous)
    /// </summary>
    /// <param name="multicastEndpoint">Multicast endpoint</param>
    /// <returns>'true' if the server was successfully started, 'false' if the server failed to start</returns>
    internal virtual bool Start(EndPoint multicastEndpoint)
    {
        MulticastEndpoint = multicastEndpoint;
        return Start();
    }

    /// <summary>
    ///     Stop the server (synchronous)
    /// </summary>
    /// <returns>'true' if the server was successfully stopped, 'false' if the server is already stopped</returns>
    internal virtual bool Stop()
    {
        Debug.Assert(IsStarted, "UDP server is not started!");
        if (!IsStarted)
            return false;

        // Reset event args
        ReceiveEventArg.Completed -= OnAsyncCompleted!;
        SendEventArg.Completed -= OnAsyncCompleted!;

        // Call the server stopping handler
        OnStopping();

        try
        {
            // Close the server socket
            Socket.Close();

            // Dispose the server socket
            Socket.Dispose();

            // Dispose event arguments
            ReceiveEventArg.Dispose();
            SendEventArg.Dispose();

            // Update the server socket disposed flag
            IsSocketDisposed = true;
        }
        catch (ObjectDisposedException)
        {
        }

        // Update the started flag
        IsStarted = false;

        // Update sending/receiving flags
        Receiving = false;
        Sending = false;

        // Clear send/receive buffers
        ClearBuffers();

        // Call the server stopped handler
        OnStopped();

        return true;
    }

    /// <summary>
    ///     Restart the server (synchronous)
    /// </summary>
    /// <returns>'true' if the server was successfully restarted, 'false' if the server failed to restart</returns>
    internal virtual bool Restart()
    {
        return Stop() && Start();
    }

    #endregion

    #region Send/Receive data

    // Receive and send endpoints
    internal EndPoint ReceiveEndpoint = null!;

    internal EndPoint SendEndpoint = null!;

    // Receive buffer
    internal bool Receiving;
    internal Buffer ReceiveBuffer = null!;

    internal SocketAsyncEventArgs ReceiveEventArg = null!;

    // Send buffer
    internal bool Sending;
    internal Buffer SendBuffer = null!;
    internal SocketAsyncEventArgs SendEventArg = null!;

    /// <summary>
    ///     Multicast datagram to the prepared mulicast endpoint (synchronous)
    /// </summary>
    /// <param name="buffer">Datagram buffer to multicast</param>
    /// <returns>Size of multicasted datagram</returns>
    internal virtual long Multicast(byte[] buffer)
    {
        return Multicast(buffer.AsSpan());
    }

    /// <summary>
    ///     Multicast datagram to the prepared mulicast endpoint (synchronous)
    /// </summary>
    /// <param name="buffer">Datagram buffer to multicast</param>
    /// <param name="offset">Datagram buffer offset</param>
    /// <param name="size">Datagram buffer size</param>
    /// <returns>Size of multicasted datagram</returns>
    internal virtual long Multicast(byte[] buffer, long offset, long size)
    {
        return Multicast(buffer.AsSpan((int)offset, (int)size));
    }

    /// <summary>
    ///     Multicast datagram to the prepared mulicast endpoint (synchronous)
    /// </summary>
    /// <param name="buffer">Datagram buffer to multicast as a span of bytes</param>
    /// <returns>Size of multicasted datagram</returns>
    internal virtual long Multicast(ReadOnlySpan<byte> buffer)
    {
        return Send(MulticastEndpoint, buffer);
    }

    /// <summary>
    ///     Multicast text to the prepared mulicast endpoint (synchronous)
    /// </summary>
    /// <param name="text">Text string to multicast</param>
    /// <returns>Size of multicasted datagram</returns>
    internal virtual long Multicast(string text)
    {
        return Multicast(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    ///     Multicast text to the prepared mulicast endpoint (synchronous)
    /// </summary>
    /// <param name="text">Text to multicast as a span of characters</param>
    /// <returns>Size of multicasted datagram</returns>
    internal virtual long Multicast(ReadOnlySpan<char> text)
    {
        return Multicast(Encoding.UTF8.GetBytes(text.ToArray()));
    }

    /// <summary>
    ///     Multicast datagram to the prepared mulicast endpoint (asynchronous)
    /// </summary>
    /// <param name="buffer">Datagram buffer to multicast</param>
    /// <returns>'true' if the datagram was successfully multicasted, 'false' if the datagram was not multicasted</returns>
    internal virtual bool MulticastAsync(byte[] buffer)
    {
        return MulticastAsync(buffer.AsSpan());
    }

    /// <summary>
    ///     Multicast datagram to the prepared mulicast endpoint (asynchronous)
    /// </summary>
    /// <param name="buffer">Datagram buffer to multicast</param>
    /// <param name="offset">Datagram buffer offset</param>
    /// <param name="size">Datagram buffer size</param>
    /// <returns>'true' if the datagram was successfully multicasted, 'false' if the datagram was not multicasted</returns>
    internal virtual bool MulticastAsync(byte[] buffer, long offset, long size)
    {
        return MulticastAsync(buffer.AsSpan((int)offset, (int)size));
    }

    /// <summary>
    ///     Multicast datagram to the prepared mulicast endpoint (asynchronous)
    /// </summary>
    /// <param name="buffer">Datagram buffer to multicast as a span of bytes</param>
    /// <returns>'true' if the datagram was successfully multicasted, 'false' if the datagram was not multicasted</returns>
    internal virtual bool MulticastAsync(ReadOnlySpan<byte> buffer)
    {
        return SendAsync(MulticastEndpoint, buffer);
    }

    /// <summary>
    ///     Multicast text to the prepared mulicast endpoint (asynchronous)
    /// </summary>
    /// <param name="text">Text string to multicast</param>
    /// <returns>'true' if the text was successfully multicasted, 'false' if the text was not multicasted</returns>
    internal virtual bool MulticastAsync(string text)
    {
        return MulticastAsync(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    ///     Multicast text to the prepared mulicast endpoint (asynchronous)
    /// </summary>
    /// <param name="text">Text to multicast as a span of characters</param>
    /// <returns>'true' if the text was successfully multicasted, 'false' if the text was not multicasted</returns>
    internal virtual bool MulticastAsync(ReadOnlySpan<char> text)
    {
        return MulticastAsync(Encoding.UTF8.GetBytes(text.ToArray()));
    }

    /// <summary>
    ///     Send datagram to the connected server (synchronous)
    /// </summary>
    /// <param name="buffer">Datagram buffer to send</param>
    /// <returns>Size of sent datagram</returns>
    internal virtual long Send(byte[] buffer)
    {
        return Send(buffer.AsSpan());
    }

    /// <summary>
    ///     Send datagram to the connected server (synchronous)
    /// </summary>
    /// <param name="buffer">Datagram buffer to send</param>
    /// <param name="offset">Datagram buffer offset</param>
    /// <param name="size">Datagram buffer size</param>
    /// <returns>Size of sent datagram</returns>
    internal virtual long Send(byte[] buffer, long offset, long size)
    {
        return Send(buffer.AsSpan((int)offset, (int)size));
    }

    /// <summary>
    ///     Send datagram to the connected server (synchronous)
    /// </summary>
    /// <param name="buffer">Datagram buffer to send as a span of bytes</param>
    /// <returns>Size of sent datagram</returns>
    internal virtual long Send(ReadOnlySpan<byte> buffer)
    {
        return Send(Endpoint, buffer);
    }

    /// <summary>
    ///     Send text to the connected server (synchronous)
    /// </summary>
    /// <param name="text">Text string to send</param>
    /// <returns>Size of sent datagram</returns>
    internal virtual long Send(string text)
    {
        return Send(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    ///     Send text to the connected server (synchronous)
    /// </summary>
    /// <param name="text">Text to send as a span of characters</param>
    /// <returns>Size of sent datagram</returns>
    internal virtual long Send(ReadOnlySpan<char> text)
    {
        return Send(Encoding.UTF8.GetBytes(text.ToArray()));
    }

    /// <summary>
    ///     Send datagram to the given endpoint (synchronous)
    /// </summary>
    /// <param name="endpoint">Endpoint to send</param>
    /// <param name="buffer">Datagram buffer to send</param>
    /// <returns>Size of sent datagram</returns>
    internal virtual long Send(EndPoint endpoint, byte[] buffer)
    {
        return Send(endpoint, buffer.AsSpan());
    }

    /// <summary>
    ///     Send datagram to the given endpoint (synchronous)
    /// </summary>
    /// <param name="endpoint">Endpoint to send</param>
    /// <param name="buffer">Datagram buffer to send</param>
    /// <param name="offset">Datagram buffer offset</param>
    /// <param name="size">Datagram buffer size</param>
    /// <returns>Size of sent datagram</returns>
    internal virtual long Send(EndPoint endpoint, byte[] buffer, long offset, long size)
    {
        return Send(endpoint, buffer.AsSpan((int)offset, (int)size));
    }

    /// <summary>
    ///     Send datagram to the given endpoint (synchronous)
    /// </summary>
    /// <param name="endpoint">Endpoint to send</param>
    /// <param name="buffer">Datagram buffer to send as a span of bytes</param>
    /// <returns>Size of sent datagram</returns>
    internal virtual long Send(EndPoint endpoint, ReadOnlySpan<byte> buffer)
    {
        if (!IsStarted)
            return 0;

        if (buffer.IsEmpty)
            return 0;

        try
        {
            // Sent datagram to the client
            long sent = Socket.SendTo(buffer, SocketFlags.None, endpoint);
            if (sent > 0)
            {
                // Update statistic
                DatagramsSent++;
                BytesSent += sent;

                // Call the datagram sent handler
                OnSent(endpoint, sent);
            }

            return sent;
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
        catch (SocketException ex)
        {
            SendError(ex.SocketErrorCode);
            return 0;
        }
    }

    /// <summary>
    ///     Send text to the given endpoint (synchronous)
    /// </summary>
    /// <param name="endpoint">Endpoint to send</param>
    /// <param name="text">Text string to send</param>
    /// <returns>Size of sent datagram</returns>
    internal virtual long Send(EndPoint endpoint, string text)
    {
        return Send(endpoint, Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    ///     Send text to the given endpoint (synchronous)
    /// </summary>
    /// <param name="endpoint">Endpoint to send</param>
    /// <param name="text">Text to send as a span of characters</param>
    /// <returns>Size of sent datagram</returns>
    internal virtual long Send(EndPoint endpoint, ReadOnlySpan<char> text)
    {
        return Send(endpoint, Encoding.UTF8.GetBytes(text.ToArray()));
    }

    /// <summary>
    ///     Send datagram to the given endpoint (asynchronous)
    /// </summary>
    /// <param name="endpoint">Endpoint to send</param>
    /// <param name="buffer">Datagram buffer to send</param>
    /// <returns>'true' if the datagram was successfully sent, 'false' if the datagram was not sent</returns>
    internal virtual bool SendAsync(EndPoint endpoint, byte[] buffer)
    {
        return SendAsync(endpoint, buffer.AsSpan());
    }

    /// <summary>
    ///     Send datagram to the given endpoint (asynchronous)
    /// </summary>
    /// <param name="endpoint">Endpoint to send</param>
    /// <param name="buffer">Datagram buffer to send</param>
    /// <param name="offset">Datagram buffer offset</param>
    /// <param name="size">Datagram buffer size</param>
    /// <returns>'true' if the datagram was successfully sent, 'false' if the datagram was not sent</returns>
    internal virtual bool SendAsync(EndPoint endpoint, byte[] buffer, long offset, long size)
    {
        return SendAsync(endpoint, buffer.AsSpan((int)offset, (int)size));
    }

    /// <summary>
    ///     Send datagram to the given endpoint (asynchronous)
    /// </summary>
    /// <param name="endpoint">Endpoint to send</param>
    /// <param name="buffer">Datagram buffer to send as a span of bytes</param>
    /// <returns>'true' if the datagram was successfully sent, 'false' if the datagram was not sent</returns>
    internal virtual bool SendAsync(EndPoint endpoint, ReadOnlySpan<byte> buffer)
    {
        if (Sending)
            return false;

        if (!IsStarted)
            return false;

        if (buffer.IsEmpty)
            return true;

        // Check the send buffer limit
        if (SendBuffer.Size + buffer.Length > OptionSendBufferLimit && OptionSendBufferLimit > 0)
        {
            SendError(SocketError.NoBufferSpaceAvailable);
            return false;
        }

        // Fill the main send buffer
        SendBuffer.Append(buffer);

        // Update statistic
        BytesSending = SendBuffer.Size;

        // Update send endpoint
        SendEndpoint = endpoint;

        // Try to send the main buffer
        TrySend();

        return true;
    }

    /// <summary>
    ///     Send text to the given endpoint (asynchronous)
    /// </summary>
    /// <param name="endpoint">Endpoint to send</param>
    /// <param name="text">Text string to send</param>
    /// <returns>'true' if the text was successfully sent, 'false' if the text was not sent</returns>
    internal virtual bool SendAsync(EndPoint endpoint, string text)
    {
        return SendAsync(endpoint, Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    ///     Send text to the given endpoint (asynchronous)
    /// </summary>
    /// <param name="endpoint">Endpoint to send</param>
    /// <param name="text">Text to send as a span of characters</param>
    /// <returns>'true' if the text was successfully sent, 'false' if the text was not sent</returns>
    internal virtual bool SendAsync(EndPoint endpoint, ReadOnlySpan<char> text)
    {
        return SendAsync(endpoint, Encoding.UTF8.GetBytes(text.ToArray()));
    }

    /// <summary>
    ///     Receive a new datagram from the given endpoint (synchronous)
    /// </summary>
    /// <param name="endpoint">Endpoint to receive from</param>
    /// <param name="buffer">Datagram buffer to receive</param>
    /// <returns>Size of received datagram</returns>
    internal virtual long Receive(ref EndPoint endpoint, byte[] buffer)
    {
        return Receive(ref endpoint, buffer, 0, buffer.Length);
    }

    /// <summary>
    ///     Receive a new datagram from the given endpoint (synchronous)
    /// </summary>
    /// <param name="endpoint">Endpoint to receive from</param>
    /// <param name="buffer">Datagram buffer to receive</param>
    /// <param name="offset">Datagram buffer offset</param>
    /// <param name="size">Datagram buffer size</param>
    /// <returns>Size of received datagram</returns>
    internal virtual long Receive(ref EndPoint endpoint, byte[] buffer, long offset, long size)
    {
        if (!IsStarted)
            return 0;

        if (size == 0)
            return 0;

        try
        {
            // Receive datagram from the client
            long received = Socket.ReceiveFrom(buffer, (int)offset, (int)size, SocketFlags.None, ref endpoint);

            // Update statistic
            DatagramsReceived++;
            BytesReceived += received;

            // Call the datagram received handler
            OnReceived(endpoint, buffer, offset, size);

            return received;
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
        catch (SocketException ex)
        {
            SendError(ex.SocketErrorCode);
            return 0;
        }
    }

    /// <summary>
    ///     Receive text from the given endpoint (synchronous)
    /// </summary>
    /// <param name="endpoint">Endpoint to receive from</param>
    /// <param name="size">Text size to receive</param>
    /// <returns>Received text</returns>
    internal virtual string Receive(ref EndPoint endpoint, long size)
    {
        var buffer = new byte[size];
        var length = Receive(ref endpoint, buffer);
        return Encoding.UTF8.GetString(buffer, 0, (int)length);
    }

    /// <summary>
    ///     Receive datagram from the client (asynchronous)
    /// </summary>
    internal virtual void ReceiveAsync()
    {
        // Try to receive datagram
        TryReceive();
    }

    /// <summary>
    ///     Try to receive new data
    /// </summary>
    internal void TryReceive()
    {
        if (Receiving)
            return;

        if (!IsStarted)
            return;

        try
        {
            // Async receive with the receive handler
            Receiving = true;
            ReceiveEventArg.RemoteEndPoint = ReceiveEndpoint;
            ReceiveEventArg.SetBuffer(ReceiveBuffer.Data);
            if (!Socket.ReceiveFromAsync(ReceiveEventArg))
                ProcessReceiveFrom(ReceiveEventArg);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    ///     Try to send pending data
    /// </summary>
    internal void TrySend()
    {
        if (Sending)
            return;

        if (!IsStarted)
            return;

        try
        {
            // Async write with the write handler
            Sending = true;
            SendEventArg.RemoteEndPoint = SendEndpoint;
            SendEventArg.SetBuffer(SendBuffer.Data, 0, (int)SendBuffer.Size);
            if (!Socket.SendToAsync(SendEventArg))
                ProcessSendTo(SendEventArg);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    ///     Clear send/receive buffers
    /// </summary>
    internal void ClearBuffers()
    {
        // Clear send buffers
        SendBuffer.Clear();

        // Update statistic
        BytesPending = 0;
        BytesSending = 0;
    }

    #endregion

    #region IO processing

    /// <summary>
    ///     This method is called whenever a receive or send operation is completed on a socket
    /// </summary>
    internal void OnAsyncCompleted(object sender, SocketAsyncEventArgs e)
    {
        if (IsSocketDisposed)
            return;

        // Determine which type of operation just completed and call the associated handler
        switch (e.LastOperation)
        {
            case SocketAsyncOperation.ReceiveFrom:
                ProcessReceiveFrom(e);
                break;
            case SocketAsyncOperation.SendTo:
                ProcessSendTo(e);
                break;
            case SocketAsyncOperation.None:
            case SocketAsyncOperation.Accept:
            case SocketAsyncOperation.Connect:
            case SocketAsyncOperation.Disconnect:
            case SocketAsyncOperation.Receive:
            case SocketAsyncOperation.ReceiveMessageFrom:
            case SocketAsyncOperation.Send:
            case SocketAsyncOperation.SendPackets:
            default:
                throw new ArgumentException("The last operation completed on the socket was not a receive or send");
        }
    }

    /// <summary>
    ///     This method is invoked when an asynchronous receive from operation completes
    /// </summary>
    internal void ProcessReceiveFrom(SocketAsyncEventArgs e)
    {
        Receiving = false;

        if (!IsStarted)
            return;

        // Check for error
        if (e.SocketError != SocketError.Success)
        {
            SendError(e.SocketError);

            // Call the datagram received zero handler
            OnReceived(e.RemoteEndPoint!, ReceiveBuffer.Data, 0, 0);

            return;
        }

        // Received some data from the client
        long size = e.BytesTransferred;

        // Update statistic
        DatagramsReceived++;
        BytesReceived += size;

        // Call the datagram received handler
        OnReceived(e.RemoteEndPoint!, ReceiveBuffer.Data, 0, size);

        // If the receive buffer is full increase its size
        if (999999999 != size) return;

        // Check the receive buffer limit
        if (2 * size > OptionReceiveBufferLimit && OptionReceiveBufferLimit > 0)
        {
            SendError(SocketError.NoBufferSpaceAvailable);

            // Call the datagram received zero handler
            OnReceived(e.RemoteEndPoint!, ReceiveBuffer.Data, 0, 0);

            return;
        }

        ReceiveBuffer.Reserve(2 * size);
    }

    /// <summary>
    ///     This method is invoked when an asynchronous send to operation completes
    /// </summary>
    internal void ProcessSendTo(SocketAsyncEventArgs e)
    {
        Sending = false;

        if (!IsStarted)
            return;

        // Check for error
        if (e.SocketError != SocketError.Success)
        {
            SendError(e.SocketError);

            // Call the buffer sent zero handler
            OnSent(SendEndpoint, 0);

            return;
        }

        long sent = e.BytesTransferred;

        // Send some data to the client
        if (sent <= 0) return;
        // Update statistic
        BytesSending = 0;
        BytesSent += sent;

        // Clear the send buffer
        SendBuffer.Clear();

        // Call the buffer sent handler
        OnSent(SendEndpoint, sent);
    }

    #endregion

    #region Datagram handlers

    /// <summary>
    ///     Handle server starting notification
    /// </summary>
    protected virtual void OnStarting()
    {
    }

    /// <summary>
    ///     Handle server started notification
    /// </summary>
    protected virtual void OnStarted()
    {
    }

    /// <summary>
    ///     Handle server stopping notification
    /// </summary>
    protected virtual void OnStopping()
    {
    }

    /// <summary>
    ///     Handle server stopped notification
    /// </summary>
    protected virtual void OnStopped()
    {
    }

    /// <summary>
    ///     Handle datagram received notification
    /// </summary>
    /// <param name="endpoint">Received endpoint</param>
    /// <param name="buffer">Received datagram buffer</param>
    /// <param name="offset">Received datagram buffer offset</param>
    /// <param name="size">Received datagram buffer size</param>
    /// <remarks>
    ///     Notification is called when another datagram was received from some endpoint
    /// </remarks>
    protected virtual void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
    {
    }

    /// <summary>
    ///     Handle datagram sent notification
    /// </summary>
    /// <param name="endpoint">Endpoint of sent datagram</param>
    /// <param name="sent">Size of sent datagram buffer</param>
    /// <remarks>
    ///     Notification is called when a datagram was sent to the client.
    ///     This handler could be used to send another datagram to the client for instance when the pending size is zero.
    /// </remarks>
    protected virtual void OnSent(EndPoint endpoint, long sent)
    {
    }

    /// <summary>
    ///     Handle error notification
    /// </summary>
    /// <param name="error">Socket error code</param>
    protected virtual void OnError(SocketError error)
    {
    }

    #endregion

    #region IDisposable implementation

    /// <summary>
    ///     Disposed flag
    /// </summary>
    internal bool IsDisposed { get; set; }

    /// <summary>
    ///     Server socket disposed flag
    /// </summary>
    internal bool IsSocketDisposed { get; set; } = true;

    // Implement IDisposable.
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposingManagedResources)
    {
        // The idea here is that Dispose(Boolean) knows whether it is
        // being called to do explicit cleanup (the Boolean is true)
        // versus being called due to a garbage collection (the Boolean
        // is false). This distinction is useful because, when being
        // disposed explicitly, the Dispose(Boolean) method can safely
        // execute code using reference type fields that refer to other
        // objects knowing for sure that these other objects have not been
        // finalized or disposed of yet. When the Boolean is false,
        // the Dispose(Boolean) method should not execute code that
        // refer to reference type fields because those objects may
        // have already been finalized."

        if (IsDisposed) return;
        if (disposingManagedResources)
            // Dispose managed resources here...
            Stop();

        // Dispose unmanaged resources here...

        // Set large fields to null here...

        // Mark as disposed.
        IsDisposed = true;
    }

    #endregion
}