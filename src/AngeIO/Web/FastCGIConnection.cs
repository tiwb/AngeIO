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
using System.IO;
using System.Text;

namespace AngeIO.Web {
    /// <summary>
    /// Fast CGI Connection.
    /// </summary>
    class FastCGIConnection {
        enum ReceiveState {
            Header,
            Body,
            Padding,
        }

        private FastCGIServer _owner;
        private TcpSocket _socket;
        private ReceiveState _receiveState;
        private int _receiveSkip;

        private byte[] _headerBuffer = new byte[8];
        private Dictionary<int, FastCGIRequest> _requests = new Dictionary<int, FastCGIRequest>();

        enum RecordType {
            BeginRequest = 1,
            AbortRequest,
            EndRequest,
            Params,
            Stdin,
            Stdout,
            Stderr,
            Data,
            GetValues,
            GetValuesResult,
            UnknownType,
            MaxType = UnknownType,
        }
        private const int FCGI_VERSION_1 = 1;
        private const int FCGI_KEEP_CONN = 1;
        private const int FCGI_REQUEST_COMPLETE = 0;

        private static Logger logger = new Logger();

        public FastCGIConnection(FastCGIServer owner, TcpSocket socket) {
            _owner = owner;
            _receiveState = ReceiveState.Header;
            _socket = socket;
            _socket.NoDelay = true;
            _socket.ReceiveBuffer(new ArraySegment<byte>(_headerBuffer));
            _socket.OnData += OnData;
            _socket.ReceiveStart();
        }

        private void OnData(TcpSocket sender, ArraySegment<byte> data) {
            switch (_receiveState) {
                case ReceiveState.Header:
                    if (_headerBuffer[0] != FCGI_VERSION_1) {
                        _socket.Destroy();
                        return;
                    }
                    var contentLength = _headerBuffer[4] << 8 | _headerBuffer[5];
                    if (contentLength == 0) {
                        data = new ArraySegment<byte>(BufferData.EmptyBytes);
                        goto case ReceiveState.Body;
                    }
                    else {
                        _socket.ReceiveBuffer(new ArraySegment<byte>(new byte[contentLength]));
                        _receiveState = ReceiveState.Body;
                    }
                    break;

                case ReceiveState.Body:
                    try {
                        var type = (RecordType)_headerBuffer[1];
                        var requestId = (_headerBuffer[2] << 8) | _headerBuffer[3];
                        OnRecordReceived(type, requestId, data);
                    }
                    catch {
                        _socket.Destroy();
                        _socket = null;
                        return;
                    }

                    _receiveSkip = _headerBuffer[6];
                    if (_receiveSkip > 0) {
                        data = new ArraySegment<byte>();
                        _receiveState = ReceiveState.Padding;
                        goto case ReceiveState.Padding;
                    }
                    else {
                        _receiveState = ReceiveState.Header;
                        _socket.ReceiveBuffer(new ArraySegment<byte>(_headerBuffer));
                    }
                    break;

                case ReceiveState.Padding:
                    _receiveSkip -= data.Count;
                    if (_receiveSkip > 0) {
                        var len = Math.Min(_headerBuffer.Length, _receiveSkip);
                        _socket.ReceiveBuffer(new ArraySegment<byte>(_headerBuffer, 0, len));
                    }
                    else {
                        _receiveState = ReceiveState.Header;
                        _socket.ReceiveBuffer(new ArraySegment<byte>(_headerBuffer));
                    }
                    break;
            }
        }

        public void Close() {
            if (_socket != null) {
                _socket.Close();
            }
        }

        public void Flush() {
            if (_socket != null)
                _socket.Flush();
        }

        private static int ReadVarLength(byte[] buf, ref int pos) {
            var b3 = buf[pos];
            if (b3 <= 127) {
                pos++;
                return b3;
            }
            else {
                byte b2 = buf[pos + 1];
                byte b1 = buf[pos + 2];
                byte b0 = buf[pos + 3];
                pos += 4;
                return (int)(16777216 * (0x7f & b3) + 65536 * b2 + 256 * b1 + b0);
            }
        }

        private static void WriteVarLength(byte[] buf, ref int pos, int len) {
            if (len <= 127) {
                buf[pos] = (byte)len;
                pos++;
            }
            else {
                buf[pos + 0] = (byte)(0x80 | len / 16777216);
                buf[pos + 1] = (byte)(len / 65536);
                buf[pos + 2] = (byte)(len / 256);
                buf[pos + 3] = (byte)(len);
                pos += 4;
            }
        }

        private static void WriteNameValue(byte[] buf, ref int pos, string name, string value) {
            int namelen = Encoding.UTF8.GetByteCount(name);
            int valuelen = Encoding.UTF8.GetByteCount(value);
            WriteVarLength(buf, ref pos, namelen);
            WriteVarLength(buf, ref pos, valuelen);
            if (pos + namelen + valuelen >= buf.Length)
                throw new EndOfStreamException();

            Encoding.UTF8.GetBytes(name, 0, name.Length, buf, pos); pos += namelen;
            Encoding.UTF8.GetBytes(value, 0, value.Length, buf, pos); pos += valuelen;
        }

