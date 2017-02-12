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

namespace AngeIO.Web {
    /// <summary>
    /// Fast CGI Listener.
    /// </summary>
    public class FastCGIServer {
        private TcpServer _server;

        public delegate void EventHandler(FastCGIRequest req);
        public event EventHandler OnRequest;

        public FastCGIServer(EventLoop loop) {
            _server = new TcpServer(loop);
            _server.OnNewConnection += OnSocketConnected;
        }

        public void Close() {
            _server.Close();
        }

        public void Listen(ushort port) {
            _server.Listen(port);
        }

        public void Listen(EndPoint bindAddress) {
            _server.Listen(bindAddress);
        }

        private void OnSocketConnected(TcpSocket s) {
            var conn = new FastCGIConnection(this, s);
        }

        internal void HandleRequest(FastCGIRequest request) {
            if (OnRequest != null) {
                try {
                    OnRequest(request);
                }
                catch (Exception) {
                    request.End();
                }
            }
            else {
                request.End();
            }
        }
    }
}
