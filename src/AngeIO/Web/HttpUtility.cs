using System;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.IO;

namespace AngeIO.Web {
    public static class HttpUtility {
        private static char[] _hexChars = "0123456789abcdef".ToCharArray();

        public static string GetHttpStatus(HttpStatusCode statusCode) {
            return GetHttpStatus((int)statusCode);
        }

        public static string GetHttpStatus(int statusCode) {
            switch (statusCode) {
                case 100: return "Continue";
                case 101: return "Switching Protocols";
                case 102: return "Processing";
                case 200: return "OK";
                case 201: return "Created";
                case 202: return "Accepted";
                case 203: return "Non-Authoritative Information";
                case 204: return "No Content";
                case 205: return "Reset Content";
                case 206: return "Partial Content";
                case 207: return "Multi-Status";
                case 208: return "Already Reported";
                case 226: return "IM Used";
                case 300: return "Multiple Choices";
                case 301: return "Moved Permanently";
                case 302: return "Found";
                case 303: return "See Other";
                case 304: return "Not Modified";
                case 305: return "Use Proxy";
                case 307: return "Temporary Redirect";
                case 308: return "Permanent Redirect";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 402: return "Payment Required";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 406: return "Not Acceptable";
                case 407: return "Proxy Authentication Required";
                case 408: return "Request Timeout";
                case 409: return "Conflict";
                case 410: return "Gone";
                case 411: return "Length Required";
                case 412: return "Precondition Failed";
                case 413: return "Payload Too Large";
                case 414: return "URI Too Long";
                case 415: return "Unsupported Media Type";
                case 416: return "Range Not Satisfiable";
                case 417: return "Expectation Failed";
                case 421: return "Misdirected Request";
                case 422: return "Unprocessable Entity";
                case 423: return "Locked";
                case 424: return "Failed Dependency";
                case 426: return "Upgrade Required";
                case 428: return "Precondition Required";
                case 429: return "Too Many Requests";
                case 431: return "Request Header Fields Too Large";
                case 451: return "Unavailable For Legal Reasons";
                case 500: return "Internal Server Error";
                case 501: return "Not Implemented";
                case 502: return "Bad Gateway";
                case 503: return "Service Unavailable";
                case 504: return "Gateway Timeout";
                case 505: return "HTTP Version Not Supported";
                case 506: return "Variant Also Negotiates";
                case 507: return "Insufficient Storage";
                case 508: return "Loop Detected";
                case 510: return "Not Extended";
                case 511: return "Network Authentication Required";
                default: return "Unknown";
            }
        }

        public static NameValueCollection ParseQueryString(string query) {
            return ParseQueryString(query, Encoding.UTF8);
        }

        public static NameValueCollection ParseQueryString(string query, Encoding encoding) {
            if (query == null)
                throw new ArgumentNullException("query");

            if (encoding == null)
                encoding = Encoding.UTF8;

            int len;
            if (query == null || (len = query.Length) == 0 || (len == 1 && query[0] == '?'))
                return new NameValueCollection(1);

            if (query[0] == '?')
                query = query.Substring(1);

            var res = new QueryStringCollection();
            var components = query.Split('&');
            foreach (var component in components) {
                var i = component.IndexOf('=');
                if (i > -1) {
                    var name = UrlDecode(component.Substring(0, i), encoding);
                    var val = component.Length > i + 1
                              ? UrlDecode(component.Substring(i + 1), encoding)
                              : String.Empty;

                    res.Add(name, val);
                }
                else {
                    res.Add(null, UrlDecode(component, encoding));
                }
            }

            return res;
        }

        public static string UrlDecode(string s) {
            return UrlDecode(s, Encoding.UTF8);
        }