        private static void WriteRecordHeader(byte[] header, RecordType type, int requestId, int contentLength) {
            if (requestId < 0 || requestId > 0xffff) {
                throw new ArgumentOutOfRangeException("requestId");
            }
            if (contentLength < 0 || contentLength > 0xffff) {
                throw new ArgumentOutOfRangeException("contentLength");
            }
            header[0] = FCGI_VERSION_1;
            header[1] = (byte)type;
            header[2] = (byte)(requestId >> 8);
            header[3] = (byte)(requestId);
            header[4] = (byte)(contentLength >> 8);
            header[5] = (byte)(contentLength);
            header[6] = 0;
            header[7] = 0;
        }

        private void OnRecordReceived(RecordType recordType, int requestId, ArraySegment<byte> content) {
            switch (recordType) {
                case RecordType.BeginRequest: {
                        var buf = content.Array;
                        var p = content.Offset;
                        var role = (buf[p] << 8) | buf[p + 1];
                        var flags = buf[p + 2];
                        var keepAlive = (flags & FCGI_KEEP_CONN) != 0;
                        var request = new FastCGIRequest(requestId, this, keepAlive);
                        _requests[requestId] = request;
                    }
                    break;

                case RecordType.AbortRequest:
                case RecordType.EndRequest: {
                        _requests.Remove(requestId);
                    }
                    break;

                case RecordType.GetValues: {
                        var buf = new byte[256];
                        int pos = 0;
                        WriteNameValue(buf, ref pos, "FCGI_MAX_CONNS", "32");
                        WriteNameValue(buf, ref pos, "FCGI_MAX_REQS", "32");
                        WriteNameValue(buf, ref pos, "FCGI_MPXS_CONNS", "0");

                        SendRecord(RecordType.GetValuesResult, 0, new ArraySegment<byte>(buf, 0, pos));
                    }
                    break;

                case RecordType.Params: {
                        FastCGIRequest request = null;
                        if (_requests.TryGetValue(requestId, out request)) {
                            if (content.Count > 0) {
                                var buf = content.Array;
                                int len = content.Count;
                                int pos = content.Offset;

                                while (pos < len) {
                                    var nameLength = ReadVarLength(buf, ref pos);
                                    var valueLength = ReadVarLength(buf, ref pos);

                                    if (pos + nameLength + valueLength > len)
                                        throw new EndOfStreamException();

                                    var name = Encoding.UTF8.GetString(buf, pos, nameLength); pos += nameLength;
                                    var value = Encoding.UTF8.GetString(buf, pos, valueLength); pos += valueLength;

                                    request._parameters[name] = value;
                                }
                            }
                        }
                    }
                    break;

                case RecordType.Stdin: {
                        FastCGIRequest request = null;
                        if (_requests.TryGetValue(requestId, out request)) {
                            // Finished requests are indicated by an empty stdin record
                            if (content.Count == 0) {
                                _owner.HandleRequest(request);
                            }
                            else {
                                request._body.Add(content);
                            }
                        }
                    }
                    break;
            }
        }

        public void SendEndRequest(int requestId) {
            _requests.Remove(requestId);

            var content = new byte[8];

            // appStatusB3 - appStatusB0
            content[0] = 0;
            content[1] = 0;
            content[2] = 0;
            content[3] = 0;

            // protocolStatus
            content[4] = FCGI_REQUEST_COMPLETE;

            // reserved bytes
            content[5] = 0;
            content[6] = 0;
            content[7] = 0;
            SendRecord(RecordType.EndRequest, requestId, new ArraySegment<byte>(content));
        }

        private void SendRecord(RecordType type, int requestId, BufferData buffer) {
            if (_socket.Writable) {
                var header = new byte[8];
                WriteRecordHeader(header, type, requestId, buffer.ByteLength);
                _socket.Write(header);
                _socket.Write(buffer);
            }
        }

        private void SendRecord(RecordType type, int requestId, ArraySegment<byte> buffer) {
            if (_socket.Writable) {
                var header = new byte[8];
                WriteRecordHeader(header, type, requestId, buffer.Count);
                _socket.Write(header);
                _socket.Write(buffer);
            }
        }

        public void SendStdout(int requestId, BufferData data) {
            if (!_socket.Writable)
                return;

            // Send data with at most 65535 bytes in one record
            if (data.ByteLength <= 65535) {
                SendRecord(RecordType.Stdout, requestId, data);
            }

            // Split data with more than 64KB into multiple records
            else {
                int left = data.ByteLength;
                var it = data.GetEnumerator();
                it.MoveNext();
                var current = it.Current;

                while (left > 0) {
                    var size = Math.Min(left, 65535);
                    var header = new byte[8];
                    WriteRecordHeader(header, RecordType.Stdout, requestId, size);
                    _socket.Write(header);

                    left -= size;
                    while (size > 0) {
                        if (current.Count <= size) {
                            _socket.Write(current);
                            size -= current.Count;
                            it.MoveNext();
                            current = it.Current;
                        }
                        else {
                            _socket.Write(new ArraySegment<byte>(current.Array, current.Offset, size - current.Count));
                            current = new ArraySegment<byte>(current.Array, current.Offset + size, current.Count - size);
                            size = 0;
                        }
                    }
                }
            }
        }
    }
}
