using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TcpSession = NRA.Broker.AbsTcp.Netbase.WWW.TcpSession;

namespace NRA.Broker.AbsTcp.Netbase;

public class TcpServer : IDisposable
{
    /// <summary>
    ///     Initialize TCP server with a given IP address and port number
    /// </summary>
    /// <param name="address">IP address</param>
    /// <param name="port">Port number</param>
    internal TcpServer(IPAddress address, int port) : this(new IPEndPoint(address, port))
    {
    }

    /// <summary>
    ///     Initialize TCP server with a given IP address and port number
    /// </summary>
    /// <param name="address">IP address</param>
    /// <param name="port">Port number</param>
    internal TcpServer(string address, int port) : this(new IPEndPoint(IPAddress.Parse(address), port))
    {
    }

    /// <summary>
    ///     Initialize TCP server with a given DNS endpoint
    /// </summary>
    /// <param name="endpoint">DNS endpoint</param>
    internal TcpServer(DnsEndPoint endpoint) : this(endpoint, endpoint.Host, endpoint.Port)
    {
    }

    /// <summary>
    ///     Initialize TCP server with a given IP endpoint
    /// </summary>
    /// <param name="endpoint">IP endpoint</param>
    internal TcpServer(IPEndPoint endpoint) : this(endpoint, endpoint.Address.ToString(), endpoint.Port)
    {
    }

    /// <summary>
    ///     Initialize TCP server with a given endpoint, address and port
    /// </summary>
    /// <param name="endpoint">Endpoint</param>
    /// <param name="address">Server address</param>
    /// <param name="port">Server port</param>
    internal TcpServer(EndPoint endpoint, string address, int port)
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
    ///     TCP server address
    /// </summary>
    internal string Address { get; }

    /// <summary>
    ///     TCP server port
    /// </summary>
    internal int Port { get; }

    /// <summary>
    ///     Endpoint
    /// </summary>
    internal EndPoint Endpoint { get; set; }

    /// <summary>
    ///     Number of sessions connected to the server
    /// </summary>
    internal long ConnectedSessions => Sessions.Count;

    /// <summary>
    ///     Number of bytes pending sent by the server
    /// </summary>
    internal long BytesPending => _bytesPending;

    /// <summary>
    ///     Number of bytes sent by the server
    /// </summary>
    internal long BytesSent => _bytesSent;

    /// <summary>
    ///     Number of bytes received by the server
    /// </summary>
    internal long BytesReceived => _bytesReceived;

    /// <summary>
    ///     Option: acceptor backlog size
    /// </summary>
    /// <remarks>
    ///     This option will set the listening socket's backlog size
    /// </remarks>
    internal int OptionAcceptorBacklog { get; set; } = 100_000_000;

    /// <summary>
    ///     Option: dual mode socket
    /// </summary>
    /// <remarks>
    ///     Specifies whether the Socket is a dual-mode socket used for both IPv4 and IPv6.
    ///     Will work only if socket is bound on IPv6 address.
    /// </remarks>
    internal bool OptionDualMode { get; set; }

    /// <summary>
    ///     Option: keep alive
    /// </summary>
    /// <remarks>
    ///     This option will setup SO_KEEPALIVE if the OS support this feature
    /// </remarks>
    internal bool OptionKeepAlive { get; set; }

    /// <summary>
    ///     Option: TCP keep alive time
    /// </summary>
    /// <remarks>
    ///     The number of seconds a TCP connection will remain alive/idle before keepalive probes are sent to the remote
    /// </remarks>
    internal int OptionTcpKeepAliveTime { get; set; } = -1;

    /// <summary>
    ///     Option: TCP keep alive interval
    /// </summary>
    /// <remarks>
    ///     The number of seconds a TCP connection will wait for a keepalive response before sending another keepalive probe
    /// </remarks>
    internal int OptionTcpKeepAliveInterval { get; set; } = -1;

    /// <summary>
    ///     Option: TCP keep alive retry count
    /// </summary>
    /// <remarks>
    ///     The number of TCP keep alive probes that will be sent before the connection is terminated
    /// </remarks>
    internal int OptionTcpKeepAliveRetryCount { get; set; } = -1;

    /// <summary>
    ///     Option: no delay
    /// </summary>
    /// <remarks>
    ///     This option will enable/disable Nagle's algorithm for TCP protocol
    /// </remarks>
    internal bool OptionNoDelay { get; set; }

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

    #region Session factory

    /// <summary>
    ///     Create TCP session factory method
    /// </summary>
    /// <returns>TCP session</returns>
    protected virtual TcpSession CreateSession()
    {
        return new TcpSession(this);
    }

    #endregion

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

    #region Start/Stop server

    // Server acceptor
    internal Socket AcceptorSocket = null!;
    internal SocketAsyncEventArgs AcceptorEventArg = null!;

    // Server statistic
    internal long _bytesPending;
    internal long _bytesSent;
    internal long _bytesReceived;

    /// <summary>
    ///     Is the server started?
    /// </summary>
    internal bool IsStarted { get; set; }

