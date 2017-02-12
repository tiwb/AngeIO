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
using System.Diagnostics;

namespace AngeIO {
    /// <summary>
    /// Debug Tracer
    /// </summary>
    public static class DebugOutput {
        static Logger _logger = Logger.Get();

        [Conditional("DEBUG_OUTPUT")]
        public static void Debug(string message) {
            _logger.Debug(message);
        }

        [Conditional("DEBUG_OUTPUT")]
        public static void Error(string message) {
            _logger.Error(message);
        }

        [Conditional("DEBUG_OUTPUT")]
        public static void Fatal(string message) {
            _logger.Fatal(message);
        }

        [Conditional("DEBUG_OUTPUT")]
        public static void Info(string message) {
            _logger.Info(message);
        }

        [Conditional("DEBUG_OUTPUT")]
        public static void Trace(string message) {
            _logger.Trace(message);
        }

        [Conditional("DEBUG_OUTPUT")]
        public static void Warn(string message) {
            _logger.Warn(message);
        }
    }
}
