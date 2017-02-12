#region License
/*
 * The MIT License
 *
 * Original based on src/http/ngx_http_parse.c from NGINX 
 * Ported from C version at: https://github.com/nodejs/http-parser
 *
 * Copyright Igor Sysoev
 * Copyright Joyent, Inc. and other Node contributors. All rights reserved.
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

#define HTTP_PARSER_STRICT

using System;

#pragma warning disable CS0649

namespace AngeIO.Web {
    /// <summary>
    /// A Http parser
    /// </summary>
    public class HttpParser {
        #region Constants
        enum ParserState {
            dead = 1, /* important that this is > 0 */
            start_req_or_res,
            res_or_resp_H,
            start_res,
            res_H,
            res_HT,
            res_HTT,
            res_HTTP,
            res_first_http_major,
            res_http_major,
            res_first_http_minor,
            res_http_minor,
            res_first_status_code,
            res_status_code,
            res_status_start,
            res_status,
            res_line_almost_done,

            start_req,

            req_method,
            req_spaces_before_url,
            req_schema,
            req_schema_slash,
            req_schema_slash_slash,
            req_server_start,
            req_server,
            req_server_with_at,
            req_path,
            req_query_string_start,
            req_query_string,
            req_fragment_start,
            req_fragment,
            req_http_start,
            req_http_H,
            req_http_HT,
            req_http_HTT,
            req_http_HTTP,
            req_first_http_major,
            req_http_major,
            req_first_http_minor,
            req_http_minor,
            req_line_almost_done,

            header_field_start,
            header_field,
            header_value_discard_ws,
            header_value_discard_ws_almost_done,
            header_value_discard_lws,
            header_value_start,
            header_value,
            header_value_lws,

            header_almost_done,

            chunk_size_start,
            chunk_size,
            chunk_parameters,
            chunk_size_almost_done,

            headers_almost_done,
            headers_done,

            /* Important: 's_headers_done' must be the last 'header' state. All,
             * states beyond this must be 'body' states. It is used for overflow
             * checking. See the PARSING_HEADER() macro.
             */

            chunk_data,
            chunk_data_almost_done,
            chunk_data_done,

            body_identity,
            body_identity_eof,

            message_done,
        }

        enum HeaderState {
            general = 0,
            C,
            CO,
            CON,

            matching_connection,
            matching_proxy_connection,
            matching_content_length,
            matching_transfer_encoding,
            matching_upgrade,

            connection,
            content_length,
            transfer_encoding,
            upgrade,

            matching_transfer_encoding_chunked,
            matching_connection_token_start,
            matching_connection_keep_alive,
            matching_connection_close,
            matching_connection_upgrade,
            matching_connection_token,

            transfer_encoding_chunked,
            connection_keep_alive,
            connection_close,
            connection_upgrade,
        }

        [Flags]
        enum HttpFlags {
            CHUNKED = 1 << 0,
            CONNECTION_KEEP_ALIVE = 1 << 1,
            CONNECTION_CLOSE = 1 << 2,
            CONNECTION_UPGRADE = 1 << 3,
            TRAILING = 1 << 4,
            UPGRADE = 1 << 5,
            SKIPBODY = 1 << 6,
            CONTENTLENGTH = 1 << 7,
        };

        private const byte CR = (byte)'\r';
        private const byte LF = (byte)'\n';
        private const int MAX_HEADER_SIZE = (80 * 1024);
        private const string PROXY_CONNECTION = "proxy-connection";
        private const string CONNECTION = "connection";
        private const string CONTENT_LENGTH = "content-length";
        private const string TRANSFER_ENCODING = "transfer-encoding";
        private const string UPGRADE = "upgrade";
        private const string CHUNKED = "chunked";
        private const string KEEP_ALIVE = "keep-alive";
        private const string CLOSE = "close";


        private static readonly byte[] normal_url_char;
        private static readonly char[] tokens;
        private static readonly sbyte[] unhex;

        #endregion

        private HttpParserType _type;
        private HttpFlags _flags;
        private ParserState _state;
        private HeaderState _header_state;
        private int _index;                  /* index into current matcher */
        private bool _lenient_http_headers;

        private uint _nread;
        private ulong _content_length;
        private bool _incomplete;

        private ushort _http_major;
        private ushort _http_minor;
        private int _status_code;
        private HttpMethod _method;
        private HttpParserError _http_errno;

        private bool _upgrade;

        // Delegates
        public delegate int DataCallback(ArraySegment<byte> data);
        public delegate int Callback();

        // Callbacks
        public Callback on_message_begin;
        public DataCallback on_url;
        public DataCallback on_status;
        public DataCallback on_header_field;
        public DataCallback on_header_value;
        public Callback on_headers_complete;
        public DataCallback on_body;
        public Callback on_message_complete;

        /* When on_chunk_header is called, the current chunk length is stored
         * in parser->content_length.
         */
        public Callback on_chunk_header;
        public Callback on_chunk_complete;


        /// <summary>
        /// Lenient HTTP headers
        /// </summary>
        public bool LenientHttpHeaders {
            get {
                return _lenient_http_headers;
            }

            set {
                _lenient_http_headers = value;
            }
        }

        /// <summary>
        /// # bytes read in various scenarios
        /// </summary>
        public int NumBytesRead {
            get { return (int)_nread; }
        }

        /// <summary>
        /// # bytes in body (0 if no Content-Length header)
        /// </summary>
        public ulong ContentLength {
            get { return _content_length; }
        }

        /// <summary>
        /// HTTP Major version
        /// </summary>
        public ushort HttpMajor {
            get {
                return _http_major;
            }
        }

        /// <summary>
        /// HTTP Minior version
        /// </summary>
        public ushort HttpMinor {
            get {
                return _http_minor;
            }
        }

        /// <summary>
        /// Response only
        /// </summary>
        public int StatusCode {
            get {
                return _status_code;
            }
        }

        /// <summary>
        /// Request only
        /// </summary>
        public HttpMethod Method {
            get {
                return _method;
            }
        }

        /// <summary>
        /// Error number.
        /// </summary>
        public HttpParserError Errno {
            get {
                return _http_errno;
            }
        }

        /* true = Upgrade header was present and the parser has exited because of that.
         * false = No upgrade header present.
         * Should be checked when Execute() returns in addition to
         * error checking.
         */
        public bool Upgrade {
            get {
                return _upgrade;
            }
        }

        /// <summary>
        /// Constructs a HttpParser
        /// </summary>
        /// <param name="type"></param>
        public HttpParser(HttpParserType type) {
            _type = type;
            _state = (type == HttpParserType.REQUEST ? ParserState.start_req : (type == HttpParserType.RESPONSE ? ParserState.start_res : ParserState.start_req_or_res));
            _http_errno = HttpParserError.OK;
        }


        /// <summary>
        /// Pause
        /// </summary>
        /// <param name="pause"></param>
        public void Pause(bool paused) {
            // Users should only be pausing/unpausing a parser that is not in an error
            // state. In non-debug builds, there's not much that we can do about this
            // other than ignore it.
            if (_http_errno == HttpParserError.OK ||
                _http_errno == HttpParserError.PAUSED) {
                _http_errno = (paused) ? HttpParserError.PAUSED : HttpParserError.OK;
            }
            else {
                throw new InvalidOperationException("Attempting to pause parser in error state");
            }
        }

        /// <summary>
        /// If ShouldKeepAlive() in the on_headers_complete or
        /// on_message_complete callback returns 0, then this should be
        /// the last message on the connection.
        /// If you are the server, respond with the "Connection: close" header.
        /// If you are the client, close the connection.
        /// </summary>
        public bool ShouldKeepAlive() {
            if (_http_major > 0 && _http_minor > 0) {
                /* HTTP/1.1 */
                if ((_flags & HttpFlags.CONNECTION_CLOSE) != 0) {
                    return false;
                }
            }
            else {
                /* HTTP/1.0 or earlier */
                if ((_flags & HttpFlags.CONNECTION_KEEP_ALIVE) == 0) {
                    return false;
                }
            }

            return !http_message_needs_eof();
        }

        /// <summary>
        /// Checks if this is the final chunk of the body.
        /// </summary>
        public bool BodyIsFinal() {
            return _state == ParserState.message_done;
        }

        /// <summary>
        /// Check if the parser is incomplete
        /// </summary>
        public bool IsIncomplete() {
            return _incomplete;
        }

        /// <summary>
        /// Executes the parser.
        /// Sets <see cref="Errno"/> on error.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="len"></param>
        /// <returns>number of parsed bytes.</returns>
        public int Execute(byte[] buffer, int offset, int len) {
            byte[] arr = buffer;
            int data = offset;
            byte c, ch;
            sbyte unhex_val;
            int p = data;
            int header_field_mark = -1;
            int header_value_mark = -1;
            int url_mark = -1;
            int body_mark = -1;
            int status_mark = -1;

            _incomplete = false;

            /* We're in an error state. Don't bother doing anything. */
            if (_http_errno != HttpParserError.OK) {
                return 0;
            }

            if (len == 0) {
                switch (_state) {
                    case ParserState.body_identity_eof:
                        /* Use of CALLBACK_NOTIFY() here would erroneously return 1 byte read if
                         * we got paused.
                         */
                        if (CALLBACK_NOTIFY(on_message_complete) != 0) {
                            _http_errno = HttpParserError.CB_message_complete;
                            goto error;
                        }
                        return 0;

                    case ParserState.dead:
                    case ParserState.start_req_or_res:
                    case ParserState.start_res:
                    case ParserState.start_req:
                        return 0;

                    default:
                        _http_errno = (HttpParserError.INVALID_EOF_STATE);
                        return 1;
                }
            }


            if (_state == ParserState.header_field)
                header_field_mark = data;
            if (_state == ParserState.header_value)
                header_value_mark = data;
            switch (_state) {
                case ParserState.req_path:
                case ParserState.req_schema:
                case ParserState.req_schema_slash:
                case ParserState.req_schema_slash_slash:
                case ParserState.req_server_start:
                case ParserState.req_server:
                case ParserState.req_server_with_at:
                case ParserState.req_query_string_start:
                case ParserState.req_query_string:
                case ParserState.req_fragment_start:
                case ParserState.req_fragment:
                    url_mark = data;
                    break;
                case ParserState.res_status:
                    status_mark = data;
                    break;
                default:
                    break;
            }

            for (p = data; p != data + len; p++) {
                ch = buffer[p];

                if (_state <= ParserState.headers_done) {
                    _nread += (1);
                    if (_nread > MAX_HEADER_SIZE) {
                        _http_errno = HttpParserError.HEADER_OVERFLOW;
                        goto error;
                    }
                }

                reexecute:
                switch (_state) {

                    case ParserState.dead:
                        /* this state is used after a 'Connection: close' message
                         * the parser will error out if it reads another message
                         */
                        if ((ch == CR || ch == LF))
                            break;

                        _http_errno = (HttpParserError.CLOSED_CONNECTION);
                        goto error;

                    case ParserState.start_req_or_res: {
                            if (ch == CR || ch == LF)
                                break;
                            _flags = 0;
                            _content_length = ulong.MaxValue;

                            if (ch == 'H') {
                                _state = (ParserState.res_or_resp_H);

                                if (CALLBACK_NOTIFY(on_message_begin) != 0) {
                                    _http_errno = HttpParserError.CB_message_begin;
                                    p++;
                                    goto error;
                                }
                            }
                            else {
                                _type = HttpParserType.REQUEST;
                                _state = (ParserState.start_req);
                                goto reexecute;
                            }

                            break;
                        }

                    case ParserState.res_or_resp_H:
                        if (ch == 'T') {
                            _type = HttpParserType.RESPONSE;
                            _state = (ParserState.res_HT);
                        }
                        else {
                            if ((ch != 'E')) {
                                _http_errno = (HttpParserError.INVALID_CONSTANT);
                                goto error;
                            }

                            _type = HttpParserType.REQUEST;
                            _method = HttpMethod.HEAD;
                            _index = 2;
                            _state = (ParserState.req_method);
                        }
                        break;

                    case ParserState.start_res: {
                            _flags = 0;
                            _content_length = ulong.MaxValue;

                            switch (ch) {
                                case (byte)'H':
                                    _state = (ParserState.res_H);
                                    break;

                                case CR:
                                case LF:
                                    break;

                                default:
                                    _http_errno = (HttpParserError.INVALID_CONSTANT);
                                    goto error;
                            }

                            if (CALLBACK_NOTIFY(on_message_begin) != 0) {
                                _http_errno = HttpParserError.CB_message_begin;
                                p++;
                                goto error;
                            }
                            break;
                        }

                    case ParserState.res_H:
#if HTTP_PARSER_STRICT
                        if (ch != 'T') { _http_errno = HttpParserError.STRICT; goto error; }
#endif
                        _state = (ParserState.res_HT);
                        break;

                    case ParserState.res_HT:
#if HTTP_PARSER_STRICT
                        if (ch != 'T') { _http_errno = HttpParserError.STRICT; goto error; }
#endif
                        _state = (ParserState.res_HTT);
                        break;

                    case ParserState.res_HTT:
#if HTTP_PARSER_STRICT
                        if (ch != 'P') { _http_errno = HttpParserError.STRICT; goto error; };
#endif
                        _state = (ParserState.res_HTTP);
                        break;

                    case ParserState.res_HTTP:
#if HTTP_PARSER_STRICT
                        if (ch != '/') { _http_errno = HttpParserError.STRICT; goto error; };
#endif
                        _state = (ParserState.res_first_http_major);
                        break;

                    case ParserState.res_first_http_major:
                        if ((ch < '0' || ch > '9')) {
                            _http_errno = (HttpParserError.INVALID_VERSION);
                            goto error;
                        }

                        _http_major = (ushort)(ch - '0');
                        _state = (ParserState.res_http_major);
                        break;

                    /* major HTTP version or dot */
                    case ParserState.res_http_major: {
                            if (ch == '.') {
                                _state = (ParserState.res_first_http_minor);
                                break;
                            }

                            if (!(ch >= '0' && ch <= '9')) {
                                _http_errno = (HttpParserError.INVALID_VERSION);
                                goto error;
                            }

                            _http_major *= 10;
                            _http_major += (ushort)(ch - '0');

                            if ((_http_major > 999)) {
                                _http_errno = (HttpParserError.INVALID_VERSION);
                                goto error;
                            }

                            break;
                        }

                    /* first digit of minor HTTP version */
                    case ParserState.res_first_http_minor:
                        if ((!(ch >= '0' && ch <= '9'))) {
                            _http_errno = (HttpParserError.INVALID_VERSION);
                            goto error;
                        }

                        _http_minor = (ushort)(ch - '0');
                        _state = (ParserState.res_http_minor);
                        break;

                    /* minor HTTP version or end of request line */
                    case ParserState.res_http_minor: {
                            if (ch == ' ') {
                                _state = (ParserState.res_first_status_code);
                                break;
                            }

                            if ((!(ch >= '0' && ch <= '9'))) {
                                _http_errno = (HttpParserError.INVALID_VERSION);
                                goto error;
                            }

                            _http_minor *= 10;
                            _http_minor += (ushort)(ch - '0');

                            if ((_http_minor > 999)) {
                                _http_errno = (HttpParserError.INVALID_VERSION);
                                goto error;
                            }

                            break;
                        }

                    case ParserState.res_first_status_code: {
                            if (!(ch >= '0' && ch <= '9')) {
                                if (ch == ' ') {
                                    break;
                                }

                                _http_errno = (HttpParserError.INVALID_STATUS);
                                goto error;
                            }
                            _status_code = (ch - '0');
                            _state = (ParserState.res_status_code);
                            break;
                        }

                    case ParserState.res_status_code: {
                            if (!(ch >= '0' && ch <= '9')) {
                                switch (ch) {
                                    case (byte)' ':
                                        _state = (ParserState.res_status_start);
                                        break;
                                    case CR:
                                        _state = (ParserState.res_line_almost_done);
                                        break;
                                    case LF:
                                        _state = (ParserState.header_field_start);
                                        break;
                                    default:
                                        _http_errno = (HttpParserError.INVALID_STATUS);
                                        goto error;
                                }
                                break;
                            }

                            _status_code *= 10;
                            _status_code += ch - '0';

                            if ((_status_code > 999)) {
                                _http_errno = (HttpParserError.INVALID_STATUS);
                                goto error;
                            }

                            break;
                        }

                    case ParserState.res_status_start: {
                            if (ch == CR) {
                                _state = (ParserState.res_line_almost_done);
                                break;
                            }

                            if (ch == LF) {
                                _state = (ParserState.header_field_start);
                                break;
                            }

                            if (status_mark == -1) status_mark = p;
                            _state = (ParserState.res_status);
                            _index = 0;
                            break;
                        }

                    case ParserState.res_status:
                        if (ch == CR) {
                            _state = (ParserState.res_line_almost_done);
                            if (CALLBACK_DATA(on_status, p, ref status_mark, buffer) != 0) {
                                _http_errno = HttpParserError.CB_status;
                                p++;
                                goto error;
                            }
                            break;
                        }

                        if (ch == LF) {
                            _state = (ParserState.header_field_start);
                            if (CALLBACK_DATA(on_status, p, ref status_mark, buffer) != 0) {
                                _http_errno = HttpParserError.CB_status;
                                p++;
                                goto error;
                            }
                            break;
                        }

                        break;

                    case ParserState.res_line_almost_done:
#if HTTP_PARSER_STRICT
                        if (ch != LF) { _http_errno = HttpParserError.STRICT; goto error; };
#endif
                        _state = (ParserState.header_field_start);
                        break;

                    case ParserState.start_req: {
                            if (ch == CR || ch == LF)
                                break;
                            _flags = 0;
                            _content_length = ulong.MaxValue;

                            if ((!IS_ALPHA(ch))) {
                                _http_errno = (HttpParserError.INVALID_METHOD);
                                goto error;
                            }

                            _method = (HttpMethod)0;
                            _index = 1;
                            switch ((char)ch) {
                                case 'A': _method = HttpMethod.ACL; break;
                                case 'B': _method = HttpMethod.BIND; break;
                                case 'C': _method = HttpMethod.CONNECT; break; /* or COPY, CHECKOUT */
                                case 'D': _method = HttpMethod.DELETE; break;
                                case 'G': _method = HttpMethod.GET; break;
                                case 'H': _method = HttpMethod.HEAD; break;
                                case 'L': _method = HttpMethod.LOCK; break; /* or LINK */
                                case 'M': _method = HttpMethod.MKCOL; break; /* or MOVE, MKACTIVITY, MERGE, M-SEARCH, MKCALENDAR */
                                case 'N': _method = HttpMethod.NOTIFY; break;
                                case 'O': _method = HttpMethod.OPTIONS; break;
                                case 'P': _method = HttpMethod.POST; break; /* or PROPFIND|PROPPATCH|PUT|PATCH|PURGE */
                                case 'R': _method = HttpMethod.REPORT; break; /* or REBIND */
                                case 'S': _method = HttpMethod.SUBSCRIBE; break; /* or SEARCH */
                                case 'T': _method = HttpMethod.TRACE; break;
                                case 'U': _method = HttpMethod.UNLOCK; break; /* or UNSUBSCRIBE, UNBIND, UNLINK */
                                default:
                                    _http_errno = (HttpParserError.INVALID_METHOD);
                                    goto error;
                            }
                            _state = (ParserState.req_method);

                            if (CALLBACK_NOTIFY(on_message_begin) != 0) {
                                _http_errno = HttpParserError.CB_message_begin;
                                p++;
                                goto error;
                            }

                            break;
                        }

                    case ParserState.req_method: {
                            string matcher;
                            if ((ch == '\0')) {
                                _http_errno = (HttpParserError.INVALID_METHOD);
                                goto error;
                            }

                            matcher = _method.GetDescription();
                            if (ch == ' ' && _index == matcher.Length) {
                                _state = (ParserState.req_spaces_before_url);
                            }
                            else if (_index < matcher.Length && ch == matcher[_index]) {
                                ; /* nada */
                            }
                            else if (IS_ALPHA(ch)) {

                                switch (((int)_method << 16) | (_index << 8) | ch) {
                                    case ((int)HttpMethod.POST << 16) | (1 << 8) | 'U': _method = HttpMethod.PUT; break;
                                    case ((int)HttpMethod.POST << 16) | (1 << 8) | 'A': _method = HttpMethod.PATCH; break;
                                    case ((int)HttpMethod.CONNECT << 16) | (1 << 8) | 'H': _method = HttpMethod.CHECKOUT; break;
                                    case ((int)HttpMethod.CONNECT << 16) | (2 << 8) | 'P': _method = HttpMethod.COPY; break;
                                    case ((int)HttpMethod.MKCOL << 16) | (1 << 8) | 'O': _method = HttpMethod.MOVE; break;
                                    case ((int)HttpMethod.MKCOL << 16) | (1 << 8) | 'E': _method = HttpMethod.MERGE; break;
                                    case ((int)HttpMethod.MKCOL << 16) | (2 << 8) | 'A': _method = HttpMethod.MKACTIVITY; break;
                                    case ((int)HttpMethod.MKCOL << 16) | (3 << 8) | 'A': _method = HttpMethod.MKCALENDAR; break;
                                    case ((int)HttpMethod.SUBSCRIBE << 16) | (1 << 8) | 'E': _method = HttpMethod.SEARCH; break;
                                    case ((int)HttpMethod.REPORT << 16) | (2 << 8) | 'B': _method = HttpMethod.REBIND; break;
                                    case ((int)HttpMethod.POST << 16) | (1 << 8) | 'R': _method = HttpMethod.PROPFIND; break;
                                    case ((int)HttpMethod.PROPFIND << 16) | (4 << 8) | 'P': _method = HttpMethod.PROPPATCH; break;
                                    case ((int)HttpMethod.PUT << 16) | (2 << 8) | 'R': _method = HttpMethod.PURGE; break;
                                    case ((int)HttpMethod.LOCK << 16) | (1 << 8) | 'I': _method = HttpMethod.LINK; break;
                                    case ((int)HttpMethod.UNLOCK << 16) | (2 << 8) | 'S': _method = HttpMethod.UNSUBSCRIBE; break;
                                    case ((int)HttpMethod.UNLOCK << 16) | (2 << 8) | 'B': _method = HttpMethod.UNBIND; break;
                                    case ((int)HttpMethod.UNLOCK << 16) | (3 << 8) | 'I': _method = HttpMethod.UNLINK; break;
                                    default:
                                        _http_errno = (HttpParserError.INVALID_METHOD);
                                        goto error;
                                }
                            }
                            else if (ch == '-' &&
                                     _index == 1 &&
                                     _method == HttpMethod.MKCOL) {
                                _method = HttpMethod.MSEARCH;
                            }
                            else {
                                _http_errno = (HttpParserError.INVALID_METHOD);
                                goto error;
                            }

                            ++_index;
                            break;
                        }

                    case ParserState.req_spaces_before_url: {
                            if (ch == ' ') break;

                            if (url_mark == -1) url_mark = p;
                            if (_method == HttpMethod.CONNECT) {
                                _state = (ParserState.req_server_start);
                            }

                            _state = (parse_url_char(_state, ch));
                            if ((_state == ParserState.dead)) {
                                _http_errno = (HttpParserError.INVALID_URL);
                                goto error;
                            }

                            break;
                        }

                    case ParserState.req_schema:
                    case ParserState.req_schema_slash:
                    case ParserState.req_schema_slash_slash:
                    case ParserState.req_server_start: {
                            /* No whitespace allowed here */
                            if (ch == ' ' || ch == CR || ch == LF) {
                                _http_errno = (HttpParserError.INVALID_URL);
                                goto error;
                            }
                            else {
                                _state = (parse_url_char(_state, ch));
                                if ((_state == ParserState.dead)) {
                                    _http_errno = (HttpParserError.INVALID_URL);
                                    goto error;
                                }
                            }
                            break;
                        }

                    case ParserState.req_server:
                    case ParserState.req_server_with_at:
                    case ParserState.req_path:
                    case ParserState.req_query_string_start:
                    case ParserState.req_query_string:
                    case ParserState.req_fragment_start:
                    case ParserState.req_fragment: {
                            if (ch == ' ') {
                                _state = (ParserState.req_http_start);
                                if (CALLBACK_DATA(on_url, p, ref url_mark, buffer) != 0) {
                                    _http_errno = HttpParserError.CB_url;
                                    p++;
                                    goto error;
                                }
                            }
                            else if (ch == CR || ch == LF) {
                                _http_major = 0;
                                _http_minor = 9;
                                _state = ((ch == CR) ?
                                  ParserState.req_line_almost_done :
                                  ParserState.header_field_start);
                                if (CALLBACK_DATA(on_url, p, ref url_mark, buffer) != 0) {
                                    _http_errno = HttpParserError.CB_url;
                                    p++;
                                    goto error;
                                }
                            }
                            else {
                                _state = (parse_url_char(_state, ch));
                                if ((_state == ParserState.dead)) {
                                    _http_errno = (HttpParserError.INVALID_URL);
                                    goto error;
                                }
                            }
                            break;
                        }

                    case ParserState.req_http_start:
                        switch ((char)ch) {
                            case 'H':
                                _state = (ParserState.req_http_H);
                                break;
                            case ' ':
                                break;
                            default:
                                _http_errno = (HttpParserError.INVALID_CONSTANT);
                                goto error;
                        }
                        break;

                    case ParserState.req_http_H:
#if HTTP_PARSER_STRICT
                        if (ch != 'T') { _http_errno = HttpParserError.STRICT; goto error; };
#endif
                        _state = (ParserState.req_http_HT);
                        break;

                    case ParserState.req_http_HT:
#if HTTP_PARSER_STRICT
                        if (ch != 'T') { _http_errno = HttpParserError.STRICT; goto error; };
#endif
                        _state = (ParserState.req_http_HTT);
                        break;

                    case ParserState.req_http_HTT:
#if HTTP_PARSER_STRICT
                        if (ch != 'P') { _http_errno = HttpParserError.STRICT; goto error; };
#endif
                        _state = (ParserState.req_http_HTTP);
                        break;

                    case ParserState.req_http_HTTP:
#if HTTP_PARSER_STRICT
                        if (ch != '/') { _http_errno = HttpParserError.STRICT; goto error; };
#endif
                        _state = (ParserState.req_first_http_major);
                        break;

                    /* first digit of major HTTP version */
                    case ParserState.req_first_http_major:
                        if ((ch < '1' || ch > '9')) {
                            _http_errno = (HttpParserError.INVALID_VERSION);
                            goto error;
                        }

                        _http_major = (ushort)(ch - '0');
                        _state = (ParserState.req_http_major);
                        break;

                    /* major HTTP version or dot */
                    case ParserState.req_http_major: {
                            if (ch == '.') {
                                _state = (ParserState.req_first_http_minor);
                                break;
                            }

                            if ((!(ch >= '0' && ch <= '9'))) {
                                _http_errno = (HttpParserError.INVALID_VERSION);
                                goto error;
                            }

                            _http_major *= 10;
                            _http_major += (ushort)(ch - '0');

                            if ((_http_major > 999)) {
                                _http_errno = (HttpParserError.INVALID_VERSION);
                                goto error;
                            }

                            break;
                        }

                    /* first digit of minor HTTP version */
                    case ParserState.req_first_http_minor:
                        if ((!(ch >= '0' && ch <= '9'))) {
                            _http_errno = (HttpParserError.INVALID_VERSION);
                            goto error;
                        }

                        _http_minor = (ushort)(ch - '0');
                        _state = (ParserState.req_http_minor);
                        break;

                    /* minor HTTP version or end of request line */
                    case ParserState.req_http_minor: {
                            if (ch == CR) {
                                _state = (ParserState.req_line_almost_done);
                                break;
                            }

                            if (ch == LF) {
                                _state = (ParserState.header_field_start);
                                break;
                            }

                            /* XXX allow spaces after digit? */

                            if ((!IS_NUM(ch))) {
                                _http_errno = (HttpParserError.INVALID_VERSION);
                                goto error;
                            }

                            _http_minor *= 10;
                            _http_minor += (ushort)(ch - '0');

                            if ((_http_minor > 999)) {
                                _http_errno = (HttpParserError.INVALID_VERSION);
                                goto error;
                            }

                            break;
                        }

                    /* end of request line */
                    case ParserState.req_line_almost_done: {
                            if ((ch != LF)) {
                                _http_errno = (HttpParserError.LF_EXPECTED);
                                goto error;
                            }

                            _state = (ParserState.header_field_start);
                            break;
                        }

                    case ParserState.header_field_start: {
                            if (ch == CR) {
                                _state = (ParserState.headers_almost_done);
                                break;
                            }

                            if (ch == LF) {
                                /* they might be just sending \n instead of \r\n so this would be
                                 * the second \n to denote the end of headers*/
                                _state = (ParserState.headers_almost_done);
                                goto reexecute;
                            }

                            c = TOKEN(ch);

                            if ((c == 0)) {
                                _http_errno = (HttpParserError.INVALID_HEADER_TOKEN);
                                goto error;
                            }

                            if (header_field_mark == -1) header_field_mark = p;

                            _index = 0;
                            _state = (ParserState.header_field);

                            switch ((char)c) {
                                case 'c':
                                    _header_state = HeaderState.C;
                                    break;

                                case 'p':
                                    _header_state = HeaderState.matching_proxy_connection;
                                    break;

                                case 't':
                                    _header_state = HeaderState.matching_transfer_encoding;
                                    break;

                                case 'u':
                                    _header_state = HeaderState.matching_upgrade;
                                    break;

                                default:
                                    _header_state = HeaderState.general;
                                    break;
                            }
                            break;
                        }

                    case ParserState.header_field: {
                            int start = p;
                            for (; p != data + len; p++) {
                                ch = buffer[p];
                                c = TOKEN(ch);

                                if (c == 0)
                                    break;

                                switch (_header_state) {
                                    case HeaderState.general:
                                        break;

                                    case HeaderState.C:
                                        _index++;
                                        _header_state = (c == 'o' ? HeaderState.CO : HeaderState.general);
                                        break;

                                    case HeaderState.CO:
                                        _index++;
                                        _header_state = (c == 'n' ? HeaderState.CON : HeaderState.general);
                                        break;

                                    case HeaderState.CON:
                                        _index++;
                                        switch ((char)c) {
                                            case 'n':
                                                _header_state = HeaderState.matching_connection;
                                                break;
                                            case 't':
                                                _header_state = HeaderState.matching_content_length;
                                                break;
                                            default:
                                                _header_state = HeaderState.general;
                                                break;
                                        }
                                        break;

                                    /* connection */

                                    case HeaderState.matching_connection:
                                        _index++;
                                        if (_index > CONNECTION.Length || c != CONNECTION[_index]) {
                                            _header_state = HeaderState.general;
                                        }
                                        else if (_index == CONNECTION.Length - 1) {
                                            _header_state = HeaderState.connection;
                                        }
                                        break;

                                    /* proxy-connection */

                                    case HeaderState.matching_proxy_connection:
                                        _index++;
                                        if (_index > PROXY_CONNECTION.Length || c != PROXY_CONNECTION[_index]) {
                                            _header_state = HeaderState.general;
                                        }
                                        else if (_index == PROXY_CONNECTION.Length - 1) {
                                            _header_state = HeaderState.connection;
                                        }
                                        break;

                                    /* content-length */

                                    case HeaderState.matching_content_length:
                                        _index++;
                                        if (_index > CONTENT_LENGTH.Length || c != CONTENT_LENGTH[_index]) {
                                            _header_state = HeaderState.general;
                                        }
                                        else if (_index == CONTENT_LENGTH.Length - 1) {
                                            _header_state = HeaderState.content_length;
                                        }
                                        break;

                                    /* transfer-encoding */

                                    case HeaderState.matching_transfer_encoding:
                                        _index++;
                                        if (_index > TRANSFER_ENCODING.Length || c != TRANSFER_ENCODING[_index]) {
                                            _header_state = HeaderState.general;
                                        }
                                        else if (_index == TRANSFER_ENCODING.Length - 1) {
                                            _header_state = HeaderState.transfer_encoding;
                                        }
                                        break;

                                    /* upgrade */

                                    case HeaderState.matching_upgrade:
                                        _index++;
                                        if (_index > UPGRADE.Length || c != UPGRADE[_index]) {
                                            _header_state = HeaderState.general;
                                        }
                                        else if (_index == UPGRADE.Length - 1) {
                                            _header_state = HeaderState.upgrade;
                                        }
                                        break;

                                    case HeaderState.connection:
                                    case HeaderState.content_length:
                                    case HeaderState.transfer_encoding:
                                    case HeaderState.upgrade:
                                        if (ch != ' ') _header_state = HeaderState.general;
                                        break;

                                    default:
                                        assert("Unknown header_state");
                                        break;
                                }
                            }

                            _nread += (uint)(p - start);
                            if (_nread > MAX_HEADER_SIZE) {
                                _http_errno = HttpParserError.HEADER_OVERFLOW;
                                goto error;
                            }

                            if (p == data + len) {
                                --p;
                                break;
                            }

                            if (ch == ':') {
                                _state = (ParserState.header_value_discard_ws);
                                if (CALLBACK_DATA(on_header_field, p, ref header_field_mark, buffer) != 0) {
                                    _http_errno = HttpParserError.CB_header_field;
                                    p++;
                                    goto error;
                                }
                                break;
                            }

                            _http_errno = (HttpParserError.INVALID_HEADER_TOKEN);
                            goto error;
                        }

                    case ParserState.header_value_discard_ws:
                        if (ch == ' ' || ch == '\t') break;

                        if (ch == CR) {
                            _state = (ParserState.header_value_discard_ws_almost_done);
                            break;
                        }

                        if (ch == LF) {
                            _state = (ParserState.header_value_discard_lws);
                            break;
                        }

                        goto case ParserState.header_value_start;
                    /* FALLTHROUGH */

                    case ParserState.header_value_start: {
                            if (header_value_mark == -1) header_value_mark = p;

                            _state = (ParserState.header_value);
                            _index = 0;

                            c = LOWER(ch);

                            switch (_header_state) {
                                case HeaderState.upgrade:
                                    _flags |= HttpFlags.UPGRADE;
                                    _header_state = HeaderState.general;
                                    break;

                                case HeaderState.transfer_encoding:
                                    /* looking for 'Transfer-Encoding: chunked' */
                                    if ('c' == c) {
                                        _header_state = HeaderState.matching_transfer_encoding_chunked;
                                    }
                                    else {
                                        _header_state = HeaderState.general;
                                    }
                                    break;

                                case HeaderState.content_length:
                                    if ((!IS_NUM(ch))) {
                                        _http_errno = (HttpParserError.INVALID_CONTENT_LENGTH);
                                        goto error;
                                    }

                                    if ((_flags & HttpFlags.CONTENTLENGTH) != 0) {
                                        _http_errno = (HttpParserError.UNEXPECTED_CONTENT_LENGTH);
                                        goto error;
                                    }

                                    _flags |= HttpFlags.CONTENTLENGTH;
                                    _content_length = (ulong)(ch - '0');
                                    break;

                                case HeaderState.connection:
                                    /* looking for 'Connection: keep-alive' */
                                    if (c == 'k') {
                                        _header_state = HeaderState.matching_connection_keep_alive;
                                        /* looking for 'Connection: close' */
                                    }
                                    else if (c == 'c') {
                                        _header_state = HeaderState.matching_connection_close;
                                    }
                                    else if (c == 'u') {
                                        _header_state = HeaderState.matching_connection_upgrade;
                                    }
                                    else {
                                        _header_state = HeaderState.matching_connection_token;
                                    }
                                    break;

                                /* Multi-value `Connection` header */
                                case HeaderState.matching_connection_token_start:
                                    break;

                                default:
                                    _header_state = HeaderState.general;
                                    break;
                            }
                            break;
                        }

                    case ParserState.header_value: {
                            int start = p;
                            HeaderState h_state = _header_state;
                            for (; p != data + len; p++) {
                                ch = buffer[p];
                                if (ch == CR) {
                                    _state = (ParserState.header_almost_done);
                                    _header_state = h_state;
                                    if (CALLBACK_DATA(on_header_value, p, ref header_value_mark, buffer) != 0) {
                                        _http_errno = HttpParserError.CB_header_value;
                                        p++;
                                        goto error;
                                    }
                                    break;
                                }

                                if (ch == LF) {
                                    _state = (ParserState.header_almost_done);
                                    _nread += (uint)(p - start);
                                    if (_nread > MAX_HEADER_SIZE) {
                                        _http_errno = HttpParserError.HEADER_OVERFLOW;
                                        goto error;
                                    }

                                    _header_state = h_state;
                                    if (CALLBACK_DATA(on_header_value, p, ref header_value_mark, buffer) != 0) {
                                        _http_errno = HttpParserError.CB_header_value;
                                        goto error;
                                    }
                                    goto reexecute;
                                }

                                if (!_lenient_http_headers && !IS_HEADER_CHAR(ch)) {
                                    _http_errno = (HttpParserError.INVALID_HEADER_TOKEN);
                                    goto error;
                                }

                                c = LOWER(ch);

                                switch (h_state) {
                                    case HeaderState.general: {
                                            int p_cr;
                                            int p_lf;
                                            int limit = data + len - p;

                                            limit = Math.Min(limit, MAX_HEADER_SIZE);

                                            p_cr = memchr(p, CR, limit, buffer);
                                            p_lf = memchr(p, LF, limit, buffer);
                                            if (p_cr != -1) {
                                                if (p_lf != -1 && p_cr >= p_lf)
                                                    p = p_lf;
                                                else
                                                    p = p_cr;
                                            }
                                            else if ((p_lf != -1)) {
                                                p = p_lf;
                                            }
                                            else {
                                                p = data + len;
                                            }
                                            --p;

                                            break;
                                        }

                                    case HeaderState.connection:
                                    case HeaderState.transfer_encoding:
                                        assert("Shouldn't get here.");
                                        break;

                                    case HeaderState.content_length: {
                                            ulong t;

                                            if (ch == ' ') break;

                                            if ((!IS_NUM(ch))) {
                                                _header_state = h_state;
                                                _http_errno = (HttpParserError.INVALID_CONTENT_LENGTH);
                                                goto error;
                                            }

                                            t = _content_length;
                                            t *= 10;
                                            t += (ulong)(ch - '0');

                                            /* Overflow? Test against a conservative limit for simplicity. */
                                            if (((ulong.MaxValue - 10) / 10 < _content_length)) {
                                                _header_state = h_state;
                                                _http_errno = (HttpParserError.INVALID_CONTENT_LENGTH);
                                                goto error;
                                            }

                                            _content_length = t;
                                            break;
                                        }

                                    /* Transfer-Encoding: chunked */
                                    case HeaderState.matching_transfer_encoding_chunked:
                                        _index++;
                                        if (_index > CHUNKED.Length || c != CHUNKED[_index]) {
                                            h_state = HeaderState.general;
                                        }
                                        else if (_index == CHUNKED.Length - 1) {
                                            h_state = HeaderState.transfer_encoding_chunked;
                                        }
                                        break;

                                    case HeaderState.matching_connection_token_start:
                                        /* looking for 'Connection: keep-alive' */
                                        if (c == 'k') {
                                            h_state = HeaderState.matching_connection_keep_alive;
                                            /* looking for 'Connection: close' */
                                        }
                                        else if (c == 'c') {
                                            h_state = HeaderState.matching_connection_close;
                                        }
                                        else if (c == 'u') {
                                            h_state = HeaderState.matching_connection_upgrade;
                                        }
                                        else if (STRICT_TOKEN(c) != 0) {
                                            h_state = HeaderState.matching_connection_token;
                                        }
                                        else if (c == ' ' || c == '\t') {
                                            /* Skip lws */
                                        }
                                        else {
                                            h_state = HeaderState.general;
                                        }
                                        break;

                                    /* looking for 'Connection: keep-alive' */
                                    case HeaderState.matching_connection_keep_alive:
                                        _index++;
                                        if (_index > KEEP_ALIVE.Length || c != KEEP_ALIVE[_index]) {
                                            h_state = HeaderState.matching_connection_token;
                                        }
                                        else if (_index == KEEP_ALIVE.Length - 1) {
                                            h_state = HeaderState.connection_keep_alive;
                                        }
                                        break;

                                    /* looking for 'Connection: close' */
                                    case HeaderState.matching_connection_close:
                                        _index++;
                                        if (_index > CLOSE.Length || c != CLOSE[_index]) {
                                            h_state = HeaderState.matching_connection_token;
                                        }
                                        else if (_index == CLOSE.Length - 1) {
                                            h_state = HeaderState.connection_close;
                                        }
                                        break;

                                    /* looking for 'Connection: upgrade' */
                                    case HeaderState.matching_connection_upgrade:
                                        _index++;
                                        if (_index > UPGRADE.Length || c != UPGRADE[_index]) {
                                            h_state = HeaderState.matching_connection_token;
                                        }
                                        else if (_index == UPGRADE.Length - 1) {
                                            h_state = HeaderState.connection_upgrade;
                                        }
                                        break;

                                    case HeaderState.matching_connection_token:
                                        if (ch == ',') {
                                            h_state = HeaderState.matching_connection_token_start;
                                            _index = 0;
                                        }
                                        break;

                                    case HeaderState.transfer_encoding_chunked:
                                        if (ch != ' ') h_state = HeaderState.general;
                                        break;

                                    case HeaderState.connection_keep_alive:
                                    case HeaderState.connection_close:
                                    case HeaderState.connection_upgrade:
                                        if (ch == ',') {
                                            if (h_state == HeaderState.connection_keep_alive) {
                                                _flags |= HttpFlags.CONNECTION_KEEP_ALIVE;
                                            }
                                            else if (h_state == HeaderState.connection_close) {
                                                _flags |= HttpFlags.CONNECTION_CLOSE;
                                            }
                                            else if (h_state == HeaderState.connection_upgrade) {
                                                _flags |= HttpFlags.CONNECTION_UPGRADE;
                                            }
                                            h_state = HeaderState.matching_connection_token_start;
                                            _index = 0;
                                        }
                                        else if (ch != ' ') {
                                            h_state = HeaderState.matching_connection_token;
                                        }
                                        break;

                                    default:
                                        _state = (ParserState.header_value);
                                        h_state = HeaderState.general;
                                        break;
                                }
                            }
                            _header_state = h_state;

                            _nread += (uint)(p - start);
                            if (_nread > MAX_HEADER_SIZE) {
                                _http_errno = HttpParserError.HEADER_OVERFLOW;
                                goto error;
                            }

                            if (p == data + len)
                                --p;
                            break;
                        }

                    case ParserState.header_almost_done: {
                            if ((ch != LF)) {
                                _http_errno = (HttpParserError.LF_EXPECTED);
                                goto error;
                            }

                            _state = (ParserState.header_value_lws);
                            break;
                        }

                    case ParserState.header_value_lws: {
                            if (ch == ' ' || ch == '\t') {
                                _state = (ParserState.header_value_start);
                                goto reexecute;
                            }

                            /* finished the header */
                            switch (_header_state) {
                                case HeaderState.connection_keep_alive:
                                    _flags |= HttpFlags.CONNECTION_KEEP_ALIVE;
                                    break;
                                case HeaderState.connection_close:
                                    _flags |= HttpFlags.CONNECTION_CLOSE;
                                    break;
                                case HeaderState.transfer_encoding_chunked:
                                    _flags |= HttpFlags.CHUNKED;
                                    break;
                                case HeaderState.connection_upgrade:
                                    _flags |= HttpFlags.CONNECTION_UPGRADE;
                                    break;
                                default:
                                    break;
                            }

                            _state = (ParserState.header_field_start);
                            goto reexecute;
                        }

                    case ParserState.header_value_discard_ws_almost_done: {
#if HTTP_PARSER_STRICT
                            if (ch != LF) { _http_errno = HttpParserError.STRICT; goto error; };
#endif
                            _state = (ParserState.header_value_discard_lws);
                            break;
                        }

                    case ParserState.header_value_discard_lws: {
                            if (ch == ' ' || ch == '\t') {
                                _state = (ParserState.header_value_discard_ws);
                                break;
                            }
                            else {
                                switch (_header_state) {
                                    case HeaderState.connection_keep_alive:
                                        _flags |= HttpFlags.CONNECTION_KEEP_ALIVE;
                                        break;
                                    case HeaderState.connection_close:
                                        _flags |= HttpFlags.CONNECTION_CLOSE;
                                        break;
                                    case HeaderState.connection_upgrade:
                                        _flags |= HttpFlags.CONNECTION_UPGRADE;
                                        break;
                                    case HeaderState.transfer_encoding_chunked:
                                        _flags |= HttpFlags.CHUNKED;
                                        break;
                                    default:
                                        break;
                                }

                                /* header value was empty */
                                if (header_value_mark == -1) header_value_mark = p;
                                _state = (ParserState.header_field_start);
                                if (CALLBACK_DATA(on_header_value, p, ref header_value_mark, buffer) != 0) {
                                    _http_errno = HttpParserError.CB_header_value;
                                    goto error;
                                }
                                goto reexecute;
                            }
                        }

                    case ParserState.headers_almost_done: {
#if HTTP_PARSER_STRICT
                            if (ch != LF) { _http_errno = HttpParserError.STRICT; goto error; };
#endif

                            if ((_flags & HttpFlags.TRAILING) != 0) {
                                /* End of a chunked request */
                                _state = (ParserState.message_done);
                                if (CALLBACK_NOTIFY(on_chunk_complete) != 0) {
                                    _http_errno = HttpParserError.CB_message_begin;
                                    goto error;
                                }
                                goto reexecute;
                            }

                            /* Cannot use chunked encoding and a content-length header together
                               per the HTTP specification. */
                            if (((_flags & HttpFlags.CHUNKED) != 0) &&
                                (_flags & HttpFlags.CONTENTLENGTH) != 0) {
                                _http_errno = (HttpParserError.UNEXPECTED_CONTENT_LENGTH);
                                goto error;
                            }

                            _state = (ParserState.headers_done);

                            /* Set this here so that on_headers_complete() callbacks can see it */
                            _upgrade =
                                      ((_flags & (HttpFlags.UPGRADE | HttpFlags.CONNECTION_UPGRADE)) ==
                                       (HttpFlags.UPGRADE | HttpFlags.CONNECTION_UPGRADE) ||
                                       _method == HttpMethod.CONNECT);

                            /* Here we call the headers_complete callback. This is somewhat
                             * different than other callbacks because if the user returns 1, we
                             * will interpret that as saying that this message has no body. This
                             * is needed for the annoying case of recieving a response to a HEAD
                             * request.
                             *
                             * We'd like to use CALLBACK_NOTIFY_NOADVANCE() here but we cannot, so
                             * we have to simulate it by handling a change in errno below.
                             */
                            switch (CALLBACK_NOTIFY(on_headers_complete)) {
                                case 0:
                                    break;

                                case 1:
                                    _flags |= HttpFlags.SKIPBODY;
                                    break;

                                case 2:
                                    _upgrade = true;
                                    _flags |= HttpFlags.SKIPBODY;
                                    break;

                                default:
                                    _http_errno = (HttpParserError.CB_headers_complete);
                                    goto error;
                            }
                            goto reexecute;
                        }

                    case ParserState.headers_done: {
                            bool hasBody;
#if HTTP_PARSER_STRICT
                            if (ch != LF) { _http_errno = HttpParserError.STRICT; goto error; };
#endif

                            _nread = 0;

                            hasBody = (_flags & HttpFlags.CHUNKED) != 0 ||
                              (_content_length > 0 && _content_length != ulong.MaxValue);
                            if (_upgrade && (_method == HttpMethod.CONNECT ||
                                                    (_flags & HttpFlags.SKIPBODY) != 0 || !hasBody)) {
                                /* Exit, the rest of the message is in a different protocol. */
                                _state = (NEW_MESSAGE());
                                if (CALLBACK_NOTIFY(on_message_complete) != 0) {
                                    _http_errno = HttpParserError.CB_message_complete;
                                    p++;
                                    goto error;
                                }
                                return (int)((p - data) + 1);
                            }

                            if ((_flags & HttpFlags.SKIPBODY) != 0) {
                                _state = (NEW_MESSAGE());
                                if (CALLBACK_NOTIFY(on_message_complete) != 0) {
                                    _http_errno = HttpParserError.CB_message_complete;
                                    p++;
                                    goto error;
                                }
                            }
                            else if ((_flags & HttpFlags.CHUNKED) != 0) {
                                /* chunked encoding - ignore Content-Length header */
                                _state = (ParserState.chunk_size_start);
                            }
                            else {
                                if (_content_length == 0) {
                                    /* Content-Length header given but zero: Content-Length: 0\r\n */
                                    _state = (NEW_MESSAGE());
                                    if (CALLBACK_NOTIFY(on_message_complete) != 0) {
                                        _http_errno = HttpParserError.CB_message_complete;
                                        p++;
                                        goto error;
                                    }
                                }
                                else if (_content_length != ulong.MaxValue) {
                                    /* Content-Length header given and non-zero */
                                    _state = (ParserState.body_identity);
                                }
                                else {
                                    if (!http_message_needs_eof()) {
                                        /* Assume content-length 0 - read the next */
                                        _state = (NEW_MESSAGE());
                                        if (CALLBACK_NOTIFY(on_message_complete) != 0) {
                                            _http_errno = HttpParserError.CB_message_complete;
                                            p++;
                                            goto error;
                                        }
                                    }
                                    else {
                                        /* Read body until EOF */
                                        _state = (ParserState.body_identity_eof);
                                    }
                                }
                            }

                            break;
                        }

                    case ParserState.body_identity: {
                            ulong to_read = Math.Min(_content_length, (ulong)((data + len) - p));

                            assert(_content_length != 0
                                && _content_length != ulong.MaxValue);

                            /* The difference between advancing content_length and p is because
                             * the latter will automaticaly advance on the next loop iteration.
                             * Further, if content_length ends up at 0, we want to see the last
                             * byte again for our message complete callback.
                             */
                            if (body_mark == -1) body_mark = p;
                            _content_length -= to_read;
                            p += (int)(to_read - 1);

                            if (_content_length == 0) {
                                _state = (ParserState.message_done);
                                /* Mimic CALLBACK_DATA_NOADVANCE() but with one extra byte.
                                 *
                                 * The alternative to doing this is to wait for the next byte to
                                 * trigger the data callback, just as in every other case. The
                                 * problem with this is that this makes it difficult for the test
                                 * harness to distinguish between complete-on-EOF and
                                 * complete-on-length. It's not clear that this distinction is
                                 * important for applications, but let's keep it for now.
                                 */
                                if (CALLBACK_DATA(on_body, p + 1, ref body_mark, buffer) != 0) {
                                    _http_errno = HttpParserError.CB_body;
                                    goto error;
                                }
                                goto reexecute;
                            }

                            break;
                        }

                    /* read until EOF */
                    case ParserState.body_identity_eof:
                        if (body_mark == -1) body_mark = p;
                        p = data + len - 1;

                        break;

                    case ParserState.message_done:
                        _state = (NEW_MESSAGE());
                        if (CALLBACK_NOTIFY(on_message_complete) != 0) {
                            _http_errno = HttpParserError.CB_message_complete;
                            p++;
                            goto error;
                        }
                        if (_upgrade) {
                            /* Exit, the rest of the message is in a different protocol. */
                            return (int)((p - data) + 1);
                        }
                        break;

                    case ParserState.chunk_size_start: {

                            assert(_nread == 1);
                            assert((_flags & HttpFlags.CHUNKED) != 0);

                            unhex_val = unhex[(byte)ch];
                            if ((unhex_val == -1)) {
                                _http_errno = (HttpParserError.INVALID_CHUNK_SIZE);
                                goto error;
                            }

                            _content_length = (ulong)unhex_val;
                            _state = (ParserState.chunk_size);
                            break;
                        }

                    case ParserState.chunk_size: {
                            ulong t;

                            assert((_flags & HttpFlags.CHUNKED) != 0);

                            if (ch == CR) {
                                _state = (ParserState.chunk_size_almost_done);
                                break;
                            }

                            unhex_val = unhex[(byte)ch];

                            if (unhex_val == -1) {
                                if (ch == ';' || ch == ' ') {
                                    _state = (ParserState.chunk_parameters);
                                    break;
                                }

                                _http_errno = (HttpParserError.INVALID_CHUNK_SIZE);
                                goto error;
                            }

                            t = _content_length;
                            t *= 16;
                            t += (ulong)unhex_val;

                            /* Overflow? Test against a conservative limit for simplicity. */
                            if (((ulong.MaxValue - 16) / 16 < _content_length)) {
                                _http_errno = (HttpParserError.INVALID_CONTENT_LENGTH);
                                goto error;
                            }

                            _content_length = t;
                            break;
                        }

                    case ParserState.chunk_parameters: {
                            assert((_flags & HttpFlags.CHUNKED) != 0);
                            /* just ignore this shit. TODO check for overflow */
                            if (ch == CR) {
                                _state = (ParserState.chunk_size_almost_done);
                                break;
                            }
                            break;
                        }

                    case ParserState.chunk_size_almost_done: {
                            assert((_flags & HttpFlags.CHUNKED) != 0);
#if HTTP_PARSER_STRICT
                            if (ch != LF) { _http_errno = HttpParserError.STRICT; goto error; };
#endif

                            _nread = 0;

                            if (_content_length == 0) {
                                _flags |= HttpFlags.TRAILING;
                                _state = (ParserState.header_field_start);
                            }
                            else {
                                _state = (ParserState.chunk_data);
                            }
                            if (CALLBACK_NOTIFY(on_chunk_header) != 0) {
                                _http_errno = HttpParserError.CB_chunk_header;
                                p++;
                                goto error;
                            }
                            break;
                        }

                    case ParserState.chunk_data: {
                            ulong to_read = Math.Min(_content_length, (ulong)((data + len) - p));

                            assert((_flags & HttpFlags.CHUNKED) != 0);
                            assert(_content_length != 0
                                && _content_length != ulong.MaxValue);

                            /* See the explanation in ParserState.body_identity for why the content
                             * length and data pointers are managed this way.
                             */
                            if (body_mark == -1) body_mark = p;
                            _content_length -= to_read;
                            p += (int)(to_read - 1);

                            if (_content_length == 0) {
                                _state = (ParserState.chunk_data_almost_done);
                            }

                            break;
                        }

                    case ParserState.chunk_data_almost_done:
                        assert((_flags & HttpFlags.CHUNKED) != 0);
                        assert(_content_length == 0);
#if HTTP_PARSER_STRICT
                        if (ch != CR) { _http_errno = HttpParserError.STRICT; goto error; };
#endif
                        _state = (ParserState.chunk_data_done);
                        if (CALLBACK_DATA(on_body, p, ref body_mark, buffer) != 0) {
                            _http_errno = HttpParserError.CB_body;
                            p++;
                            goto error;
                        }
                        break;

                    case ParserState.chunk_data_done:
                        assert((_flags & HttpFlags.CHUNKED) != 0);
#if HTTP_PARSER_STRICT
                        if (ch != LF) { _http_errno = HttpParserError.STRICT; goto error; };
#endif
                        _nread = 0;
                        _state = (ParserState.chunk_size_start);
                        if (CALLBACK_NOTIFY(on_chunk_complete) != 0) {
                            _http_errno = HttpParserError.CB_chunk_complete;
                            p++;
                            goto error;
                        }
                        break;

                    default:
                        assert("unhandled state");
                        _http_errno = (HttpParserError.INVALID_INTERNAL_STATE);
                        goto error;
                }
            }

            /* Run callbacks for any marks that we have leftover after we ran our of
             * bytes. There should be at most one of these set, so it's OK to invoke
             * them in series (unset marks will not result in callbacks).
             *
             * We use the NOADVANCE() variety of callbacks here because 'p' has already
             * overflowed 'data' and this allows us to correct for the off-by-one that
             * we'd otherwise have (since CALLBACK_DATA() is meant to be run with a 'p'
             * value that's in-bounds).
             */

            assert(((header_field_mark != -1 ? 1 : 0) +
                    (header_value_mark != -1 ? 1 : 0) +
                    (url_mark != -1 ? 1 : 0) +
                    (body_mark != -1 ? 1 : 0) +
                    (status_mark != -1 ? 1 : 0)) <= 1);

            _incomplete = true;
            if (CALLBACK_DATA(on_header_field, p, ref header_field_mark, buffer) != 0) {
                _http_errno = HttpParserError.CB_header_field;
                goto error;
            }
            if (CALLBACK_DATA(on_header_value, p, ref header_value_mark, buffer) != 0) {
                _http_errno = HttpParserError.CB_header_value;
                goto error;
            }
            if (CALLBACK_DATA(on_url, p, ref url_mark, buffer) != 0) {
                _http_errno = HttpParserError.CB_url;
                goto error;
            }
            if (CALLBACK_DATA(on_body, p, ref body_mark, buffer) != 0) {
                _http_errno = HttpParserError.CB_body;
                goto error;
            }
            if (CALLBACK_DATA(on_status, p, ref status_mark, buffer) != 0) {
                _http_errno = HttpParserError.CB_status;
                goto error;
            }
            return (int)(len);

            error:
            if (_http_errno == HttpParserError.OK) {
                _http_errno = (HttpParserError.UNKNOWN);
            }

            return (int)(p - data);
        }

        /* Our URL parser.
         *
         * This is designed to be shared by http_parser_execute() for URL validation,
         * hence it has a state transition + byte-for-byte interface. In addition, it
         * is meant to be embedded in http_parser_parse_url(), which does the dirty
         * work of turning state transitions URL components for its API.
         *
         * This function should only be invoked with non-space characters. It is
         * assumed that the caller cares about (and can detect) the transition between
         * URL and non-URL states by looking for these.
         */
        ParserState parse_url_char(ParserState s, byte ch) {
            if (ch == ' ' || ch == '\r' || ch == '\n') {
                return ParserState.dead;
            }

#if HTTP_PARSER_STRICT
            if (ch == '\t' || ch == '\f') {
                return ParserState.dead;
            }
#endif

            switch (s) {
                case ParserState.req_spaces_before_url:
                    /* Proxied requests are followed by scheme of an absolute URI (alpha).
                     * All methods except CONNECT are followed by '/' or '*'.
                     */

                    if (ch == '/' || ch == '*') {
                        return ParserState.req_path;
                    }

                    if (IS_ALPHA(ch)) {
                        return ParserState.req_schema;
                    }

                    break;

                case ParserState.req_schema:
                    if (IS_ALPHA(ch)) {
                        return s;
                    }

                    if (ch == ':') {
                        return ParserState.req_schema_slash;
                    }

                    break;

                case ParserState.req_schema_slash:
                    if (ch == '/') {
                        return ParserState.req_schema_slash_slash;
                    }

                    break;

                case ParserState.req_schema_slash_slash:
                    if (ch == '/') {
                        return ParserState.req_server_start;
                    }

                    break;

                case ParserState.req_server_with_at:
                    if (ch == '@') {
                        return ParserState.dead;
                    }
                    goto case ParserState.req_server_start;

                /* FALLTHROUGH */
                case ParserState.req_server_start:
                case ParserState.req_server:
                    if (ch == '/') {
                        return ParserState.req_path;
                    }

                    if (ch == '?') {
                        return ParserState.req_query_string_start;
                    }

                    if (ch == '@') {
                        return ParserState.req_server_with_at;
                    }

                    if (IS_USERINFO_CHAR(ch) || ch == '[' || ch == ']') {
                        return ParserState.req_server;
                    }

                    break;

                case ParserState.req_path:
                    if (IS_URL_CHAR(ch)) {
                        return s;
                    }

                    switch ((char)ch) {
                        case '?':
                            return ParserState.req_query_string_start;

                        case '#':
                            return ParserState.req_fragment_start;
                    }

                    break;

                case ParserState.req_query_string_start:
                case ParserState.req_query_string:
                    if (IS_URL_CHAR(ch)) {
                        return ParserState.req_query_string;
                    }

                    switch ((char)ch) {
                        case '?':
                            /* allow extra '?' in query string */
                            return ParserState.req_query_string;

                        case '#':
                            return ParserState.req_fragment_start;
                    }

                    break;

                case ParserState.req_fragment_start:
                    if (IS_URL_CHAR(ch)) {
                        return ParserState.req_fragment;
                    }

                    switch ((char)ch) {
                        case '?':
                            return ParserState.req_fragment;

                        case '#':
                            return s;
                    }

                    break;

                case ParserState.req_fragment:
                    if (IS_URL_CHAR(ch)) {
                        return s;
                    }

                    switch ((char)ch) {
                        case '?':
                        case '#':
                            return s;
                    }

                    break;

                default:
                    break;
            }

            /* We should never fall out of the switch above unless there's an error */
            return ParserState.dead;
        }



        private static bool IS_ALPHA(byte c) {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        private static bool IS_NUM(byte c) {
            return c >= '0' && c <= '9';
        }

        private static bool IS_ALPHANUM(byte c) {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        private static bool BIT_AT(byte[] a, byte c) {
            return 0 != (a[c >> 3] & (1 << (c & 7)));
        }

        private static byte LOWER(byte c) {
            return (byte)(c | 0x20);
        }

        private static bool IS_HEADER_CHAR(byte ch) {
            return (ch == CR || ch == LF || ch == 9 || (ch > 31 && ch != 127));
        }

        private static bool IS_MARK(byte c) {
            return ((c) == '-' || (c) == '_' || (c) == '.' ||
                    (c) == '!' || (c) == '~' || (c) == '*' || (c) == '\'' || (c) == '(' ||
                    (c) == ')');
        }


        private static bool IS_USERINFO_CHAR(byte c) {
            return (IS_ALPHANUM(c) || IS_MARK(c) || (c) == '%' ||
                  (c) == ';' || (c) == ':' || (c) == '&' || (c) == '=' || (c) == '+' ||
                  (c) == '$' || (c) == ',');
        }


        private byte STRICT_TOKEN(byte c) {
            return (byte)tokens[c];
        }

#if HTTP_PARSER_STRICT
        private static byte TOKEN(byte c) {
            return (byte)tokens[c];
        }

        private static bool IS_URL_CHAR(byte c) {
            return 0 != (normal_url_char[c >> 3] & (1 << (c & 7)));
        }

        private static bool IS_HOST_CHAR(byte c) {
            return IS_ALPHANUM(c) || c == '.' || c == '-';
        }

        private ParserState NEW_MESSAGE() {
            if (ShouldKeepAlive())
                return _type == HttpParserType.REQUEST ? ParserState.start_req : ParserState.start_res;
            else
                return ParserState.dead;
        }

#else
        private static byte TOKEN(byte c) {
            return ((c == ' ') ? (byte)' ' : (byte)tokens[c]);
        }

        private static bool IS_URL_CHAR(byte c) {
            return (normal_url_char[c >> 3] & (1 << (c & 7))) != 0 || (c & 0x80) != 0;
        }

        private static bool IS_HOST_CHAR(byte c) {
            return IS_ALPHANUM(c) || c == '.' || c == '-' || c == '_';
        }

        private ParserState NEW_MESSAGE() {
            return _type == HttpParserType.REQUEST ? ParserState.start_req : ParserState.start_res;
        }
#endif

        private int CALLBACK_NOTIFY(Callback cb) {
            if (cb != null) {
                var result = cb();
                if (result != 0) {
                    return result;
                }
            }
            return 0;
        }

        private int CALLBACK_DATA(DataCallback cb, int p, ref int mark, byte[] arr) {
            if (mark != -1) {
                if (cb != null) {
                    var result = cb(new ArraySegment<byte>(arr, mark, p - mark));
                    if (result != 0) {
                        return result;
                    }
                }
                mark = -1;
            }
            return 0;
        }

        private void assert(bool value) {
#if DEBUG
            if (!value) {
                throw new Exception("Assert Failed");
            }
#endif
        }

        private static void assert(int value) {
#if DEBUG
            if (value != 0) {
                throw new Exception("Assert Failed");
            }
#endif
        }

        private static void assert(string value) {
#if DEBUG
            throw new Exception("Assert Failed: " + value);
#endif
        }

        private static int memchr(int p, byte ch, int limit, byte[] buff) {
            int end = p + limit;
            for (;p < end; p++) {
                if (buff[p] == ch)
                    return p;
            }
            return -1;
        }

        private bool http_message_needs_eof() {
            if (_type == HttpParserType.REQUEST) {
                return false;
            }

            /* See RFC 2616 section 4.4 */
            if (_status_code / 100 == 1 || // 1xx e.g. Continue
                _status_code == 204 ||     // No Content
                _status_code == 304 ||     // Not Modified
                (_flags & HttpFlags.SKIPBODY) != 0) {     // response to a HEAD request
                return false;
            }

            if ((_flags & HttpFlags.CHUNKED) != 0 || _content_length != ulong.MaxValue) {
                return false;
            }

            return true;
        }


        #region Static Initializer
        static HttpParser() {
            const char o = (char)0;
#if HTTP_PARSER_STRICT
            const byte T2 = 0, T5 = 0;
#endif
            normal_url_char = new byte[32] {
            /*   0 nul    1 soh    2 stx    3 etx    4 eot    5 enq    6 ack    7 bel  */
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
            /*   8 bs     9 ht    10 nl    11 vt    12 np    13 cr    14 so    15 si   */
                    0    |   T2   |   0    |   0    |   T5   |   0    |   0    |   0,
            /*  16 dle   17 dc1   18 dc2   19 dc3   20 dc4   21 nak   22 syn   23 etb */
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
            /*  24 can   25 em    26 sub   27 esc   28 fs    29 gs    30 rs    31 us  */
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
            /*  32 sp    33  !    34  "    35  #    36  $    37  %    38  &    39  '  */
                    0    |   2    |   4    |   0    |   16   |   32   |   64   |  128,
            /*  40  (    41  )    42  *    43  +    44  ,    45  -    46  .    47  /  */
                    1    |   2    |   4    |   8    |   16   |   32   |   64   |  128,
            /*  48  0    49  1    50  2    51  3    52  4    53  5    54  6    55  7  */
                    1    |   2    |   4    |   8    |   16   |   32   |   64   |  128,
            /*  56  8    57  9    58  :    59  ;    60  <    61  =    62  >    63  ?  */
                    1    |   2    |   4    |   8    |   16   |   32   |   64   |   0,
            /*  64  @    65  A    66  B    67  C    68  D    69  E    70  F    71  G  */
                    1    |   2    |   4    |   8    |   16   |   32   |   64   |  128,
            /*  72  H    73  I    74  J    75  K    76  L    77  M    78  N    79  O  */
                    1    |   2    |   4    |   8    |   16   |   32   |   64   |  128,
            /*  80  P    81  Q    82  R    83  S    84  T    85  U    86  V    87  W  */
                    1    |   2    |   4    |   8    |   16   |   32   |   64   |  128,
            /*  88  X    89  Y    90  Z    91  [    92  \    93  ]    94  ^    95  _  */
                    1    |   2    |   4    |   8    |   16   |   32   |   64   |  128,
            /*  96  `    97  a    98  b    99  c   100  d   101  e   102  f   103  g  */
                    1    |   2    |   4    |   8    |   16   |   32   |   64   |  128,
            /* 104  h   105  i   106  j   107  k   108  l   109  m   110  n   111  o  */
                    1    |   2    |   4    |   8    |   16   |   32   |   64   |  128,
            /* 112  p   113  q   114  r   115  s   116  t   117  u   118  v   119  w  */
                    1    |   2    |   4    |   8    |   16   |   32   |   64   |  128,
            /* 120  x   121  y   122  z   123  {   124  |   125  }   126  ~   127 del */
                    1    |   2    |   4    |   8    |   16   |   32   |   64   |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0,
                    0    |   0    |   0    |   0    |   0    |   0    |   0    |   0};

            /* Tokens as defined by rfc 2616. Also lowercases them.
            *        token       = 1*<any CHAR except CTLs or separators>
            *     separators     = "(" | ")" | "<" | ">" | "@"
            *                    | "," | ";" | ":" | "\" | <">
            *                    | "/" | "[" | "]" | "?" | "="
            *                    | "{" | "}" | SP | HT
            */
            tokens = new char[256] {
            /*   0 nul    1 soh    2 stx    3 etx    4 eot    5 enq    6 ack    7 bel  */
                    o,       o,       o,       o,       o,       o,       o,       o,
            /*   8 bs     9 ht    10 nl    11 vt    12 np    13 cr    14 so    15 si   */
                    o,       o,       o,       o,       o,       o,       o,       o,
            /*  16 dle   17 dc1   18 dc2   19 dc3   20 dc4   21 nak   22 syn   23 etb */
                    o,       o,       o,       o,       o,       o,       o,       o,
            /*  24 can   25 em    26 sub   27 esc   28 fs    29 gs    30 rs    31 us  */
                    o,       o,       o,       o,       o,       o,       o,       o,
            /*  32 sp    33  !    34  "    35  #    36  $    37  %    38  &    39  '  */
                    o,      '!',      o,      '#',     '$',     '%',     '&',    '\'',
            /*  40  (    41  )    42  *    43  +    44  ,    45  -    46  .    47  /  */
                    o,       o,      '*',     '+',      o,      '-',     '.',      o,
            /*  48  0    49  1    50  2    51  3    52  4    53  5    54  6    55  7  */
                   '0',     '1',     '2',     '3',     '4',     '5',     '6',     '7',
            /*  56  8    57  9    58  :    59  ;    60  <    61  =    62  >    63  ?  */
                   '8',     '9',      o,       o,       o,       o,       o,       o,
            /*  64  @    65  A    66  B    67  C    68  D    69  E    70  F    71  G  */
                    o,      'a',     'b',     'c',     'd',     'e',     'f',     'g',
            /*  72  H    73  I    74  J    75  K    76  L    77  M    78  N    79  O  */
                   'h',     'i',     'j',     'k',     'l',     'm',     'n',     'o',
            /*  80  P    81  Q    82  R    83  S    84  T    85  U    86  V    87  W  */
                   'p',     'q',     'r',     's',     't',     'u',     'v',     'w',
            /*  88  X    89  Y    90  Z    91  [    92  \    93  ]    94  ^    95  _  */
                   'x',     'y',     'z',      o,       o,       o,      '^',     '_',
            /*  96  `    97  a    98  b    99  c   100  d   101  e   102  f   103  g  */
                   '`',     'a',     'b',     'c',     'd',     'e',     'f',     'g',
            /* 104  h   105  i   106  j   107  k   108  l   109  m   110  n   111  o  */
                   'h',     'i',     'j',     'k',     'l',     'm',     'n',     'o',
            /* 112  p   113  q   114  r   115  s   116  t   117  u   118  v   119  w  */
                   'p',     'q',     'r',     's',     't',     'u',     'v',     'w',
            /* 120  x   121  y   122  z   123  {   124  |   125  }   126  ~   127 del */
                   'x',     'y',     'z',      o,      '|',      o,      '~',       o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o,
                    o,       o,       o,       o,       o,       o,       o,        o};

            unhex = new sbyte[256] {
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
               0, 1, 2, 3, 4, 5, 6, 7, 8, 9,-1,-1,-1,-1,-1,-1,
              -1,10,11,12,13,14,15,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,10,11,12,13,14,15,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
               0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
               0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
               0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
               0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
               0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
               0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
               0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
               0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            };
        }
#endregion
    }


    /// <summary>
    /// Type of the parser
    /// </summary>
    public enum HttpParserType {
        REQUEST,
        RESPONSE,
        BOTH
    };

    /// <summary>
    /// Error code for http parser
    /// </summary>
    public enum HttpParserError {
        /* No error */
        OK,

        /* Callback-related errors */
        CB_message_begin,
        CB_url,
        CB_header_field,
        CB_header_value,
        CB_headers_complete,
        CB_body,
        CB_message_complete,
        CB_status,
        CB_chunk_header,
        CB_chunk_complete,

        /* Parsing-related errors */
        INVALID_EOF_STATE,
        HEADER_OVERFLOW,
        CLOSED_CONNECTION,
        INVALID_VERSION,
        INVALID_STATUS,
        INVALID_METHOD,
        INVALID_URL,
        INVALID_HOST,
        INVALID_PORT,
        INVALID_PATH,
        INVALID_QUERY_STRING,
        INVALID_FRAGMENT,
        LF_EXPECTED,
        INVALID_HEADER_TOKEN,
        INVALID_CONTENT_LENGTH,
        UNEXPECTED_CONTENT_LENGTH,
        INVALID_CHUNK_SIZE,
        INVALID_CONSTANT,
        INVALID_INTERNAL_STATE,
        STRICT,
        PAUSED,
        UNKNOWN,
    }

    static class HttptParserExt {
        public static string GetErrorDescription(this HttpParserError err) {
            switch (err) {
                case HttpParserError.OK: return "success";
                case HttpParserError.CB_message_begin: return "the on_message_begin callback failed";
                case HttpParserError.CB_url: return "the on_url callback failed";
                case HttpParserError.CB_header_field: return "the on_header_field callback failed";
                case HttpParserError.CB_header_value: return "the on_header_value callback failed";
                case HttpParserError.CB_headers_complete: return "the on_headers_complete callback failed";
                case HttpParserError.CB_body: return "the on_body callback failed";
                case HttpParserError.CB_message_complete: return "the on_message_complete callback failed";
                case HttpParserError.CB_status: return "the on_status callback failed";
                case HttpParserError.CB_chunk_header: return "the on_chunk_header callback failed";
                case HttpParserError.CB_chunk_complete: return "the on_chunk_complete callback failed";
                case HttpParserError.INVALID_EOF_STATE: return "stream ended at an unexpected time";
                case HttpParserError.HEADER_OVERFLOW: return "too many header bytes seen; overflow detected";
                case HttpParserError.CLOSED_CONNECTION: return "data received after completed connection: return close message";
                case HttpParserError.INVALID_VERSION: return "invalid HTTP version";
                case HttpParserError.INVALID_STATUS: return "invalid HTTP status code";
                case HttpParserError.INVALID_METHOD: return "invalid HTTP method";
                case HttpParserError.INVALID_URL: return "invalid URL";
                case HttpParserError.INVALID_HOST: return "invalid host";
                case HttpParserError.INVALID_PORT: return "invalid port";
                case HttpParserError.INVALID_PATH: return "invalid path";
                case HttpParserError.INVALID_QUERY_STRING: return "invalid query string";
                case HttpParserError.INVALID_FRAGMENT: return "invalid fragment";
                case HttpParserError.LF_EXPECTED: return "LF character expected";
                case HttpParserError.INVALID_HEADER_TOKEN: return "invalid character in header";
                case HttpParserError.INVALID_CONTENT_LENGTH: return "invalid character in content-length header";
                case HttpParserError.UNEXPECTED_CONTENT_LENGTH: return "unexpected content-length header";
                case HttpParserError.INVALID_CHUNK_SIZE: return "invalid character in chunk size header";
                case HttpParserError.INVALID_CONSTANT: return "invalid constant string";
                case HttpParserError.INVALID_INTERNAL_STATE: return "encountered unexpected internal state";
                case HttpParserError.STRICT: return "strict mode assertion failed";
                case HttpParserError.PAUSED: return "parser is paused";
                case HttpParserError.UNKNOWN: return "an unknown error occurred";
                default: return string.Empty;
            }
        }
    }
}
