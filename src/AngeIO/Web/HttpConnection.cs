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
using System.Text;

namespace AngeIO.Web {
    /// <summary>
    /// A Websocket connection
    /// </summary>
    public class HttpConnection {
        private HttpServer _server;
        private TcpSocket _socket;
        private HttpParser _parser;
        private HttpServerRequest _msg;
        private string _headerField;
        private string _headerValue;
        private ArraySegment<byte> _pausedData;

        public bool Writable {
            get { return _socket != null && _socket.Writable; }
        }

        public HttpConnection(TcpSocket s, HttpServer server) {
            _server = server;
            _socket = s;
            _socket.OnConnected += OnSocketConnected;
            _socket.OnDisconnected += OnSocketDisconnected;
            _socket.OnData += OnData;

            _parser = new HttpParser(HttpParserType.REQUEST);
            _parser.on_message_begin = OnMessageBegin;
            _parser.on_url = OnUrl;
            _parser.on_header_field = OnHeaderField;
            _parser.on_header_value = OnHeaderValue;
            _parser.on_headers_complete = OnHeaderComplete;
            _parser.on_body = OnBody;
            _parser.on_message_complete = OnMessageComplete;

            OnSocketConnected(s);
        }

        public void Close() {
            _socket.Close();
        }

        public void Write(byte[] data) {
            _socket.Write(data);
        }

        public void Write(ArraySegment<byte> data) {
            _socket.Write(data);
        }

        public void Write(string str) {
            Write(new ArraySegment<byte>(Encoding.UTF8.GetBytes(str)));
        }

        public void Flush() {
            _socket.Flush();
        }

        public void End() {
            _socket.Flush();
            _msg = null;
            if (_parser.ShouldKeepAlive()) {
                _parser.Pause(false);
                if (_pausedData.Count > 0) {
                    var data = _pausedData;
                    _pausedData = new ArraySegment<byte>();
                    OnData(_socket, data);
                }
                if (_parser.Errno == HttpParserError.OK)
                    _socket.ReceiveStart();
            }
            else {
                Close();
            }
        }

        private void OnSocketConnected(TcpSocket socket) {
            DebugOutput.Info($"Http connected: {socket.RemoteEndpoint}");
            _socket.ReceiveStart();
        }

        private void OnSocketDisconnected(TcpSocket socket) {
            DebugOutput.Info($"Http disconnected: {socket.RemoteEndpoint}");
        }

        private void OnData(TcpSocket socket, ArraySegment<byte> data) {
            int received = _parser.Execute(data.Array, data.Offset, data.Count);
            if (_parser.Errno == HttpParserError.PAUSED) {
                if (data.Count > received) {
                    _pausedData = new ArraySegment<byte>(data.Array, data.Offset + received, data.Count - received);
                }
                if (_parser.Upgrade) {
                    _socket.OnConnected -= OnSocketConnected;
                    _socket.OnDisconnected -= OnSocketDisconnected;
                    _socket.OnData -= OnData;
                    _server.HandleUpgrade(_msg, _socket, _pausedData);
                }
                else {
                    _server.HandleRequest(_msg, this);
                }
            }
            else if (_parser.Errno != HttpParserError.OK) {
                Close();
            }
        }

        private int OnMessageBegin() {
            _msg = new HttpServerRequest();
            return 0;
        }

        private int OnUrl(ArraySegment<byte> data) {
            _msg.Url += Encoding.ASCII.GetString(data.Array, data.Offset, data.Count);
            return 0;
        }

        private int OnHeaderField(ArraySegment<byte> data) {
            _headerField += Encoding.ASCII.GetString(data.Array, data.Offset, data.Count);
            return 0;
        }

        private int OnHeaderValue(ArraySegment<byte> data) {
            _headerValue += Encoding.ASCII.GetString(data.Array, data.Offset, data.Count);
            if (!_parser.IsIncomplete()) {
                _msg.Headers.Add(_headerField, _headerValue);
                _headerField = null;
                _headerValue = null;
            }
            return 0;
        }

        private int OnHeaderComplete() {
            _msg.ContentLength = (int)_parser.ContentLength;
            _msg.Method = _parser.Method;
            _msg.KeepAlive = _parser.ShouldKeepAlive();

            if (_parser.Upgrade) {
                return 1;
            }
            return 0;
        }

        private int OnBody(ArraySegment<byte> data) {
            _msg.Body.Add(data);
            return 0;
        }

        private int OnMessageComplete() {
            // stop receiving until request is processed.
            _socket.ReceiveStop();
            _parser.Pause(true);
            return 0;
        }
    }
}
