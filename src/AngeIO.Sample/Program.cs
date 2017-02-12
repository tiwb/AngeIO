using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AngeIO;
using AngeIO.Web;

namespace AngeIO.Sample {
    class Program {
        static void Main(string[] args) {
            // create Event loop
            var loop = new EventLoop();

            // create http server
            var httpsvr = new HttpServer(loop);
            httpsvr.Listen(8080);
            httpsvr.OnRequest += OnHttpRequest;
            httpsvr.OnUpgrade += OnHttpUpgrade;

            // create websocket server
            var wssvr = new WebSocketServer(loop);
            wssvr.OnConnection += OnWebsocketConnected;
            httpsvr.OnUpgrade += wssvr.HandleUpgrade;

            // create fastcgi server
            var cgisvr = new FastCGIServer(loop);
            cgisvr.OnRequest += OnFastCGIRequest;
            cgisvr.Listen(19000);

            // Run event loop
            loop.Run();

        }

        private static void OnHttpUpgrade(HttpServerRequest msg, TcpSocket socket, ArraySegment<byte> header) {
            Console.WriteLine("Http Upgrade: " + msg.Url);
        }

        private static void OnFastCGIRequest(FastCGIRequest req) {
            var url = req.GetParameter("REQUEST_URI");
            Console.WriteLine("FastCGI Request: " + url);
            if (url == "/") {
                var data = Encoding.UTF8.GetBytes(Properties.Resources.echo);
                req.Write("HTTP 200 OK\r\nContent-Length:" + data.Length + "\r\n\r\n");
                req.Write(data);
                req.End();
            }
            else {
                req.Write("HTTP 404 Not Found\r\nContent-Length:9\r\n\r\nNot Found");
                req.End();
            }
        }

        private static void OnHttpRequest(HttpServerRequest req, HttpConnection conn) {
            Console.WriteLine("Http Request: " + req.Url);
            if (req.Url == "/") {
                var data = Encoding.UTF8.GetBytes(Properties.Resources.echo);
                conn.Write("HTTP 200 OK\r\nContent-Length:" + data.Length + "\r\n\r\n");
                conn.Write(data);
                conn.End();

            }
            else {
                conn.Write("HTTP 404 Not Found\r\nContent-Length:0\r\n\r\n");
                conn.End();
            }
        }

        private static void OnWebsocketConnected(WebSocket socket) {
            socket.TextMode = true;
            socket.OnMessage += OnWebsocketMessage;
            socket.OnClose += WebsocketClosed;
            Console.WriteLine("Websocket connected: " + socket.RemoteEndpoint);
        }

        private static void WebsocketClosed(WebSocket sender, int code, string reason) {
            Console.WriteLine($"Websocket Closed: code={code} reason={reason}");
        }

        private static void OnWebsocketMessage(WebSocket socket, BufferData msg, int type) {
            var str = msg.ToString(Encoding.UTF8);
            Console.WriteLine("Websocket Message: " + str);
            socket.Write(msg);
            socket.EndMessage();
        }
    }
}
