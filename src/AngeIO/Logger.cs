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

namespace AngeIO {
    /// <summary>
    /// Specifies the logging level.
    /// </summary>
    public enum LogLevel {
        /// <summary>
        /// Specifies the bottom logging level.
        /// </summary>
        Trace,
        /// <summary>
        /// Specifies the 2nd logging level from the bottom.
        /// </summary>
        Debug,
        /// <summary>
        /// Specifies the 3rd logging level from the bottom.
        /// </summary>
        Info,
        /// <summary>
        /// Specifies the 3rd logging level from the top.
        /// </summary>
        Warn,
        /// <summary>
        /// Specifies the 2nd logging level from the top.
        /// </summary>
        Error,
        /// <summary>
        /// Specifies the top logging level.
        /// </summary>
        Fatal
    }

    /// <summary>
    /// Logger
    /// </summary>
    class Logger {
        private LogLevel _level = LogLevel.Debug;
        private Action<string, LogLevel> _output;

        [ThreadStatic]
        private static Logger _instance;

        private void Output(string s, LogLevel level) {
            if (level <= this._level)
                Console.WriteLine(s);
        }

        #region Public Methods

        public static Logger Get() {
            return _instance ?? (_instance = new Logger());
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public Logger() {
            _output = Output;
        }

        /// <summary>
        /// Outputs <paramref name="message"/> as a log with <see cref="LogLevel.Debug"/>.
        /// </summary>
        /// <remarks>
        /// If the current logging level is higher than <see cref="LogLevel.Debug"/>,
        /// this method doesn't output <paramref name="message"/> as a log.
        /// </remarks>
        /// <param name="message">
        /// A <see cref="string"/> that represents the message to output as a log.
        /// </param>
        public void Debug(string message) {
            this?._output(message, LogLevel.Debug);
        }

        /// <summary>
        /// Outputs <paramref name="message"/> as a log with <see cref="LogLevel.Error"/>.
        /// </summary>
        /// <remarks>
        /// If the current logging level is higher than <see cref="LogLevel.Error"/>,
        /// this method doesn't output <paramref name="message"/> as a log.
        /// </remarks>
        /// <param name="message">
        /// A <see cref="string"/> that represents the message to output as a log.
        /// </param>
        public void Error(string message) {
            this?._output(message, LogLevel.Error);
        }

        /// <summary>
        /// Outputs <paramref name="message"/> as a log with <see cref="LogLevel.Fatal"/>.
        /// </summary>
        /// <param name="message">
        /// A <see cref="string"/> that represents the message to output as a log.
        /// </param>
        public void Fatal(string message) {
            this?._output(message, LogLevel.Fatal);
        }

        /// <summary>
        /// Outputs <paramref name="message"/> as a log with <see cref="LogLevel.Info"/>.
        /// </summary>
        /// <remarks>
        /// If the current logging level is higher than <see cref="LogLevel.Info"/>,
        /// this method doesn't output <paramref name="message"/> as a log.
        /// </remarks>
        /// <param name="message">
        /// A <see cref="string"/> that represents the message to output as a log.
        /// </param>
        public void Info(string message) {
            this?._output(message, LogLevel.Info);
        }

        /// <summary>
        /// Outputs <paramref name="message"/> as a log with <see cref="LogLevel.Trace"/>.
        /// </summary>
        /// <remarks>
        /// If the current logging level is higher than <see cref="LogLevel.Trace"/>,
        /// this method doesn't output <paramref name="message"/> as a log.
        /// </remarks>
        /// <param name="message">
        /// A <see cref="string"/> that represents the message to output as a log.
        /// </param>
        public void Trace(string message) {
            this?._output(message, LogLevel.Trace);
        }

        /// <summary>
        /// Outputs <paramref name="message"/> as a log with <see cref="LogLevel.Warn"/>.
        /// </summary>
        /// <remarks>
        /// If the current logging level is higher than <see cref="LogLevel.Warn"/>,
        /// this method doesn't output <paramref name="message"/> as a log.
        /// </remarks>
        /// <param name="message">
        /// A <see cref="string"/> that represents the message to output as a log.
        /// </param>
        public void Warn(string message) {
            this?._output(message, LogLevel.Warn);
        }

        #endregion
    }
}