        public static string UrlDecode(string s, Encoding encoding) {
            if (s == null || s.Length == 0 || !s.Contains('%', '+'))
                return s;

            if (encoding == null)
                encoding = Encoding.UTF8;

            var buff = new List<byte>();
            var len = s.Length;
            for (var i = 0; i < len; i++) {
                var c = s[i];
                if (c == '%' && i + 2 < len && s[i + 1] != '%') {
                    int xchar;
                    if (s[i + 1] == 'u' && i + 5 < len) {
                        // Unicode hex sequence.
                        xchar = getChar(s, i + 2, 4);
                        if (xchar != -1) {
                            writeCharBytes((char)xchar, buff, encoding);
                            i += 5;
                        }
                        else {
                            writeCharBytes('%', buff, encoding);
                        }
                    }
                    else if ((xchar = getChar(s, i + 1, 2)) != -1) {
                        writeCharBytes((char)xchar, buff, encoding);
                        i += 2;
                    }
                    else {
                        writeCharBytes('%', buff, encoding);
                    }

                    continue;
                }

                if (c == '+') {
                    writeCharBytes(' ', buff, encoding);
                    continue;
                }

                writeCharBytes(c, buff, encoding);
            }

            return encoding.GetString(buff.ToArray());
        }

        public static string UrlDecode(byte[] bytes, Encoding encoding) {
            int len;
            return bytes == null
                   ? null
                   : (len = bytes.Length) == 0
                     ? String.Empty
                     : InternalUrlDecode(bytes, 0, len, encoding ?? Encoding.UTF8);
        }

        public static string UrlDecode(byte[] bytes, int offset, int count, Encoding encoding) {
            if (bytes == null)
                return null;

            var len = bytes.Length;
            if (len == 0 || count == 0)
                return String.Empty;

            if (offset < 0 || offset >= len)
                throw new ArgumentOutOfRangeException("offset");

            if (count < 0 || count > len - offset)
                throw new ArgumentOutOfRangeException("count");

            return InternalUrlDecode(bytes, offset, count, encoding ?? Encoding.UTF8);
        }

        public static byte[] UrlDecodeToBytes(byte[] bytes) {
            int len;
            return bytes != null && (len = bytes.Length) > 0
                   ? InternalUrlDecodeToBytes(bytes, 0, len)
                   : bytes;
        }

        public static byte[] UrlDecodeToBytes(string s) {
            return UrlDecodeToBytes(s, Encoding.UTF8);
        }

        public static byte[] UrlDecodeToBytes(string s, Encoding encoding) {
            if (s == null)
                return null;

            if (s.Length == 0)
                return new byte[0];

            var bytes = (encoding ?? Encoding.UTF8).GetBytes(s);
            return InternalUrlDecodeToBytes(bytes, 0, bytes.Length);
        }

        public static byte[] UrlDecodeToBytes(byte[] bytes, int offset, int count) {
            int len;
            if (bytes == null || (len = bytes.Length) == 0)
                return bytes;

            if (count == 0)
                return new byte[0];

            if (offset < 0 || offset >= len)
                throw new ArgumentOutOfRangeException("offset");

            if (count < 0 || count > len - offset)
                throw new ArgumentOutOfRangeException("count");

            return InternalUrlDecodeToBytes(bytes, offset, count);
        }

        public static string UrlEncode(byte[] bytes) {
            int len;
            return bytes == null
                   ? null
                   : (len = bytes.Length) == 0
                     ? String.Empty
                     : Encoding.ASCII.GetString(InternalUrlEncodeToBytes(bytes, 0, len));
        }

        public static string UrlEncode(string s) {
            return UrlEncode(s, Encoding.UTF8);
        }

        public static string UrlEncode(string s, Encoding encoding) {
            int len;
            if (s == null || (len = s.Length) == 0)
                return s;

            var needEncode = false;
            foreach (var c in s) {
                if ((c < '0') || (c < 'A' && c > '9') || (c > 'Z' && c < 'a') || (c > 'z')) {
                    if (notEncoded(c))
                        continue;

                    needEncode = true;
                    break;
                }
            }

            if (!needEncode)
                return s;

            if (encoding == null)
                encoding = Encoding.UTF8;

            // Avoided GetByteCount call.
            var bytes = new byte[encoding.GetMaxByteCount(len)];
            var realLen = encoding.GetBytes(s, 0, len, bytes, 0);

            return Encoding.ASCII.GetString(InternalUrlEncodeToBytes(bytes, 0, realLen));
        }

