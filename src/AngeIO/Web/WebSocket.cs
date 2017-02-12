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
    /// A Websocket connection
    /// </summary>
    public class WebSocket {
        private enum RecvStat {
            Start1,
            PayloadLength1,
            PayloadLength2,
            Mask,
            Data,
        }

        public enum SocketStat {
            Connecting,
            Open,
            Closing,
            Closed,
        }

        private TcpSocket _socket;
        private HttpServerRequest _context;
        private RecvStat _recvstate;
        private byte _fragmented;
        private int _index;
        private bool _fin;
        private byte _opcode;
        private bool _masked;
        private int _maskPos;
        private uint _maskValue;
        private long _payloadLength;
        private BufferData _payloadData;
        private BufferData _sendingQueue;
        private bool _sendingFragment;
        private bool _sendingTextMode;
        private bool _sendingMaskMode;
        private bool _isserver;
        private SocketStat _state;
        private int _closeCode;
        private string _closeReason;

        public delegate void CloseEventHandler(WebSocket socket, int code, string reason);
        public delegate void MessageEventHandler(WebSocket socket, BufferData msg, int type);

        public event MessageEventHandler OnMessage;
        public event CloseEventHandler OnClose;

        static ulong _rand = ((ulong)DateTime.Now.Ticks) * 1103515245 + 12345;

        public EndPoint RemoteEndpoint {
            get { return _socket.RemoteEndpoint; }
        }

        public bool Mask {
            get { return _sendingMaskMode; }
            set { _sendingMaskMode = value; }
        }

        public bool TextMode {
            get { return _sendingTextMode; }
            set { _sendingTextMode = value; }
        }

        internal WebSocket(TcpSocket socket, HttpServerRequest context, ArraySegment<byte> head) {
            _context = context;
            _socket = socket;
            _socket.OnData += OnData;
            _socket.OnDisconnected += OnSocketDisconnected;
            _recvstate = RecvStat.Start1;
            _fragmented = 0;
            _payloadData = new BufferData();
            _sendingQueue = new BufferData();
            _sendingTextMode = false;
            _sendingMaskMode = false;
            _isserver = true;
            _state = SocketStat.Open;

            if (head.Count > 0) {
                OnData(_socket, head);
            }

            _socket.NoDelay = true;
            _socket.ReceiveStart();
        }

        public void Write(byte[] buff) {
            if (buff.Length > 0)
                _sendingQueue.Add(buff);
        }

        public void Write(ArraySegment<byte> buff) {
            if (buff.Count > 0) {
                _sendingQueue.Add(buff);
            }
        }

        public void Write(BufferData buff) {
            if (buff.ByteLength > 0) {
                _sendingQueue.AddRange(buff);
            }
        }

        public void Flush() {
            if (_sendingQueue.ByteLength > 0) {
                byte opcode = (byte)(_sendingTextMode ? 1 : 2);
                if (_sendingFragment) {
                    opcode = 0;
                }
                else {
                    _sendingFragment = true;
                }
                SendFrame(opcode, _sendingMaskMode && !_isserver, false, _sendingQueue);
                _sendingQueue.Clear();
            }
        }

        public void EndMessage() {
            byte opcode = (byte)(_sendingTextMode ? 1 : 2);
            if (_sendingFragment) {
                opcode = 0;
                _sendingFragment = false;
            }
            SendFrame(opcode, _sendingMaskMode && !_isserver, true, _sendingQueue);
            _sendingQueue.Clear();
        }

        public void Close() {
            Close(0, null);
        }

        public void Close(ushort code, string reason) {
            if (_state == SocketStat.Closed)
                return;

            if (_state == SocketStat.Closing) {
                return;
            }

            _state = SocketStat.Closed;
            _closeCode = code;
            _closeReason = reason;

            if (_closeCode >= 1000 && !string.IsNullOrEmpty(_closeReason)) {
                var buf = new BufferData();
                var writer = new BufferWriter(buf);
                writer.WriteUInt16(code);
                writer.WriteString(reason);
                writer.Flush();
                SendFrame(0x08, _sendingMaskMode && !_isserver, true, buf);
            }
            else {
                SendFrame(0x08, _sendingMaskMode && !_isserver, true, null);
            }
        }

        private void SendFrame(byte opcode, bool mask, bool fin, BufferData buff) {
            int payloadlen = buff != null ? buff.ByteLength : 0;
            int bufflen = payloadlen;
            int headerlen = 2;
            if (payloadlen >= 65535) {
                headerlen += 8;
                payloadlen = 127;
            }
            else if (payloadlen > 125) {
                headerlen += 2;
                payloadlen = 126;
            }

            int maskpos = headerlen;
            if (mask) {
                headerlen += 4;
                headerlen += bufflen;
            }

            var header = new byte[headerlen];
            header[0] = (byte)((fin ? 0x80 : 0) | (opcode & 0x0f));
            header[1] = (byte)((mask ? 0x80 : 0) | (payloadlen));
            if (payloadlen == 126) {
                var len = bufflen;
                header[2] = (byte)(len >> 8);
                header[3] = (byte)(len >> 0);
            }
            else if (payloadlen == 127) {
                var len = bufflen;
                header[2] = (byte)(len >> 24);
                header[3] = (byte)(len >> 16);
                header[4] = (byte)(len >> 8);
                header[5] = (byte)(len >> 0);
            }

            if (mask) {
                var r = _rand;
                _rand = _rand * 1103515245 + 12345;
                r = 0;
                header[maskpos + 0] = (byte)(r >> 24);
                header[maskpos + 1] = (byte)(r >> 16);
                header[maskpos + 2] = (byte)(r >> 8);
                header[maskpos + 3] = (byte)(r >> 0);

                int p = maskpos + 4;
                int maskid = 3;
                if (buff != null) {
                    foreach (var d in buff) { 
                        var buf = d.Array;
                        for (int i = d.Offset; i < d.Offset + d.Count; i++) {
                            header[p] = (byte)(buf[i] ^ (byte)(r >> (maskid * 8)));
                            maskid = ((maskid - 1) & 3);
                            p++;
                        }
                    }
                }
                _socket.Write(header);
            }
            else {
                _socket.Write(header);
                _socket.Write(buff);
            }
            _socket.Flush();
        }

        private void OnData(TcpSocket socket, ArraySegment<byte> data) {
            var buf = data.Array;
            var start = data.Offset;
            var end = data.Offset + data.Count;
            var p = start;

            while (p < end) {
                switch (_recvstate) {
                    case RecvStat.Start1: {
                            byte tmp = buf[p];
                            if ((tmp & 0x70) != 0) Error("RSV1, RSV2 and RSV3 must be clear", 1002);
                            _fin = (tmp & 0x80) == 0x80;
                            _opcode = (byte)(tmp & 0x0f);
                            if (_opcode == 0) {
                                if (_fragmented == 0) {
                                    Error("invalid opcode: " + _opcode, 1002);
                                    return;
                                }
                                else {
                                    _opcode = _fragmented;
                                }
                            }
                            else if (_opcode == 0x01 || _opcode == 0x02) {
                                if (_fragmented != 0) {
                                    Error("invalid opcode: " + _opcode, 1002);
                                    return;
                                }
                            }
                            else if (_opcode > 0x07 && _opcode < 0x0b) {
                                if (!_fin) {
                                    Error("FIN must be set", 1002);
                                    return;
                                }
                            }
                            else {
                                Error("invalid opcode: " + _opcode, 1002);
                                return;
                            }

                            p++;
                            _recvstate = RecvStat.PayloadLength1;
                            break;
                        }

                    case RecvStat.PayloadLength1: {
                            _payloadLength = (long)(buf[p] & 0x7f);
                            _masked = (buf[p] & 0x80) == 0x80;
                            _maskValue = 0;
                            _maskPos = 0;

                            if (_payloadLength == 126) {
                                _payloadLength = 0;
                                _index = 2;
                                _recvstate = RecvStat.PayloadLength2;
                            }
                            else if (_payloadLength == 127) {
                                _payloadLength = 0;
                                _index = 8;
                                _recvstate = RecvStat.PayloadLength2;
                            }
                            else {
                                _recvstate = _masked ? RecvStat.Mask : RecvStat.Data;
                                _index = 4;
                            }

                            p++;
                            break;
                        }

                    case RecvStat.PayloadLength2: {
                            while (_index > 0 && p < end) {
                                _index--;
                                _payloadLength |= ((long)buf[p] << (8 * _index));
                                p++;
                            }
                            if (_index == 0) {
                                _recvstate = _masked ? RecvStat.Mask : RecvStat.Data;
                                _index = 4;
                            }
                            break;
                        }

                    case RecvStat.Mask: {
                            while (_index > 0 && p < end) {
                                _index--;
                                _maskValue |= ((uint)buf[p] << (8 * (3 - _index)));
                                p++;
                            }
                            if (_index == 0) {
                                _recvstate = RecvStat.Data;
                            }
                            break;
                        }

                    case RecvStat.Data: {
                            var len = (int)Math.Min(_payloadLength, end - p);
                            if (_masked) {
                                for (var i = p; i < p + len; i++) {
                                    buf[i] ^= (byte)(_maskValue >> (8 * (_maskPos & 3)));
                                    _maskPos++;
                                }
                            }
                            _payloadData.Add(buf, p, len);
                            _payloadLength -= len;
                            p += len;
                            if (_payloadLength == 0) {
                                _recvstate = RecvStat.Start1;
                                OnFrameReceived();
                            }
                            break;
                        }
                }
            }

            if (_recvstate == RecvStat.Data && _payloadLength == 0) {
                _recvstate = RecvStat.Start1;
                OnFrameReceived();
            }
        }

        private void OnFrameReceived() {
            if (_opcode > 0x7) {
                var msg = _payloadData;
                _payloadData = new BufferData();
                _payloadLength = 0;
                if (_opcode == 0x08) {
                    if (msg.ByteLength == 0) {
                        HandleClose(0, null);
                    }
                    else if (msg.ByteLength == 1) {
                        Error("Invalid Payload Length", 1002);
                    }
                    else {
                        var reader = new BufferReader(msg);
                        var code = reader.ReadUInt16();
                        var desc = reader.ReadString(msg.ByteLength - 2);
                        HandleClose(code, desc);
                    }
                }

                else if (_opcode == 0x09) {
                    SendFrame(0x0a, _sendingMaskMode && !_isserver, true, msg);
                }
                else if (_opcode == 0x0a) {
                }
            }
            else if (_fin) {
                var msg = _payloadData;
                _payloadData = new BufferData();
                _payloadLength = 0;
                if (OnMessage != null) {
                    OnMessage.Invoke(this, msg, _opcode);
                }
            }
        }

        private void Error(string desc, int code) {

        }

        private void HandleClose(int statusCode, string message) {
            Close((ushort)statusCode, message);
            _socket.Close();
        }

        private void OnSocketDisconnected(TcpSocket sender) {
            OnClose?.Invoke(this, _closeCode, _closeReason);
        }
    }
}