    /// <summary>
    ///     Is the server accepting new clients?
    /// </summary>
    internal bool IsAccepting { get; set; }

    /// <summary>
    ///     Create a new socket object
    /// </summary>
    /// <remarks>
    ///     Method may be override if you need to prepare some specific socket object in your implementation.
    /// </remarks>
    /// <returns>Socket object</returns>
    protected virtual Socket CreateSocket()
    {
        return new Socket(Endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    }

    /// <summary>
    ///     Start the server
    /// </summary>
    /// <returns>'true' if the server was successfully started, 'false' if the server failed to start</returns>
    public virtual bool Start()
    {
        Debug.Assert(!IsStarted, "TCP server is already started!");
        if (IsStarted)
            return false;

        // Setup acceptor event arg
        AcceptorEventArg = new SocketAsyncEventArgs();
        AcceptorEventArg.Completed += OnAsyncCompleted!;

        // Create a new acceptor socket
        AcceptorSocket = CreateSocket();

        // Update the acceptor socket disposed flag
        IsSocketDisposed = false;

        // Apply the option: reuse address
        AcceptorSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, OptionReuseAddress);
        // Apply the option: exclusive address use
        AcceptorSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse,
            OptionExclusiveAddressUse);
        // Apply the option: dual mode (this option must be applied before listening)
        if (AcceptorSocket.AddressFamily == AddressFamily.InterNetworkV6)
            AcceptorSocket.DualMode = OptionDualMode;

        // Bind the acceptor socket to the endpoint
        AcceptorSocket.Bind(Endpoint);
        // Refresh the endpoint property based on the actual endpoint created
        Endpoint = AcceptorSocket.LocalEndPoint!;

        // Call the server starting handler
        OnStarting();

        // Start listen to the acceptor socket with the given accepting backlog size
        AcceptorSocket.Listen(OptionAcceptorBacklog);

        // Reset statistic
        _bytesPending = 0;
        _bytesSent = 0;
        _bytesReceived = 0;

        // Update the started flag
        IsStarted = true;

        // Call the server started handler
        OnStarted();

        // Perform the first server accept
        IsAccepting = true;
        StartAccept(AcceptorEventArg);

