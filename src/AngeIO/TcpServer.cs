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
    public class TcpServer {
        private EventLoop _loop;

        private Socket _socket;
        private SocketAsyncEventArgs _acceptEventArgs;
        private int _backlog;

        public delegate void EventHandler(TcpSocket socket);
        public event EventHandler OnNewConnection;

        public bool Listening {
            get {
                return _socket != null;
            }
        }

        public TcpServer(EventLoop ev) {
            _loop = ev;
            _backlog = 1;
        }

        public void Listen(EndPoint bindAddress) {
            if (_socket != null)
                throw new Exception("Already listening");

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.Blocking = false;
            _socket.Bind(bindAddress); 
            _socket.Listen(_backlog);
            _acceptEventArgs = new SocketAsyncEventArgs();
            _acceptEventArgs.Completed += (sender, e) => _loop.Post(AcceptComplete, e);

            // Starts accept.
            if (!_socket.AcceptAsync(_acceptEventArgs)) {
                AcceptComplete(_acceptEventArgs);
            }
        }

        public void Listen(int port) {
            Listen(new IPEndPoint(IPAddress.Any, port));
        }

        private void AcceptComplete(object state) {
            while (true) {
                var e = (SocketAsyncEventArgs)state;
                if (e.SocketError != SocketError.Success) {
                    DebugOutput.Error($"Accept failed: err={e.SocketError}");
                    var socket = e.AcceptSocket;
                    if (socket != null) {
                        socket.Close();
                        socket.Dispose();
                    }
                }
                else {
                    var socket = e.AcceptSocket;
                    if (socket != null) {
                        e.AcceptSocket = null;
                        if (OnNewConnection != null) {
                            OnNewConnection(new TcpSocket(_loop, socket));
                        }
                        else {
                            socket.Close();
                            socket.Dispose();
                        }
                    }
                }

                try {
                    if (_socket.AcceptAsync(e))
                        return;
                }
                catch (Exception err) {
                    DebugOutput.Error($"AcceptAsync failed: exception={err}");
                    return;
                }
            }
        }

        public void Close() {
            if (_socket != null) {
                _socket.Dispose();
                _socket = null;
            }
            if (_acceptEventArgs != null) {
                _acceptEventArgs.Dispose();
                _acceptEventArgs = null;
            }
        }
    }
}
