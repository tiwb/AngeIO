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
using System.Collections.Generic;
using System.Text;

namespace AngeIO.Web {
    /// <summary>
    /// A FastCGI request.
    /// </summary>
    public class FastCGIRequest {
        int _requestId;
        bool _keepAlive;
        FastCGIConnection _connection;

        internal BufferData _body;
        internal Dictionary<string, string> _parameters;
        private BufferData _responseBuffer;

        /// <summary>
        /// Creates a new request.
        /// </summary>
        internal FastCGIRequest(int requestId, FastCGIConnection connection, bool keepAlive) {
            _requestId = requestId;
            _connection = connection;
            _keepAlive = keepAlive;

            _body = new BufferData();
            _responseBuffer = new BufferData();
            _parameters = new Dictionary<string, string>();
        }

        /// <summary>
        /// Get the parameters collection
        /// </summary>
        public Dictionary<string, string> Parameters {
            get { return _parameters; }
        }

        /// <summary>
        /// Returns the parameter with the given name as an UTF-8 encoded string.
        /// </summary>
        public string GetParameter(string name) {
            string value;
            _parameters.TryGetValue(name, out value);
            return value;
        }

        /// <summary>
        /// Appends an UTF-8 string to the response body.
        /// </summary>
        public void Write(string data) {
            _responseBuffer.Add(Encoding.UTF8.GetBytes(data));
        }

        /// <summary>
        /// Appends a byte array to the response body.
        /// </summary>
        public void Write(byte[] data) {
            _responseBuffer.Add(data);
        }

        /// <summary>
        /// Flush stdout.
        /// </summary>
        public void Flush() {
            if (_connection != null) {
                if (!_responseBuffer.IsEmpty()) {
                    _connection.SendStdout(_requestId, _responseBuffer);
                    _connection.Flush();
                    _responseBuffer.Clear();
                }
            }
        }

        /// <summary>
        /// End this request.
        /// </summary>
        public void End() {
            if (_connection != null) {
                if (!_responseBuffer.IsEmpty()) {
                    _connection.SendStdout(_requestId, _responseBuffer);
                    _responseBuffer.Clear();
                }
                // Last empty response
                _connection.SendStdout(_requestId, _responseBuffer);
                _connection.SendEndRequest(_requestId);
                _connection.Flush();
                if (!_keepAlive)
                    _connection.Close();

                // Clear connection after closed.
                _connection = null;
            }
        }
    }
}
