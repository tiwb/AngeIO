using System;
using AngeIO.Web;
using NUnit.Framework;
using System.Text;

namespace AngeIO.Tests.Http {
    /// <summary>
    /// 
    /// </summary>
    [TestFixture]
    public class HttpParserTests {
        const string request_str =
            "POST /joyent/http-parser HTTP/1.1\r\n" +
            "Host: github.com\r\n" +
            "DNT: 1\r\n" +
            "Accept-Encoding: gzip, deflate, sdch\r\n" +
            "Accept-Language: ru-RU,ru;q=0.8,en-US;q=0.6,en;q=0.4\r\n" +
            "User-Agent: Mozilla/5.0 (Macintosh; Intel Mac OS X 10_10_1) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/39.0.2171.65 Safari/537.36\r\n" +
            "Accept: text/html,application/xhtml+xml,application/xml;q=0.9," +
                "image/webp,*/*;q=0.8\r\n" +
            "Referer: https://github.com/joyent/http-parser\r\n" +
            "Connection: keep-alive\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "Cache-Control: max-age=0\r\n\r\nb\r\nhello world\r\n0\r\n\r\n";

        [Test]
        public void HttpParserBench() {
            var data = Encoding.ASCII.GetBytes(request_str);
            var data_len = data.Length;

            for (int i = 0; i < 500000; i++) {
                var parser = new HttpParser(HttpParserType.REQUEST);
                parser.on_message_begin = on_info;
                parser.on_headers_complete = on_info;
                parser.on_message_complete = on_info;
                parser.on_header_field = on_data;
                parser.on_header_value = on_data;
                parser.on_url = on_data;
                parser.on_status = on_data;
                parser.on_body = on_data;
                var parsed = parser.Execute(data, 0, data_len);
                Assert.AreEqual(data_len, parsed);
            }
        }

        private int on_info() {
            return 0;
        }

        private int on_data(ArraySegment<byte> data) {
            return 0;
        }
    }
}