        return true;
    }

    /// <summary>
    ///     Stop the server
    /// </summary>
    /// <returns>'true' if the server was successfully stopped, 'false' if the server is already stopped</returns>
    internal virtual bool Stop(bool closeSessions)
    {
        Debug.Assert(IsStarted, "TCP server is not started!");
        if (!IsStarted)
            return false;

        // Stop accepting new clients
        IsAccepting = false;

        // Reset acceptor event arg
        AcceptorEventArg.Completed -= OnAsyncCompleted!;

        // Call the server stopping handler
        OnStopping();

        try
        {
            // Close the acceptor socket
            AcceptorSocket.Close();

            // Dispose the acceptor socket
            AcceptorSocket.Dispose();

            // Dispose event arguments
            AcceptorEventArg.Dispose();

            // Update the acceptor socket disposed flag
            IsSocketDisposed = true;
        }
        catch (ObjectDisposedException)
        {
        }

        // Disconnect all sessions
        if (closeSessions)
            DisconnectAll();

        // Update the started flag
        IsStarted = false;

        // Call the server stopped handler
        OnStopped();

        return true;
    }

    /// <summary>
    ///     Restart the server
    /// </summary>
    /// <returns>'true' if the server was successfully restarted, 'false' if the server failed to restart</returns>
    internal virtual bool Restart()
    {
        if (!Stop(true))
            return false;

        while (IsStarted)
            Thread.Yield();

        return Start();
    }

    #endregion

    #region Accepting clients

    /// <summary>
    ///     Start accept a new client connection
    /// </summary>
    internal void StartAccept(SocketAsyncEventArgs e)
    {
        // Socket must be cleared since the context object is being reused
        e.AcceptSocket = null;

        // Async accept a new client connection
        if (!AcceptorSocket.AcceptAsync(e))
            ProcessAccept(e);
    }

    /// <summary>
    ///     Process accepted client connection
    /// </summary>
    internal void ProcessAccept(SocketAsyncEventArgs e)
    {
        if (e.SocketError == SocketError.Success)
        {
            // Create a new session to register
            var session = CreateSession();

            // Register the session
            RegisterSession(session);

            // Connect new session
            session.Connect(e.AcceptSocket!);
        }
        else
        {
            SendError(e.SocketError);
        }

        // Accept the next client connection
        if (IsAccepting)
            StartAccept(e);
    }

    /// <summary>
    ///     This method is the callback method associated with Socket.AcceptAsync()
    ///     operations and is invoked when an accept operation is complete
    /// </summary>
    internal void OnAsyncCompleted(object sender, SocketAsyncEventArgs e)
    {
        if (IsSocketDisposed)
            return;

        ProcessAccept(e);
    }

    #endregion

    #region Session management

    /// <summary>
    ///     Server sessions
    /// </summary>
    protected readonly ConcurrentDictionary<Guid, TcpSession> Sessions = new();

    /// <summary>
    ///     Disconnect all connected sessions
    /// </summary>
    /// <returns>'true' if all sessions were successfully disconnected, 'false' if the server is not started</returns>
    internal virtual bool DisconnectAll()
    {
        if (!IsStarted)
            return false;

        // Disconnect all sessions
        foreach (var session in Sessions.Values)
            session.Disconnect();

        return true;
    }

    /// <summary>
    ///     Find a session with a given Id
    /// </summary>
    /// <param name="id">Session Id</param>
    /// <returns>Session with a given Id or null if the session it not connected</returns>
    internal TcpSession FindSession(Guid id)
    {
        // Try to find the required session
        return Sessions.GetValueOrDefault(id)!;
    }

    /// <summary>
    ///     Register a new session
    /// </summary>
    /// <param name="session">Session to register</param>
    internal void RegisterSession(TcpSession session)
    {
        // Register a new session
        Sessions.TryAdd(session.Id, session);
    }

    /// <summary>
    ///     Unregister session by ID
    /// </summary>
    /// <param name="id">Session ID</param>
    internal void UnregisterSession(Guid id)
    {
        // Unregister session by Id
        Sessions.TryRemove(id, out var _);
    }

    #endregion

    #region Multicasting

    /// <summary>
    ///     Multicast data to all connected sessions
    /// </summary>
    /// <param name="buffer">Buffer to multicast</param>
    /// <returns>'true' if the data was successfully multicasted, 'false' if the data was not multicasted</returns>
    internal virtual bool Multicast(byte[] buffer)
    {
        return Multicast(buffer.AsSpan());
    }

    /// <summary>
    ///     Multicast data to all connected clients
    /// </summary>
    /// <param name="buffer">Buffer to multicast</param>
    /// <param name="offset">Buffer offset</param>
    /// <param name="size">Buffer size</param>
    /// <returns>'true' if the data was successfully multicasted, 'false' if the data was not multicasted</returns>
    internal virtual bool Multicast(byte[] buffer, long offset, long size)
    {
        return Multicast(buffer.AsSpan((int)offset, (int)size));
    }

    /// <summary>
    ///     Multicast data to all connected clients
    /// </summary>
    /// <param name="buffer">Buffer to send as a span of bytes</param>
    /// <returns>'true' if the data was successfully multicasted, 'false' if the data was not multicasted</returns>
    internal virtual bool Multicast(ReadOnlySpan<byte> buffer)
    {
        if (!IsStarted)
            return false;

        if (buffer.IsEmpty)
            return true;

        // Multicast data to all sessions
        foreach (var session in Sessions.Values)
            session.SendAsync(buffer);

        return true;
    }

    /// <summary>
    ///     Multicast text to all connected clients
    /// </summary>
    /// <param name="text">Text string to multicast</param>
    /// <returns>'true' if the text was successfully multicasted, 'false' if the text was not multicasted</returns>
    internal virtual bool Multicast(string text)
    {
        return Multicast(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    ///     Multicast text to all connected clients
    /// </summary>
    /// <param name="text">Text to multicast as a span of characters</param>
    /// <returns>'true' if the text was successfully multicasted, 'false' if the text was not multicasted</returns>
    internal virtual bool Multicast(ReadOnlySpan<char> text)
    {
        return Multicast(Encoding.UTF8.GetBytes(text.ToArray()));
    }

    #endregion

    #region Server handlers

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
    ///     Handle session connecting notification
    /// </summary>
    /// <param name="session">Connecting session</param>
    protected virtual void OnConnecting(TcpSession session)
    {
    }

    /// <summary>
    ///     Handle session connected notification
    /// </summary>
    /// <param name="session">Connected session</param>
    protected virtual void OnConnected(TcpSession session)
    {
    }

    /// <summary>
    ///     Handle session disconnecting notification
    /// </summary>
    /// <param name="session">Disconnecting session</param>
    protected virtual void OnDisconnecting(TcpSession session)
    {
    }

    /// <summary>
    ///     Handle session disconnected notification
    /// </summary>
    /// <param name="session">Disconnected session</param>
    protected virtual void OnDisconnected(TcpSession session)
    {
    }

    /// <summary>
    ///     Handle error notification
    /// </summary>
    /// <param name="error">Socket error code</param>
    protected virtual void OnError(SocketError error)
    {
    }

    internal void OnConnectingInternal(TcpSession session)
    {
        OnConnecting(session);
    }

    internal void OnConnectedInternal(TcpSession session)
    {
        OnConnected(session);
    }

    internal void OnDisconnectingInternal(TcpSession session)
    {
        OnDisconnecting(session);
    }

    internal void OnDisconnectedInternal(TcpSession session)
    {
        OnDisconnected(session);
    }

    #endregion

    #region IDisposable implementation

    /// <summary>
    ///     Disposed flag
    /// </summary>
    internal bool IsDisposed { get; set; }

    /// <summary>
    ///     Acceptor socket disposed flag
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
            Stop(true);

        // Dispose unmanaged resources here...

        // Set large fields to null here...

        // Mark as disposed.
        IsDisposed = true;
    }

    #endregion
}