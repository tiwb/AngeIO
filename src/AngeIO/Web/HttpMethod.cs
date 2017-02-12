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

namespace AngeIO.Web {
    /* Request Methods */
    public enum HttpMethod {
        DELETE = 0,
        GET = 1,
        HEAD = 2,
        POST = 3,
        PUT = 4,

        // pathological
        CONNECT = 5,
        OPTIONS = 6,
        TRACE = 7,

        // WebDAV
        COPY = 8,
        LOCK = 9,
        MKCOL = 10,
        MOVE = 11,
        PROPFIND = 12,
        PROPPATCH = 13,
        SEARCH = 14,
        UNLOCK = 15,
        BIND = 16,
        REBIND = 17,
        UNBIND = 18,
        ACL = 19,

        // subversion
        REPORT = 20,
        MKACTIVITY = 21,
        CHECKOUT = 22,
        MERGE = 23,

        // upnp
        MSEARCH = 24,
        NOTIFY = 25,
        SUBSCRIBE = 26,
        UNSUBSCRIBE = 27,

        // RFC-5789
        PATCH = 28,
        PURGE = 29,

        // CalDAV
        MKCALENDAR = 30,

        // RFC-2068, section19.6.1.2 
        LINK = 31,
        UNLINK = 32,
    }

    public static partial class HttpExt {
        private static readonly string[] method_str = new string[] {
            "DELETE", "GET", "HEAD", "POST", "PUT",
            "CONNECT", "OPTIONS", "TRACE",
            "COPY", "LOCK", "MKCOL", "MOVE", "PROPFIND", "PROPPATCH", "SEARCH", "UNLOCK", "BIND", "REBIND", "UNBIND", "ACL",
            "REPORT", "MKACTIVITY", "CHECKOUT", "MERGE",
            "M-SEARCH", "NOTIFY", "SUBSCRIBE", "UNSUBSCRIBE",
            "PATCH", "PURGE",
            "MKCALENDAR",
            "LINK", "UNLINK",
        };

        public static string GetDescription(this HttpMethod meth) {
            return ((int)meth < method_str.Length) ? method_str[(int)meth] : "UNKNOWN";
        }
    }
}
