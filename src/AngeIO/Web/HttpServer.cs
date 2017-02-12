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
using System.Net;

namespace AngeIO.Web {
    /// <summary>
    /// A Http Request
    /// </summary>
    public class HttpServer {
        private TcpServer _listener;

        public delegate void RequestEvenHandler(HttpServerRequest msg, HttpConnection conn);
        public delegate void UpgradeEventHandler(HttpServerRequest msg, TcpSocket socket, ArraySegment<byte> header);

        public event RequestEvenHandler OnRequest;
        public event UpgradeEventHandler OnUpgrade;

        public bool Listening {
            get {
                return _listener != null && _listener.Listening;
            }
        }

        public int MaxHeadersCount {
            get; set;
        }

        public HttpServer(EventLoop loop) {
            _listener = new TcpServer(loop);
            _listener.OnNewConnection += OnNewConnection;
        }

        public void Listen(EndPoint bindaddress) {
            if (_listener != null) {
                _listener.Listen(bindaddress);
            }
        }

        public void Listen(ushort port) {
            Listen(new IPEndPoint(IPAddress.Any, port));
        }

        public void Close() {
            if (_listener != null) {
                _listener.Close();
                _listener = null;
            }
        }

        private void OnNewConnection(TcpSocket socket) {
            new HttpConnection(socket, this);
        }

        internal void HandleRequest(HttpServerRequest msg, HttpConnection conn) {
            if (OnRequest != null) {
                OnRequest(msg, conn);
            }
            else {
                var s = "HTTP 500 Internal Server Error\r\n\r\n";
                conn.Write(new ArraySegment<byte>(Encoding.ASCII.GetBytes(s)));
                conn.End();
            }
        }

        internal void HandleUpgrade(HttpServerRequest msg, TcpSocket socket, ArraySegment<byte> header) {
            try {
                if (OnUpgrade != null) {
                    OnUpgrade.Invoke(msg, socket, header);
                }
                else {
                    socket.Close();
                }
            }

            catch {
                socket.Close();
            }
        }
    }
}