        public static string UrlEncode(byte[] bytes, int offset, int count) {
            var encoded = UrlEncodeToBytes(bytes, offset, count);
            return encoded == null ? null : 
                   encoded.Length == 0 ? String.Empty : 
                   Encoding.ASCII.GetString(encoded);
        }

        public static byte[] UrlEncodeToBytes(byte[] bytes) {
            int len;
            return bytes != null && (len = bytes.Length) > 0
                   ? InternalUrlEncodeToBytes(bytes, 0, len)
                   : bytes;
        }

        public static byte[] UrlEncodeToBytes(string s) {
            return UrlEncodeToBytes(s, Encoding.UTF8);
        }

        public static byte[] UrlEncodeToBytes(string s, Encoding encoding) {
            if (s == null)
                return null;

            if (s.Length == 0)
                return new byte[0];

            var bytes = (encoding ?? Encoding.UTF8).GetBytes(s);
            return InternalUrlEncodeToBytes(bytes, 0, bytes.Length);
        }

        public static byte[] UrlEncodeToBytes(byte[] bytes, int offset, int count) {
            int len;
            if (bytes == null || (len = bytes.Length) == 0)
                return bytes;

            if (count == 0)
                return new byte[0];

            if (offset < 0 || offset >= len)
                throw new ArgumentOutOfRangeException("offset");

            if (count < 0 || count > len - offset)
                throw new ArgumentOutOfRangeException("count");

            return InternalUrlEncodeToBytes(bytes, offset, count);
        }


        public static string UrlPathEncode(string s) {
            if (s == null || s.Length == 0)
                return s;

            using (var res = new MemoryStream()) {
                foreach (var c in s)
                    urlPathEncode(c, res);

                res.Close();
                return Encoding.ASCII.GetString(res.ToArray());
            }
        }

        internal static string InternalUrlDecode(byte[] bytes, int offset, int count, Encoding encoding) {
            var output = new StringBuilder();
            using (var acc = new MemoryStream()) {
                var end = count + offset;
                for (var i = offset; i < end; i++) {
                    if (bytes[i] == '%' && i + 2 < count && bytes[i + 1] != '%') {
                        int xchar;
                        if (bytes[i + 1] == (byte)'u' && i + 5 < end) {
                            if (acc.Length > 0) {
                                output.Append(getChars(acc, encoding));
                                acc.SetLength(0);
                            }

                            xchar = getChar(bytes, i + 2, 4);
                            if (xchar != -1) {
                                output.Append((char)xchar);
                                i += 5;

                                continue;
                            }
                        }
                        else if ((xchar = getChar(bytes, i + 1, 2)) != -1) {
                            acc.WriteByte((byte)xchar);
                            i += 2;

                            continue;
                        }
                    }

                    if (acc.Length > 0) {
                        output.Append(getChars(acc, encoding));
                        acc.SetLength(0);
                    }

                    if (bytes[i] == '+') {
                        output.Append(' ');
                        continue;
                    }

                    output.Append((char)bytes[i]);
                }

                if (acc.Length > 0)
                    output.Append(getChars(acc, encoding));
            }

            return output.ToString();
        }

        private static void urlEncode(char c, Stream result, bool unicode) {
            if (c > 255) {
                // FIXME: What happens when there is an internal error?
                //if (!unicode)
                //  throw new ArgumentOutOfRangeException ("c", c, "Greater than 255.");

                result.WriteByte((byte)'%');
                result.WriteByte((byte)'u');

                var i = (int)c;
                var idx = i >> 12;
                result.WriteByte((byte)_hexChars[idx]);

                idx = (i >> 8) & 0x0F;
                result.WriteByte((byte)_hexChars[idx]);

                idx = (i >> 4) & 0x0F;
                result.WriteByte((byte)_hexChars[idx]);

                idx = i & 0x0F;
                result.WriteByte((byte)_hexChars[idx]);

                return;
            }

            if (c > ' ' && notEncoded(c)) {
                result.WriteByte((byte)c);
                return;
            }

            if (c == ' ') {
                result.WriteByte((byte)'+');
                return;
            }

            if ((c < '0') ||
                (c < 'A' && c > '9') ||
                (c > 'Z' && c < 'a') ||
                (c > 'z')) {
                if (unicode && c > 127) {
                    result.WriteByte((byte)'%');
                    result.WriteByte((byte)'u');
                    result.WriteByte((byte)'0');
                    result.WriteByte((byte)'0');
                }
                else {
                    result.WriteByte((byte)'%');
                }

                var i = (int)c;
                var idx = i >> 4;
                result.WriteByte((byte)_hexChars[idx]);

                idx = i & 0x0F;
                result.WriteByte((byte)_hexChars[idx]);

                return;
            }

            result.WriteByte((byte)c);
        }

