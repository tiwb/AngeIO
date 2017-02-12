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
using System.Collections;
using System.Collections.Generic;

namespace AngeIO {
    using ByteArraySegment = ArraySegment<byte>;

    /// <summary>
    /// A list of byte array segments.
    /// </summary>
    public class BufferData : IList<ByteArraySegment> {
        private int _byteLength;
        private int _head;
        private int _segs;
        private ByteArraySegment[] _buffer;

        static byte[] _emptyBytes = new byte[0];
        static readonly ByteArraySegment[] _emptyBuffer = new ByteArraySegment[0];

        public static byte[] EmptyBytes {
            get { return _emptyBytes; }
        }

        public static BufferData FromUtf8String(string s) {
            if (string.IsNullOrEmpty(s)) {
                return new BufferData();
            }
            else {
                return new BufferData(Encoding.UTF8.GetBytes(s));
            }
        }

        public BufferData() {
            _buffer = _emptyBuffer;
        }

        public BufferData(byte[] data) {
            _buffer = new ByteArraySegment[1];
            Add(new ByteArraySegment(data));
        }

        public BufferData(byte[] data, int offset, int size) {
            _buffer = new ByteArraySegment[1];
            Add(new ByteArraySegment(data, offset, size));
        }

        public BufferData(ByteArraySegment data) {
            _buffer = new ByteArraySegment[1];
            Add(data);
        }

        public bool IsEmpty() {
            return 0 == _segs;
        }

        #region List methods

