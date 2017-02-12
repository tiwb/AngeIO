#region License
/*
 * The MIT License
 *
 * Copyright Li Jia
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */
#endregion

using System;
using System.Net;
using System.Net.Sockets;

namespace AngeIO {
    public class TcpSocket {
        enum State {
            Connecting,
            Connected,
            Disconnecting,
            Disconnected,
        }

        private Socket _socket;
        private EventLoop _loop;
        private bool _nodelay;
        private State _state;
        private SocketAsyncEventArgs _receiveEvent;
        private SocketAsyncEventArgs _sendEvent;
        private bool _receiveAsyncInprogress;
        private bool _sendAsyncInprogress;
        private BufferData _sendingQueue = new BufferData();
        private BufferData _sendingBuffer = new BufferData();
        private bool _receiveStarted;
        private ArraySegment<byte> _receiveBuff;
        private int _receiveOffset;
        private int _receiveEnd;
        private bool _receiveCallbackRunning;
        private EndPoint _remoteEndpoint;

        private static byte[] _peekbuff = new byte[1];

        public delegate void EventHandler(TcpSocket sender);
        public delegate void DataEventHandler(TcpSocket sender, ArraySegment<byte> data);

        public event EventHandler OnConnected;
        public event EventHandler OnDisconnected;
        public event DataEventHandler OnData;

        public TcpSocket(EventLoop ev) {
            _loop = ev;
            _state = State.Disconnected;
        }

        public TcpSocket(EventLoop ev, Socket s) {
            _loop = ev;
            _state = State.Connected;
            InitSocket(s);
        }

        public bool Connecting {
            get { return _state == State.Connecting; }
        }

        public bool Connected {
            get { return _state == State.Connected; }
        }

        public bool Closed {
            get { return _state == State.Disconnected; }
        }

        public bool NoDelay {
            get {
                return _nodelay;
            }
            set {
                _nodelay = value;
                try {
                    if (_socket != null)
                        _socket.NoDelay = value;
                }
                catch {}
            }
        }

        public bool Readable {
            get { return _state <= State.Disconnecting; }
        }

        public bool Writable {
            get { return _state <= State.Disconnecting; }
        }

        public EndPoint RemoteEndpoint {
            get { return _remoteEndpoint; }
        }

        public void Connect(EndPoint endpoint) {
            if (_state != State.Disconnected) {
                throw new Exception("Invalid state");
            }

            InitSocket(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp));
            try {
                _receiveEvent.RemoteEndPoint = endpoint;
                _state = State.Connecting;
                if (!_socket.ConnectAsync(_receiveEvent)) {
                    ReceiveAsyncComplete();
                }
            }
            catch (Exception err) {
                DebugOutput.Error($"Connect failed: err={err}");
                Disconnect();
            }
        }

        public void Close() {
            if (_state == State.Connecting) {
                Disconnect();
            }
            else if (_state == State.Connected) {
                _state = State.Disconnecting;
                SendData();
            }
        }

        public void Destroy() {
            Disconnect();
        }

        public void Write(byte[] buff) {
            if (_state < State.Disconnecting ) {
                if (buff.Length > 0)
                    _sendingQueue.Add(buff);
            }
        }

        public void Write(ArraySegment<byte> buff) {
            if (_state < State.Disconnecting ) {
                if (buff.Count > 0)
                    _sendingQueue.Add(buff);
            }
        }

        public void Write(BufferData buff) {
            if (_state < State.Disconnecting ) {
                if (buff != null && buff.ByteLength > 0)
                    _sendingQueue.AddRange(buff);
            }
        }

        public void Flush() {
            SendData();
        }

        public void ReceiveBuffer(ArraySegment<byte> buff) {
            if (_state < State.Disconnecting) {
                _receiveBuff = buff;
                _receiveOffset = buff.Offset;
                _receiveEnd = buff.Offset + buff.Count;
            }
        }

        public void ReceiveStart() {
            if (_state < State.Disconnecting) {
                _receiveStarted = true;
                if (!_receiveCallbackRunning)
                    ReceiveData();
            }
        }

        public void ReceiveStop() {
            _receiveStarted = false;
        }

        private void InitSocket(Socket s) {
            try {
                _socket = s;
                _socket.Blocking = false;
                _nodelay = _socket.NoDelay;

                _receiveEvent = new SocketAsyncEventArgs();
                _receiveEvent.SetBuffer(_peekbuff, 0, 1);
                _receiveEvent.SocketFlags = SocketFlags.Peek;
                _receiveEvent.Completed += (sender, e) => _loop.Post(ReceiveAsyncComplete);

                _sendEvent = new SocketAsyncEventArgs();
                _sendEvent.Completed += (sender, e) => _loop.Post(SendAsyncComplete);

                _sendAsyncInprogress = false;
                _receiveAsyncInprogress = false;
                _receiveOffset = 0;
                _receiveEnd = 0;
                _receiveStarted = false;

                _remoteEndpoint = _socket.RemoteEndPoint;
            }
            catch (Exception err) {
                DebugOutput.Error($"Socket init failed: err={err}");
                Disconnect();
            }
        }

        private void Disconnect() {
            if (_socket != null) {
                _socket.Close();
                _socket.Dispose();
                _socket = null;
            }
            if (_receiveEvent != null) {
                _receiveEvent.Dispose();
                _receiveEvent = null;
            }
            if (_sendEvent != null) {
                _sendEvent.Dispose();
                _sendEvent = null;
            }
            if (_state != State.Disconnected) {
                _state = State.Disconnected;
                OnDisconnected?.Invoke(this);
            }
        }