        private static void urlPathEncode(char c, Stream result) {
            if (c < 33 || c > 126) {
                var bytes = Encoding.UTF8.GetBytes(c.ToString());
                foreach (var b in bytes) {
                    result.WriteByte((byte)'%');

                    var i = (int)b;
                    var idx = i >> 4;
                    result.WriteByte((byte)_hexChars[idx]);

                    idx = i & 0x0F;
                    result.WriteByte((byte)_hexChars[idx]);
                }

                return;
            }

            if (c == ' ') {
                result.WriteByte((byte)'%');
                result.WriteByte((byte)'2');
                result.WriteByte((byte)'0');

                return;
            }

            result.WriteByte((byte)c);
        }


        private static int getInt(byte b) {
            var c = (char)b;
            return c >= '0' && c <= '9'
                   ? c - '0'
                   : c >= 'a' && c <= 'f'
                     ? c - 'a' + 10
                     : c >= 'A' && c <= 'F'
                       ? c - 'A' + 10
                       : -1;
        }

        private static int getChar(byte[] bytes, int offset, int length) {
            var val = 0;
            var end = length + offset;
            for (var i = offset; i < end; i++) {
                var current = getInt(bytes[i]);
                if (current == -1)
                    return -1;

                val = (val << 4) + current;
            }

            return val;
        }

        private static int getChar(string s, int offset, int length) {
            var val = 0;
            var end = length + offset;
            for (var i = offset; i < end; i++) {
                var c = s[i];
                if (c > 127)
                    return -1;

                var current = getInt((byte)c);
                if (current == -1)
                    return -1;

                val = (val << 4) + current;
            }

            return val;
        }

        private static char[] getChars(MemoryStream buffer, Encoding encoding) {
            return encoding.GetChars(buffer.GetBuffer(), 0, (int)buffer.Length);
        }

        private static bool notEncoded(char c) {
            return c == '!' ||
                   c == '\'' ||
                   c == '(' ||
                   c == ')' ||
                   c == '*' ||
                   c == '-' ||
                   c == '.' ||
                   c == '_';
        }

        private static void writeCharBytes(char c, IList buffer, Encoding encoding) {
            if (c > 255) {
                foreach (var b in encoding.GetBytes(new[] { c }))
                    buffer.Add(b);

                return;
            }

            buffer.Add((byte)c);
        }

        internal static byte[] InternalUrlDecodeToBytes(byte[] bytes, int offset, int count) {
            using (var res = new MemoryStream()) {
                var end = offset + count;
                for (var i = offset; i < end; i++) {
                    var c = (char)bytes[i];
                    if (c == '+') {
                        c = ' ';
                    }
                    else if (c == '%' && i < end - 2) {
                        var xchar = getChar(bytes, i + 1, 2);
                        if (xchar != -1) {
                            c = (char)xchar;
                            i += 2;
                        }
                    }

                    res.WriteByte((byte)c);
                }

                res.Close();
                return res.ToArray();
            }
        }

        internal static byte[] InternalUrlEncodeToBytes(byte[] bytes, int offset, int count) {
            using (var res = new MemoryStream()) {
                var end = offset + count;
                for (var i = offset; i < end; i++)
                    urlEncode((char)bytes[i], res, false);

                res.Close();
                return res.ToArray();
            }
        }


        private static bool Contains(this string value, params char[] chars) {
            return chars == null || chars.Length == 0 ? true :
                   value == null || value.Length == 0 ? false :
                   value.IndexOfAny(chars) > -1;
        }
    }
}
