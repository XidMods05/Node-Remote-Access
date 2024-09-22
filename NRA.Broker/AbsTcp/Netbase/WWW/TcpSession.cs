﻿using System.Net.Sockets;
using System.Text;

namespace NRA.Broker.AbsTcp.Netbase.WWW;

public class TcpSession : IDisposable
{
    /// <summary>
    ///     Initialize the session with a given server
    /// </summary>
    /// <param name="server">TCP server</param>
    internal TcpSession(TcpServer server)
    {
        Id = Guid.NewGuid();
        Server = server;
    }

    /// <summary>
    ///     Session Id
    /// </summary>
    internal Guid Id { get; }

    /// <summary>
    ///     Server
    /// </summary>
    internal TcpServer Server { get; }

    /// <summary>
    ///     Socket
    /// </summary>
    internal Socket Socket { get; private set; } = null!;

    /// <summary>
    ///     Number of bytes pending sent by the session
    /// </summary>
    internal long BytesPending { get; private set; }

    /// <summary>
    ///     Number of bytes sending by the session
    /// </summary>
    internal long BytesSending { get; private set; }

    /// <summary>
    ///     Number of bytes sent by the session
    /// </summary>
    internal long BytesSent { get; private set; }

    /// <summary>
    ///     Number of bytes received by the session
    /// </summary>
    internal long BytesReceived { get; private set; }

    #region Error handling

    /// <summary>
    ///     Send error notification
    /// </summary>
    /// <param name="error">Socket error code</param>
    private void SendError(SocketError error)
    {
        // Skip disconnect errors
        if (error is SocketError.ConnectionAborted or SocketError.ConnectionRefused or SocketError.ConnectionReset
            or SocketError.OperationAborted or SocketError.Shutdown)
            return;

        OnError(error);
    }

    #endregion

    #region Connect/Disconnect session

    /// <summary>
    ///     Is the session connected?
    /// </summary>
    internal bool IsConnected { get; private set; }