        private void ReceiveData() {
            run_again:
            try {
                if (_receiveAsyncInprogress || _socket == null)
                    return;

                if (!_receiveStarted)
                    return;

                // Receive data
                var available = _socket.Available;
                if (available > 0) {
                    byte[] buff = _receiveBuff.Array;

                    // Receive avaliable data
                    if (_receiveBuff.Count == 0) {
                        buff = new byte[available];
                        _receiveOffset = 0;
                        _receiveEnd = buff.Length;
                    }

                    // Receive data here
                    var bytesReceived = _socket.Receive(buff, _receiveOffset, _receiveEnd - _receiveOffset, SocketFlags.None);
                    if (bytesReceived <= 0) {
                        DebugOutput.Info("TcpSocket: Connection Closed.");
                        Disconnect();
                        return;
                    }

                    // Increment size received
                    _receiveOffset += bytesReceived;

                    // Receive completed.
                    if (_receiveBuff.Count == 0 || _receiveOffset == _receiveEnd) {
                        var received = new ArraySegment<byte>(buff, _receiveBuff.Offset, _receiveEnd - _receiveBuff.Offset);
                        _receiveOffset = _receiveBuff.Offset;
                        _receiveEnd = _receiveBuff.Offset + _receiveBuff.Count;
                        _receiveCallbackRunning = true;
                        OnData?.Invoke(this, received);
                        _receiveCallbackRunning = false;
                    }
                    goto run_again;
                }

                // Goto async mode to wait data ready.
                _receiveEvent.SocketFlags = SocketFlags.Peek;
                if (_socket.ReceiveAsync(_receiveEvent)) {
                    _receiveAsyncInprogress = true;
                    return;
                }

                // Should not get here
                if (_receiveEvent.SocketError != SocketError.Success) {
                    Disconnect();
                    return;
                }
                goto run_again;
            }
            catch (SocketException se) {
                if (se.SocketErrorCode == SocketError.WouldBlock ||
                    se.SocketErrorCode == SocketError.InProgress)
                    goto run_again;

                DebugOutput.Error($"TcpSocket: ReceiveException: code={se.SocketErrorCode}");
                Disconnect();
            }
            catch (Exception se) {
                DebugOutput.Error($"TcpSocket: ReceiveException: err={se.Message}");
                Disconnect();
            }
        }

        private void ReceiveAsyncComplete() {
            if (_socket == null)
                return;

            // Receive async completed.
            _receiveAsyncInprogress = false;

            // Check error
            if (_receiveEvent.SocketError != SocketError.Success) {
                Disconnect();
                return;
            }

            if (_receiveEvent.LastOperation == SocketAsyncOperation.Connect) {
                OnConnected?.Invoke(this);
                ReceiveData();
            }
            else if (_receiveEvent.LastOperation == SocketAsyncOperation.Receive) {
                if (_receiveEvent.BytesTransferred == 0) {
                    DebugOutput.Info("TcpSocket: Connection Closed.");
                    Disconnect();
                    return;
                }
                ReceiveData();
            }
        }

        private void SendData() {
            if (_sendAsyncInprogress) {
                return;
            }

            if (_socket == null)
                return;

            while (!_sendingBuffer.IsEmpty() || !_sendingQueue.IsEmpty()) {
                // Move data from sending queue to sending buffer.
                while (!_sendingQueue.IsEmpty() && _sendingBuffer.ByteLength < 1024) {
                    _sendingBuffer.Add(_sendingQueue.RemoveFront());
                }

                int size = 0;
                try {
                    size = _socket.Send(_sendingBuffer);
                }
                catch (SocketException se) {
                    if (se.SocketErrorCode != SocketError.WouldBlock &&
                        se.SocketErrorCode != SocketError.InProgress) {
                        DebugOutput.Error($"TcpSocket: Connection SendFailed: err={se.SocketErrorCode}");
                        Disconnect();
                        return;
                    }
                }

                if (size <= 0) {
                    // Can't send more data, enter async mode.
                    _sendAsyncInprogress = true;
                    _sendEvent.BufferList = _sendingBuffer;

                    try {
                        if (_socket.SendAsync(_sendEvent)) {
                            _sendAsyncInprogress = true;
                            return;
                        }
                    }
                    catch {
                        Disconnect();
                        return;
                    }
                    if (_sendEvent.SocketError != SocketError.Success) {
                        DebugOutput.Error($"TcpSocket: SendAsyncFailed: err={_sendEvent.SocketError}");
                        Disconnect();
                        return;
                    }
                    size = _sendEvent.BytesTransferred;
                }

                _sendingBuffer.RemoveFontBytes(size);
            }

            // Send finished, Close connection
            if (_state == State.Disconnecting) {
                try {
                    if (_socket.DisconnectAsync(_sendEvent)) {
                        _sendAsyncInprogress = true;
                        return;
                    }
                }
                catch (Exception err) {
                    DebugOutput.Error($"Disconnect failed: err={err}");
                }
                Disconnect();
            }
        }

        private void SendAsyncComplete() {
            if (_socket == null)
                return;

            _sendAsyncInprogress = false;
            if (_sendEvent.SocketError != SocketError.Success) {
                DebugOutput.Error($"TcpSocket: SendAsyncFailed: err={_sendEvent.SocketError}");
                Disconnect();
                return;
            }

            if (_sendEvent.LastOperation == SocketAsyncOperation.Send) {
                _sendingBuffer.RemoveFontBytes(_sendEvent.BytesTransferred);
                SendData();
            }

            else if (_sendEvent.LastOperation == SocketAsyncOperation.Disconnect) {
                Disconnect();
            }
        }
    }
}