        public IEnumerator<ByteArraySegment> GetEnumerator() {

            // The below is done for performance reasons.
            // Rather than doing bounds checking and modulo arithmetic
            // that would go along with calls to Get(index), we can skip
            // all of that by referencing the underlying array.

            if (_head + _segs > _buffer.Length) {
                for (int i = _head; i < _buffer.Length; i++) {
                    yield return _buffer[i];
                }

                int endIndex = ToBufferIndex(_segs);
                for (int i = 0; i < endIndex; i++) {
                    yield return _buffer[i];
                }
            }
            else {
                int endIndex = _head + _segs;
                for (int i = _head; i < endIndex; i++) {
                    yield return _buffer[i];
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return this.GetEnumerator();
        }

        public int IndexOf(ByteArraySegment item) {
            throw new NotImplementedException();
        }

        public void Insert(int index, ByteArraySegment item) {
            throw new NotImplementedException();
        }

        public void RemoveAt(int index) {
            throw new NotImplementedException();
        }

        public ByteArraySegment this[int index] {
            get {
                if (index < 0 || index >= _segs) {
                    throw new IndexOutOfRangeException("index");
                }
                return _buffer[ToBufferIndex(index)];
            }
            set {
                if (index < 0 || index >= _segs) {
                    throw new IndexOutOfRangeException("index");
                }
                if (value.Count == 0) {
                    RemoveAt(index);
                    return;
                }
                var idx = ToBufferIndex(index);
                _byteLength += value.Count - _buffer[idx].Count;
                _buffer[idx] = value;
            }
        }

        public void Add(ByteArraySegment item) {
            if (item.Count > 0) {
                EnsureCapacityFor(1);
                _buffer[ToBufferIndex(_segs)] = item;
                _byteLength += item.Count;
                _segs++;
            }
        }

        public void Add(byte[] buff) {
            Add(new ByteArraySegment(buff));
        }

        public void Add(byte[] buff, int offset, int size) {
            Add(new ByteArraySegment(buff, offset, size));
        }

        public void Add(BufferData buff, int size) {
            if (size > buff._byteLength)
                throw new Exception("Invalid size");

            int numItems = 0;
            int size1 = 0;
            foreach (var item in buff) {
                size1 += item.Count;
                numItems++;
                if (size1 >= size)
                    break;
            }

            EnsureCapacityFor(numItems);
            foreach (var item in buff) {
                if (size <= 0)
                    break;

                if (item.Count == 0)
                    continue;

                if (item.Count <= size) {
                    _buffer[ToBufferIndex(_segs)] = item;
                    _segs++;
                    _byteLength += item.Count;
                    size -= item.Count;
                }
                else {
                    _buffer[ToBufferIndex(_segs)] = new ByteArraySegment(item.Array, item.Offset, size);
                    _segs++;
                    _byteLength += size;
                    size = 0;
                    break;
                }
            }

            if (size != 0) {
                throw new Exception("Invalid size");
            }
        }

        public void AddRange(IEnumerable<ByteArraySegment> collection) {
            int numItems = 0;
            foreach (var item in collection) numItems++;
            EnsureCapacityFor(numItems);

            foreach (var item in collection) {
                if (item.Count > 0) {
                    _buffer[ToBufferIndex(_segs)] = item;
                    _segs++;
                    _byteLength += item.Count;
                }
            }
        }


        public void Clear() {
            if (_segs > 0) {
                Array.Clear(_buffer, 0, _buffer.Length);
            }
            _segs = 0;
            _byteLength = 0;
            _head = 0;
        }

        public bool Contains(ByteArraySegment item) {
            throw new NotImplementedException();
        }

        public void CopyTo(ByteArraySegment[] array, int arrayIndex) {
            if (null == array) {
                throw new ArgumentNullException("array");
            }

            if (arrayIndex + _segs > array.Length) {
                throw new ArgumentOutOfRangeException("arrayIndex");
            }

            if (0 != _head && _head + _segs >= _buffer.Length) {
                int lengthFromStart = _buffer.Length - _head;
                int lengthFromEnd = _segs - lengthFromStart;

                Array.Copy(_buffer, _head, array, arrayIndex, lengthFromStart);
                Array.Copy(_buffer, 0, array, arrayIndex + lengthFromStart, lengthFromEnd);
            }
            else {
                Array.Copy(_buffer, _head, array, arrayIndex, _segs);
            }
        }

        public int ByteLength {
            get { return _byteLength; }
        }

        public int Count {
            get { return _segs; }
        }

        public bool IsReadOnly {
            get { return false; }
        }

        public bool Remove(ByteArraySegment item) {
            throw new NotImplementedException();
        }

        #endregion

        public byte[] ToByteArray() {
            var result = new byte[ByteLength];
            var p = 0;
            var i = GetEnumerator();
            while (i.MoveNext()) {
                var buf = i.Current;
                System.Buffer.BlockCopy(buf.Array, buf.Offset, result, p, buf.Count);
                p += buf.Count;
            }
            return result;
        }

        public string ToString(Encoding encoding) {
            if (_segs == 0) {
                return string.Empty;
            }
            else if (_segs == 1) {
                var buf = _buffer[_head];
                return encoding.GetString(buf.Array, buf.Offset, buf.Count);
            }
            else {
                var size = _byteLength;
                var s = new char[size];
                var p = 0;
                var i = GetEnumerator();
                while (i.MoveNext()) {
                    var buf = i.Current;
                    p += encoding.GetChars(buf.Array, buf.Offset, buf.Count, s, p);
                }
                return new string(s, 0, p);
            }
        }

        public ByteArraySegment RemoveFront() {
            if (_segs > 0) {
                ByteArraySegment ret = _buffer[_head];
                _buffer[_head] = default(ByteArraySegment);
                _head = ToBufferIndex(1);
                _segs--;
                return ret;
            }
            return default(ByteArraySegment);
        }

        public void RemoveFontBytes(int size) {
            if (_byteLength < size) {
                throw new ArgumentException("size");
            }
            _byteLength -= size;

            // Faster to skip all.
            if (_byteLength == 0) {
                Clear();
                return;
            }

            while (size > 0) {
                var buf = _buffer[_head];
                if (size >= buf.Count) {
                    _buffer[_head] = default(ByteArraySegment);
                    _head = ToBufferIndex(1);
                    _segs--;
                    size -= buf.Count;
                }
                else {
                    _buffer[_head] = new ByteArraySegment(buf.Array, buf.Offset + size, buf.Count - size);
                    break;
                }
            }
        }


        private int ToBufferIndex(int index) {
            int bufferIndex = index + _head;
            if (bufferIndex >= _buffer.Length) {
                bufferIndex -= _buffer.Length;
            }
            return bufferIndex;
        }

        private void EnsureCapacityFor(int numElements) {
            if (_segs + numElements > _buffer.Length) {
                var newsize = Math.Max(_segs + numElements, Math.Max(32, _segs + _segs / 2));
                var newBuffer = new ByteArraySegment[newsize];
                CopyTo(newBuffer, 0);

                // Set up to use the new buffer.
                _buffer = newBuffer;
                _head = 0;
            }
        }
    }
}
