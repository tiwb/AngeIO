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

namespace AngeIO {

    /// <summary>
    /// A list of byte array segments.
    /// </summary>
    public class BufferWriter {
        private BufferData _buffer;
        private byte[] _cache;
        private int _pos;
        private int _end;
        private int _start;

        const int WRITE_BUFFER_SIZE = 512;

        public BufferWriter(BufferData buffer) {
            _buffer = buffer;
        }

        public BufferWriter(ArraySegment<byte> data) {
            _cache = data.Array;
            _pos = data.Offset;
            _end = data.Offset + data.Count;
        }

        public BufferWriter(byte[] data, int offset, int size) {
            _cache = data;
            _pos = offset;
            _end = offset + size;
        }

        public void WriteByte(byte value) {
            EnsureCache(1);
            _cache[_pos] = value;
            _pos++;
        }

        public void WriteInt16(short value) {
            EnsureCache(2);
            _cache[_pos + 0] = (byte)(value >> 8);
            _cache[_pos + 1] = (byte)(value >> 0);
            _pos += 2;
        }

        public void WriteUInt16(ushort value) {
            EnsureCache(2);
            _cache[_pos + 0] = (byte)(value >> 8);
            _cache[_pos + 1] = (byte)(value >> 0);
            _pos += 2;
        }

        public void WriteInt32(int value) {
            EnsureCache(4);
            _cache[_pos + 0] = (byte)(value >> 24);
            _cache[_pos + 1] = (byte)(value >> 16);
            _cache[_pos + 2] = (byte)(value >> 8);
            _cache[_pos + 3] = (byte)(value >> 0);
        }

        public void WriteUInt32(uint value) {
            EnsureCache(4);
            _cache[_pos + 0] = (byte)(value >> 24);
            _cache[_pos + 1] = (byte)(value >> 16);
            _cache[_pos + 2] = (byte)(value >> 8);
            _cache[_pos + 3] = (byte)(value >> 0);
        }

        /// <summary>
        /// Writes a 32 bit value as a varint. The fast route is taken when
        /// there's enough buffer space left to whizz through without checking
        /// for each byte; otherwise, we resort to calling WriteByte each time.
        /// </summary>
        public void WriteVarint32(uint value) {
            while (value > 127 && _pos < _end) {
                _cache[_pos++] = (byte)((value & 0x7F) | 0x80);
                value >>= 7;
            }
            while (value > 127) {
                WriteByte((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            if (_pos < _end) {
                _cache[_pos++] = (byte)value;
            }
            else {
                WriteByte((byte)value);
            }
        }

        public void WriteBytes(byte[] data) {
            WriteBytes(data, 0, data.Length);
        }

        public void WriteBytes(byte[] data, int offset, int length) {
            if (_end - _pos >= length) {
                Buffer.BlockCopy(data, offset, _cache, _pos, length);
                // We have room in the current buffer.
                _pos += length;
            }
            else {
                // Write extends past current buffer.  Fill the rest of this buffer and
                // flush.
                int bytesWritten = _end - _pos;
                Buffer.BlockCopy(data, offset, _cache, _pos, bytesWritten);
                offset += bytesWritten;
                length -= bytesWritten;
                _pos = _end;

                EnsureCache(length);
                Buffer.BlockCopy(data, offset, _cache, _pos, length);
                _pos += length;
            }
        }

        public void WriteBytes(ArraySegment<byte> data) {
            WriteBytes(data.Array, data.Offset, data.Count);
        }

        public void WriteString(string s, Encoding encoding) {
            if (string.IsNullOrEmpty(s)) {
                return;
            }

            var maxBytes = encoding.GetMaxByteCount(s.Length);
            if (_pos + maxBytes < _end) {
                _pos += encoding.GetBytes(s, 0, s.Length, _cache, _pos);
                return;
            }

            Flush();
            WriteBytes(encoding.GetBytes(s));
        }

        public void WriteString(string s) {
            if (!string.IsNullOrEmpty(s)) {
                WriteString(s, Encoding.UTF8);
            }
        }

        public void Flush() {
            if (_pos > _start) {
                if (_buffer != null) {
                    var size = _pos - _start;
                    if (_buffer.Count > 0) {
                        var tail = _buffer[_buffer.Count - 1];
                        if (tail.Array == _cache && tail.Offset + tail.Count == _pos) {
                            _buffer[_buffer.Count - 1] = new ArraySegment<byte>(_cache, tail.Offset, tail.Count + size);
                            _start = _pos;
                            return;
                        }
                    }
                    _buffer.Add(_cache, _start, size);
                }
                _start = _pos;
            }
        }

        private void EnsureCache(int size) {
            if (_pos + size >= _end) {
                Flush();
                _start = 0;
                _pos = 0;
                _end = Math.Max(WRITE_BUFFER_SIZE, size);
                _cache = new byte[_end];
            }
        }
    }
}
