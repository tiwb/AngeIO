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
using System.IO;

namespace AngeIO {
    /// <summary>
    /// A list of byte array segments.
    /// </summary>
    public class BufferReader {
        private BufferData _buffer;
        private int _index;
        private byte[] _data;
        private int _pos;
        private int _end;

        public BufferReader(BufferData buffer) {
            _data = BufferData.EmptyBytes;
            _buffer = buffer;
        }

        public BufferReader(ArraySegment<byte> data) {
            _data = data.Array;
            _pos = data.Offset;
            _end = data.Offset + data.Count;
        }

        public BufferReader(byte[] data, int offset, int size) {
            _data = data;
            _pos = offset;
            _end = offset + size;
        }

        private bool FillCache() {
            if (_buffer != null && _index < _buffer.Count) {
                var seg = _buffer[_index];
                _index++;
                _data = seg.Array;
                _pos = seg.Offset;
                _end = seg.Offset + seg.Count;
                return true;
            }
            return false;
        }

        private void CheckCache() {
            if (_pos == _end) {
                if (_buffer == null || _index >= _buffer.Count) {
                    throw new EndOfStreamException();
                }
                var seg = _buffer[_index];
                _data = seg.Array;
                _pos = seg.Offset;
                _end = seg.Offset + seg.Count;
                _index++;
            }
        }

        public void Skip(int size) {
            if (_pos + size < _end) {
                _pos += size;
                return;
            }
            while (size > 0) {
                CheckCache();
                var len = Math.Min(_end - _pos, size);
                _pos += len;
                size -= len;
            }
        }

        public void Read(BufferData dst, int size) {
            while (size > 0) {
                CheckCache();
                var len = Math.Min(_end - _pos, size);
                dst.Add(_data, _pos, len);
                _pos += len;
                size -= len;
            }
        }

        public void Read(byte[] dst, int offset, int size) {
            while (size > 0) {
                CheckCache();
                var len = Math.Min(_end - _pos, size);
                Buffer.BlockCopy(_data, _pos, dst, offset, len);
                _pos += len;
                offset += len;
                size -= len;
            }
        }

        public byte ReadByte() {
            if (_pos == _end) CheckCache();
            return _data[_pos++];
        }

        public short ReadInt16() {
            return (short)ReadUInt16();
        }

        public ushort ReadUInt16() {
            byte b1, b2;
            if (_pos + 2 <= _end) {
                b1 = _data[_pos + 0];
                b2 = _data[_pos + 1];
                _pos += 2;
            }
            else {
                b1 = ReadByte();
                b2 = ReadByte();
            }
            return (ushort)((b1 << 8) | b2);
        }

        public int ReadInt32() {
            return (int)ReadUInt32();
        }

        public uint ReadUInt32() {
            byte b1, b2, b3, b4;
            if (_pos + 4 <= _end) {
                b1 = _data[_pos + 0];
                b2 = _data[_pos + 1];
                b3 = _data[_pos + 2];
                b4 = _data[_pos + 3];
                _pos += 4;
            }
            else {
                b1 = ReadByte();
                b2 = ReadByte();
                b3 = ReadByte();
                b4 = ReadByte();
            }
            return (uint)((b1 << 24) | (b2 << 16) | (b3 << 8) | (b4 << 0));
        }

        /// <summary>
        /// Read a raw Varint from the stream.  If larger than 32 bits, discard the upper bits.
        /// This method is optimised for the case where we've got lots of data in the buffer.
        /// That means we can check the size just once, then just read directly from the buffer
        /// without constant rechecking of the buffer length.
        /// </summary>
        public uint ReadVarint32() {
            if (_pos + 5 <= _end) {
                int tmp = _data[_pos++];
                if (tmp < 128) {
                    return (uint)tmp;
                }
                int result = tmp & 0x7f;
                if ((tmp = _data[_pos++]) < 128) {
                    result |= tmp << 7;
                }
                else {
                    result |= (tmp & 0x7f) << 7;
                    if ((tmp = _data[_pos++]) < 128) {
                        result |= tmp << 14;
                    }
                    else {
                        result |= (tmp & 0x7f) << 14;
                        if ((tmp = _data[_pos++]) < 128) {
                            result |= tmp << 21;
                        }
                        else {
                            result |= (tmp & 0x7f) << 21;
                            result |= (tmp = _data[_pos++]) << 28;
                            if (tmp >= 128) {
                                // Discard upper 32 bits.
                                // Note that this has to use ReadByte() as we only ensure we've
                                // got at least 5 bytes at the start of the method. This lets us
                                // use the fast path in more cases, and we rarely hit this section of code.
                                while (ReadByte() >= 128) {
                                    continue;
                                }
                            }
                        }
                    }
                }
                return (uint)result;
            }
            else {
                uint result = 0;
                int shift = 0;
                while (true) { 
                    if (_pos == _end) CheckCache();
                    byte b = _data[_pos++];
                    result |= (uint)b << shift;
                    shift += 7;
                    if (b < 128)
                        break;
                }
                return result;
            }
        }


        public string ReadString(int size, Encoding encoding) {
            if (_pos + size <= _end) {
                var s = encoding.GetString(_data, _pos, size);
                _pos += size;
                return s;
            }
            else {
                var t = new byte[size];
                Read(t, 0, size);
                return encoding.GetString(t);
            }
        }

        public string ReadString(int size) {
            return ReadString(size, Encoding.UTF8);
        }
    }
}
