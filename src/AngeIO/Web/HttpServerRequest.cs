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
using System.Collections;
using System.Collections.Specialized;

namespace AngeIO.Web {
    /// <summary>
    /// A Http Request
    /// </summary>
    public class HttpServerRequest {
        private NameValueCollection _headers;
        private BufferData _body;

        public string Url { get; set; }

        public HttpMethod Method { get; set; }

        public bool KeepAlive { get; set; }

        public int ContentLength { get; set; }

        public bool BodyComplete { get; set; }

        public NameValueCollection Headers {
            get { return _headers ?? (_headers = new NameValueCollection()); }
        }

        public BufferData Body {
            get { return _body ?? (_body = new BufferData()); }
        }

        public HttpServerRequest() {
        }
    }
}
