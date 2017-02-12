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
    /// A WebSocket server.
    /// </summary>
    public class WebSocketServer {
        private EventLoop _loop;
        private HttpServer _httpserver;

        public delegate void ConnectionEventHandler(WebSocket socket);
        public event ConnectionEventHandler OnConnection;

        private const string GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        public WebSocketServer(EventLoop ev) {
            _loop = ev;
        }

        public void Listen(ushort port) {
            if (_httpserver != null) {
                throw new InvalidOperationException("Already listening");
            }
            _httpserver = new HttpServer(_loop);
            _httpserver.OnUpgrade += HandleUpgrade;
            _httpserver.OnRequest += OnRequest;
            _httpserver.Listen(port);
        }


        public void Close() {
            if (_httpserver != null) {
                _httpserver.Close();
                _httpserver = null;
            }
        }

        /// <summary>
        /// Handle a HTTP Upgrade request.
        /// </summary>
        /// <param name="req"></param>
        /// <param name="conn"></param>
        public void HandleUpgrade(HttpServerRequest req, TcpSocket socket, ArraySegment<byte> head) {
            if (!socket.Readable || !socket.Writable) {
                socket.Destroy();
                return;
            }

            if (OnConnection == null) {
                AbortConnection(socket, 400);
                return;
            }

            var upgrade = req.Headers["upgrade"];
            var version = req.Headers["sec-websocket-version"];

            if ((version != "13" && version != "8") ||
                !string.Equals(upgrade, "websocket", StringComparison.InvariantCultureIgnoreCase)) {
                socket.Write(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 400 Bad Request\r\n" +
                    "Connection: close\r\n" +
                    "Sec-WebSocket-Version: 13, 8\r\n"));
                socket.Close();
                return;
            }

            string acceptKey;
            using (var sha1 = new System.Security.Cryptography.SHA1CryptoServiceProvider()) {
                var key = req.Headers["sec-websocket-key"];
                acceptKey = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + GUID)));
            }

            socket.Write(Encoding.UTF8.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + acceptKey + "\r\n\r\n"));
            socket.Flush();

            OnConnection.Invoke(new WebSocket(socket, req, head));
        }

        private void OnRequest(HttpServerRequest req, HttpConnection conn) {
            if (conn.Writable) {
                conn.Write("HTTP/1.1 426 Upgrade Required\r\n" +
                    "Connection: close\r\n" +
                    "Content-Length: 0\r\n" +
                    "\r\n");
            }
            conn.Close();
        }

        private void AbortConnection(TcpSocket socket, int statusCode, string message = null) {
            if (socket.Writable) {
                var messageBytes = string.IsNullOrEmpty(message) ? BufferData.EmptyBytes : Encoding.UTF8.GetBytes(message);
                var msg = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 " + statusCode + " " + HttpUtility.GetHttpStatus(statusCode) + "\r\n" +
                    "Connection: close\r\n" +
                    "Content-type: text/html\r\n" +
                    "Content-Length: " + messageBytes.Length + "\r\n" +
                    "\r\n");

                socket.Write(msg);
                socket.Write(messageBytes);
            }
            socket.Close();
        }
    }
}