    /// <summary>
    ///     Connect the session
    /// </summary>
    /// <param name="socket">Session socket</param>
    internal void Connect(Socket socket)
    {
        Socket = socket;

        // Update the session socket disposed flag
        IsSocketDisposed = false;

        // Setup buffers
        _receiveBuffer = new Buffer(3072);

        // Setup event args
        _receiveEventArg = new SocketAsyncEventArgs();
        _receiveEventArg.Completed += OnAsyncCompleted!;
        _sendEventArg = new SocketAsyncEventArgs();
        _sendEventArg.Completed += OnAsyncCompleted!;

        // Apply the option: keep alive
        if (Server.OptionKeepAlive)
            Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        if (Server.OptionTcpKeepAliveTime >= 0)
            Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime,
                Server.OptionTcpKeepAliveTime);
        if (Server.OptionTcpKeepAliveInterval >= 0)
            Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval,
                Server.OptionTcpKeepAliveInterval);
        if (Server.OptionTcpKeepAliveRetryCount >= 0)
            Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount,
                Server.OptionTcpKeepAliveRetryCount);
        // Apply the option: no delay
        if (Server.OptionNoDelay)
            Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

        // Reset statistic
        BytesPending = 0;
        BytesSending = 0;
        BytesSent = 0;
        BytesReceived = 0;

        // Call the session connecting handler
        OnConnecting();

        // Call the session connecting handler in the server
        Server.OnConnectingInternal(this);

        // Update the connected flag
        IsConnected = true;

        // Try to receive something from the client
        TryReceive();

        // Check the socket disposed state: in some rare cases it might be disconnected while receiving!
        if (IsSocketDisposed)
            return;

        // Call the session connected handler
        OnConnected();

        // Call the session connected handler in the server
        Server.OnConnectedInternal(this);
    }

    /// <summary>
    ///     Disconnect the session
    /// </summary>
    /// <returns>'true' if the section was successfully disconnected, 'false' if the section is already disconnected</returns>
    internal virtual bool Disconnect()
    {
        if (!IsConnected)
            return false;

        // Reset event args
        _receiveEventArg.Completed -= OnAsyncCompleted!;
        _sendEventArg.Completed -= OnAsyncCompleted!;

        // Call the session disconnecting handler
        OnDisconnecting();

        // Call the session disconnecting handler in the server
        Server.OnDisconnectingInternal(this);

        try
        {
            try
            {
                // Shutdown the socket associated with the client
                Socket.Shutdown(SocketShutdown.Both);
            }
            catch (SocketException)
            {
            }

            // Close the session socket
            Socket.Close();

            // Dispose the session socket
            Socket.Dispose();

            // Dispose event arguments
            _receiveEventArg.Dispose();
            _sendEventArg.Dispose();

            // Update the session socket disposed flag
            IsSocketDisposed = true;
        }
        catch (ObjectDisposedException)
        {
        }

        // Update the connected flag
        IsConnected = false;

        // Update sending/receiving flags
        _receiving = false;
        _sending = false;

        // Clear send/receive buffers
        ClearBuffers();

        // Call the session disconnected handler
        OnDisconnected();

        // Call the session disconnected handler in the server
        Server.OnDisconnectedInternal(this);

        // Unregister session
        Server.UnregisterSession(Id);

        return true;
    }

    #endregion

    #region Send/Receive data

    // Receive buffer
    private bool _receiving;
    private Buffer _receiveBuffer = null!;

    private SocketAsyncEventArgs _receiveEventArg = null!;

    // Send buffer
    private readonly object _sendLock = new();
    private bool _sending;
    private Buffer _sendBufferMain = null!;
    private Buffer _sendBufferFlush = null!;
    private SocketAsyncEventArgs _sendEventArg = null!;
    private long _sendBufferFlushOffset;

    /// <summary>
    ///     Send data to the client (synchronous)
    /// </summary>
    /// <param name="buffer">Buffer to send</param>
    /// <returns>Size of sent data</returns>
    internal virtual long Send(byte[] buffer)
    {
        if (_sendBufferFlush != null!) return Send(buffer.AsSpan());

        _sendBufferMain = new Buffer(32);
        _sendBufferFlush = new Buffer(32);

        return Send(buffer.AsSpan());
    }

    /// <summary>
    ///     Send data to the client (synchronous)
    /// </summary>
    /// <param name="buffer">Buffer to send</param>
    /// <param name="offset">Buffer offset</param>
    /// <param name="size">Buffer size</param>
    /// <returns>Size of sent data</returns>
    internal virtual long Send(byte[] buffer, long offset, long size)
    {
        return Send(buffer.AsSpan((int)offset, (int)size));
    }

    /// <summary>
    ///     Send data to the client (synchronous)
    /// </summary>
    /// <param name="buffer">Buffer to send as a span of bytes</param>
    /// <returns>Size of sent data</returns>
    internal virtual long Send(ReadOnlySpan<byte> buffer)
    {
        if (!IsConnected)
            return 0;

        if (buffer.IsEmpty)
            return 0;

        if (_sendBufferFlush == null!)
        {
            _sendBufferMain = new Buffer(32);
            _sendBufferFlush = new Buffer(32);
        }

        // Sent data to the client
        long sent = Socket.Send(buffer, SocketFlags.None, out var ec);
        if (sent > 0)
        {
            // Update statistic
            BytesSent += sent;
            Interlocked.Add(ref Server._bytesSent, sent);

            // Call the buffer sent handler
            OnSent(sent, BytesPending + BytesSending);
        }

        // Check for socket error
        if (ec != SocketError.Success)
        {
            SendError(ec);
            Disconnect();
        }

        return sent;
    }

    /// <summary>
    ///     Send text to the client (synchronous)
    /// </summary>
    /// <param name="text">Text string to send</param>
    /// <returns>Size of sent data</returns>
    internal virtual long Send(string text)
    {
        if (_sendBufferFlush != null!) return Send(Encoding.UTF8.GetBytes(text));

        _sendBufferMain = new Buffer(32);
        _sendBufferFlush = new Buffer(32);

        return Send(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    ///     Send text to the client (synchronous)
    /// </summary>
    /// <param name="text">Text to send as a span of characters</param>
    /// <returns>Size of sent data</returns>
    internal virtual long Send(ReadOnlySpan<char> text)
    {
        return Send(Encoding.UTF8.GetBytes(text.ToArray()));
    }

    /// <summary>
    ///     Send data to the client (asynchronous)
    /// </summary>
    /// <param name="buffer">Buffer to send</param>
    /// <returns>'true' if the data was successfully sent, 'false' if the session is not connected</returns>
    internal virtual bool SendAsync(byte[] buffer)
    {
        if (_sendBufferFlush != null!) return SendAsync(buffer.AsSpan());

        _sendBufferMain = new Buffer(32);
        _sendBufferFlush = new Buffer(32);

        return SendAsync(buffer.AsSpan());
    }

    /// <summary>
    ///     Send data to the client (asynchronous)
    /// </summary>
    /// <param name="buffer">Buffer to send</param>
    /// <param name="offset">Buffer offset</param>
    /// <param name="size">Buffer size</param>
    /// <returns>'true' if the data was successfully sent, 'false' if the session is not connected</returns>
    internal virtual bool SendAsync(byte[] buffer, long offset, long size)
    {
        return SendAsync(buffer.AsSpan((int)offset, (int)size));
    }

    /// <summary>
    ///     Send data to the client (asynchronous)
    /// </summary>
    /// <param name="buffer">Buffer to send as a span of bytes</param>
    /// <returns>'true' if the data was successfully sent, 'false' if the session is not connected</returns>
    internal virtual bool SendAsync(ReadOnlySpan<byte> buffer)
    {
        if (!IsConnected)
            return false;

        if (buffer.IsEmpty)
            return true;

        if (_sendBufferFlush == null!)
        {
            _sendBufferMain = new Buffer(32);
            _sendBufferFlush = new Buffer(32);
        }

        lock (_sendLock)
        {
            // Fill the main send buffer
            _sendBufferMain.Append(buffer);

            // Update statistic
            BytesPending = _sendBufferMain.Size;

            // Avoid multiple send handlers
            if (_sending)
                return true;
            _sending = true;

            // Try to send the main buffer
            TrySend();
        }

        return true;
    }

    /// <summary>
    ///     Send text to the client (asynchronous)
    /// </summary>
    /// <param name="text">Text string to send</param>
    /// <returns>'true' if the text was successfully sent, 'false' if the session is not connected</returns>
    internal virtual bool SendAsync(string text)
    {
        return SendAsync(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    ///     Send text to the client (asynchronous)
    /// </summary>
    /// <param name="text">Text to send as a span of characters</param>
    /// <returns>'true' if the text was successfully sent, 'false' if the session is not connected</returns>
    internal virtual bool SendAsync(ReadOnlySpan<char> text)
    {
        return SendAsync(Encoding.UTF8.GetBytes(text.ToArray()));
    }

    /// <summary>
    ///     Receive data from the client (synchronous)
    /// </summary>
    /// <param name="buffer">Buffer to receive</param>
    /// <returns>Size of received data</returns>
    internal virtual long Receive(byte[] buffer)
    {
        return Receive(buffer, 0, buffer.Length);
    }

    /// <summary>
    ///     Receive data from the client (synchronous)
    /// </summary>
    /// <param name="buffer">Buffer to receive</param>
    /// <param name="offset">Buffer offset</param>
    /// <param name="size">Buffer size</param>
    /// <returns>Size of received data</returns>
    internal virtual long Receive(byte[] buffer, long offset, long size)
    {
        if (!IsConnected)
            return 0;

        if (size == 0)
            return 0;

        // Receive data from the client
        long received = Socket.Receive(buffer, (int)offset, (int)size, SocketFlags.None, out var ec);
        if (received > 0)
        {
            // Update statistic
            BytesReceived += received;
            Interlocked.Add(ref Server._bytesReceived, received);

            // Call the buffer received handler
            OnReceived(buffer, 0, received);
        }

        // Check for socket error
        if (ec != SocketError.Success)
        {
            SendError(ec);
            Disconnect();
        }

        return received;
    }

    /// <summary>
    ///     Receive text from the client (synchronous)
    /// </summary>
    /// <param name="size">Text size to receive</param>
    /// <returns>Received text</returns>
    internal virtual string Receive(long size)
    {
        var buffer = new byte[size];
        var length = Receive(buffer);
        return Encoding.UTF8.GetString(buffer, 0, (int)length);
    }

    /// <summary>
    ///     Receive data from the client (asynchronous)
    /// </summary>
    internal virtual void ReceiveAsync()
    {
        // Try to receive data from the client
        TryReceive();
    }

    /// <summary>
    ///     Try to receive new data
    /// </summary>
    private void TryReceive()
    {
        if (_receiving)
            return;

        if (!IsConnected)
            return;

        var process = true;

        while (process)
        {
            process = false;

            try
            {
                // Async receive with the receive handler
                _receiving = true;
                _receiveEventArg.SetBuffer(_receiveBuffer.Data);
                if (!Socket.ReceiveAsync(_receiveEventArg))
                    process = ProcessReceive(_receiveEventArg);
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>
    ///     Try to send pending data
    /// </summary>
    private void TrySend()
    {
        if (!IsConnected)
            return;

        var empty = false;
        var process = true;

        while (process)
        {
            process = false;

            lock (_sendLock)
            {
                // Is previous socket send in progress?
                if (_sendBufferFlush.IsEmpty)
                {
                    // Swap flush and main buffers
                    _sendBufferFlush = Interlocked.Exchange(ref _sendBufferMain, _sendBufferFlush);
                    _sendBufferFlushOffset = 0;

                    // Update statistic
                    BytesPending = 0;
                    BytesSending += _sendBufferFlush.Size;

                    // Check if the flush buffer is empty
                    if (_sendBufferFlush.IsEmpty)
                    {
                        // Need to call empty send buffer handler
                        empty = true;

                        // End sending process
                        _sending = false;
                    }
                }
                else
                {
                    return;
                }
            }

            // Call the empty send buffer handler
            if (empty)
            {
                OnEmpty();
                return;
            }

            try
            {
                // Async write with the write handler
                _sendEventArg.SetBuffer(_sendBufferFlush.Data, (int)_sendBufferFlushOffset,
                    (int)(_sendBufferFlush.Size - _sendBufferFlushOffset));
                if (!Socket.SendAsync(_sendEventArg))
                    process = ProcessSend(_sendEventArg);
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>
    ///     Clear send/receive buffers
    /// </summary>
    private void ClearBuffers()
    {
        lock (_sendLock)
        {
            try
            {
                _sendBufferMain.Clear();
                _sendBufferFlush.Clear();
                _sendBufferFlushOffset = 0;

                // Update statistic
                BytesPending = 0;
                BytesSending = 0;
            }
            catch
            {
                // ignored.
            }
        }
    }

    #endregion

    #region IO processing

    /// <summary>
    ///     This method is called whenever a receive or send operation is completed on a socket
    /// </summary>
    private void OnAsyncCompleted(object sender, SocketAsyncEventArgs e)
    {
        if (IsSocketDisposed)
            return;

        // Determine which type of operation just completed and call the associated handler
        switch (e.LastOperation)
        {
            case SocketAsyncOperation.Receive:
                if (ProcessReceive(e))
                    TryReceive();
                break;
            case SocketAsyncOperation.Send:
                if (ProcessSend(e))
                    TrySend();
                break;
            default:
                throw new ArgumentException("The last operation completed on the socket was not a receive or send");
        }
    }

    /// <summary>
    ///     This method is invoked when an asynchronous receive operation completes
    /// </summary>
    private bool ProcessReceive(SocketAsyncEventArgs e)
    {
        if (!IsConnected)
            return false;

        long size = e.BytesTransferred;

        // Received some data from the client
        if (size > 0)
        {
            // Update statistic
            BytesReceived += size;
            Interlocked.Add(ref Server._bytesReceived, size);

            // Call the buffer received handler
            OnReceived(_receiveBuffer.Data, 0, size);
        }

        _receiving = false;

        // Try to receive again if the session is valid
        if (e.SocketError == SocketError.Success)
        {
            // If zero is returned from a read operation, the remote end has closed the connection
            if (size > 0)
                return true;
            Disconnect();
        }
        else
        {
            SendError(e.SocketError);
            Disconnect();
        }

        return false;
    }

    /// <summary>
    ///     This method is invoked when an asynchronous send operation completes
    /// </summary>
    private bool ProcessSend(SocketAsyncEventArgs e)
    {
        if (!IsConnected)
            return false;

        long size = e.BytesTransferred;

        // Send some data to the client
        if (size > 0)
        {
            // Update statistic
            BytesSending -= size;
            BytesSent += size;
            Interlocked.Add(ref Server._bytesSent, size);

            // Increase the flush buffer offset
            _sendBufferFlushOffset += size;

            // Successfully send the whole flush buffer
            if (_sendBufferFlushOffset == _sendBufferFlush.Size)
            {
                // Clear the flush buffer
                _sendBufferFlush.Clear();
                _sendBufferFlushOffset = 0;
            }

            // Call the buffer sent handler
            OnSent(size, BytesPending + BytesSending);
        }

        // Try to send again if the session is valid
        if (e.SocketError == SocketError.Success) return true;

        SendError(e.SocketError);
        Disconnect();
        return false;
    }

    #endregion

    #region Session handlers

    /// <summary>
    ///     Handle client connecting notification
    /// </summary>
    protected virtual void OnConnecting()
    {
    }

    /// <summary>
    ///     Handle client connected notification
    /// </summary>
    protected virtual void OnConnected()
    {
    }

    /// <summary>
    ///     Handle client disconnecting notification
    /// </summary>
    protected virtual void OnDisconnecting()
    {
    }

    /// <summary>
    ///     Handle client disconnected notification
    /// </summary>
    protected virtual void OnDisconnected()
    {
    }

    /// <summary>
    ///     Handle buffer received notification
    /// </summary>
    /// <param name="buffer">Received buffer</param>
    /// <param name="offset">Received buffer offset</param>
    /// <param name="size">Received buffer size</param>
    /// <remarks>
    ///     Notification is called when another part of buffer was received from the client
    /// </remarks>
    protected virtual void OnReceived(byte[] buffer, long offset, long size)
    {
    }

    /// <summary>
    ///     Handle buffer sent notification
    /// </summary>
    /// <param name="sent">Size of sent buffer</param>
    /// <param name="pending">Size of pending buffer</param>
    /// <remarks>
    ///     Notification is called when another part of buffer was sent to the client.
    ///     This handler could be used to send another buffer to the client for instance when the pending size is zero.
    /// </remarks>
    protected virtual void OnSent(long sent, long pending)
    {
    }

    /// <summary>
    ///     Handle empty send buffer notification
    /// </summary>
    /// <remarks>
    ///     Notification is called when the send buffer is empty and ready for a new data to send.
    ///     This handler could be used to send another buffer to the client.
    /// </remarks>
    protected virtual void OnEmpty()
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
    internal bool IsDisposed { get; private set; }

    /// <summary>
    ///     Session socket disposed flag
    /// </summary>
    internal bool IsSocketDisposed { get; private set; } = true;

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
            Disconnect();

        // Dispose unmanaged resources here...

        // Set large fields to null here...

        // Mark as disposed.
        IsDisposed = true;
    }

    #endregion
}