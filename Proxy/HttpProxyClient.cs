//
//  Author:
//       Benton Stark <benton.stark@gmail.com>
//
//  Copyright (c) 2016 Benton Stark
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.ComponentModel;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProxyLib.Proxy.EventArgs;
using ProxyLib.Proxy.Exceptions;

namespace ProxyLib.Proxy
{
    /// <summary>
    /// HTTP connection proxy class.  This class implements the HTTP standard proxy protocol.
    /// <para>
    /// You can use this class to set up a connection to an HTTP proxy server.  Calling the 
    /// CreateConnection() method initiates the proxy connection and returns a standard
    /// System.Net.Socks.TcpClient object that can be used as normal.  The proxy plumbing
    /// is all handled for you.
    /// </para>
    /// <code>
    /// 
    /// </code>
    /// </summary>
    public class HttpProxyClient : IProxyClient
    {
        private string _proxyHost;
        private int _proxyPort;
        private string _proxyUsername;
        private string _proxyPassword;
        private HttpResponseCodes _respCode;
        private string _respText;
        private TcpClient _tcpClient;
        private TcpClient _tcpClientCached;

        private const int HTTP_PROXY_DEFAULT_PORT = 8080;
        private const string HTTP_PROXY_CONNECT_CMD = "CONNECT {0}:{1} HTTP/1.0\r\n{2}HOST: {0}:{1}\r\n\r\n";
        private const string HTTP_PROXY_AUTHENTICATE_CMD = "CONNECT {0}:{1} HTTP/1.0\r\n{3}HOST: {0}:{1}\r\nProxy-Authorization: Basic {2}\r\n\r\n";

        private const int WAIT_FOR_DATA_INTERVAL = 50; // 50 ms
        private const int WAIT_FOR_DATA_TIMEOUT = 15000; // 15 seconds
        private const string PROXY_NAME = "HTTP";

        private enum HttpResponseCodes
        {
            None = 0,
            Continue = 100,
            SwitchingProtocols = 101,
            OK = 200,
            Created = 201,
            Accepted = 202,
            NonAuthoritiveInformation = 203,
            NoContent = 204,
            ResetContent = 205,
            PartialContent = 206,
            MultipleChoices = 300,
            MovedPermanetly = 301,
            Found = 302,
            SeeOther = 303,
            NotModified = 304,
            UserProxy = 305,
            TemporaryRedirect = 307,
            BadRequest = 400,
            Unauthorized = 401,
            PaymentRequired = 402,
            Forbidden = 403,
            NotFound = 404,
            MethodNotAllowed = 405,
            NotAcceptable = 406,
            ProxyAuthenticantionRequired = 407,
            RequestTimeout = 408,
            Conflict = 409,
            Gone = 410,
            PreconditionFailed = 411,
            RequestEntityTooLarge = 413,
            RequestURITooLong = 414,
            UnsupportedMediaType = 415,
            RequestedRangeNotSatisfied = 416,
            ExpectationFailed = 417,
            InternalServerError = 500,
            NotImplemented = 501,
            BadGateway = 502,
            ServiceUnavailable = 503,
            GatewayTimeout = 504,
            HTTPVersionNotSupported = 505
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public HttpProxyClient()
        {
        }

        /// <summary>
        /// Creates a HTTP proxy client object using the supplied TcpClient object connection.
        /// </summary>
        /// <param name="tcpClient">A TcpClient connection object.</param>
        public HttpProxyClient(TcpClient tcpClient)
        {
            if (tcpClient == null)
                throw new ArgumentNullException("tcpClient");

            _tcpClientCached = tcpClient;
        }


        /// <summary>
        /// Constructor.  The default HTTP proxy port 8080 is used.
        /// </summary>
        /// <param name="proxyHost">Host name or IP address of the proxy.</param>
        public HttpProxyClient(string proxyHost)
        {
            if (String.IsNullOrEmpty(proxyHost))
                throw new ArgumentNullException("proxyHost");

            _proxyHost = proxyHost;
            _proxyPort = HTTP_PROXY_DEFAULT_PORT;
        }

        /// <summary>
        /// Constructor.  
        /// </summary>
        /// <param name="proxyHost">Host name or IP address of the proxy server.</param>
        /// <param name="proxyPort">Port number to connect to the proxy server.</param>
        /// <param name="proxyUsername">Username for the proxy server.</param>
        /// <param name="proxyPassword">Password for the proxy server.</param>
        public HttpProxyClient(string proxyHost, int proxyPort, string proxyUsername, string proxyPassword)
        {
            if (String.IsNullOrEmpty(proxyHost))
                throw new ArgumentNullException("proxyHost");

            if (String.IsNullOrEmpty(proxyUsername))
                throw new ArgumentNullException("proxyUsername");

            if (proxyPassword == null)
                throw new ArgumentNullException("proxyPassword");

            if (proxyPort <= 0 || proxyPort > 65535)
                throw new ArgumentOutOfRangeException("proxyPort", "port must be greater than zero and less than 65535");

            _proxyHost = proxyHost;
            _proxyPort = proxyPort;
            _proxyUsername = proxyUsername;
            _proxyPassword = proxyPassword;
        }


        /// <summary>
        /// Constructor.  
        /// </summary>
        /// <param name="proxyHost">Host name or IP address of the proxy server.</param>
        /// <param name="proxyPort">Port number for the proxy server.</param>
        public HttpProxyClient(string proxyHost, int proxyPort)
        {
            if (String.IsNullOrEmpty(proxyHost))
                throw new ArgumentNullException("proxyHost");

            if (proxyPort <= 0 || proxyPort > 65535)
                throw new ArgumentOutOfRangeException("proxyPort", "port must be greater than zero and less than 65535");

            _proxyHost = proxyHost;
            _proxyPort = proxyPort;
        }

        /// <summary>
        /// Gets or sets host name or IP address of the proxy server.
        /// </summary>
        public string ProxyHost
        {
            get { return _proxyHost; }
            set { _proxyHost = value; }
        }

        /// <summary>
        /// Gets or sets port number for the proxy server.
        /// </summary>
        public int ProxyPort
        {
            get { return _proxyPort; }
            set { _proxyPort = value; }
        }

        /// <summary>
        /// Gets String representing the name of the proxy. 
        /// </summary>
        /// <remarks>This property will always return the value 'HTTP'</remarks>
        public string ProxyName
        {
            get { return PROXY_NAME; }
        }

        public int SendTimeout
        {
            get => _sendTimeout;
            set => _sendTimeout = value;
        }

        public int ReceiveTimeout
        {
            get => _receiveTimeout;
            set => _receiveTimeout = value;
        }

        /// <summary>
        /// Gets or sets the TcpClient object. 
        /// This property can be set prior to executing CreateConnection to use an existing TcpClient connection.
        /// </summary>
        public TcpClient TcpClient
        {
            get { return _tcpClientCached; }
            set { _tcpClientCached = value; }
        }

        /// <summary>
        /// Creates a remote TCP connection through a proxy server to the destination host on the destination port.
        /// </summary>
        /// <param name="destinationHost">Destination host name or IP address.</param>
        /// <param name="destinationPort">Port number to connect to on the destination host.</param>
        /// <returns>
        /// Returns an open TcpClient object that can be used normally to communicate
        /// with the destination server
        /// </returns>
        /// <remarks>
        /// This method creates a connection to the proxy server and instructs the proxy server
        /// to make a pass through connection to the specified destination host on the specified
        /// port.  
        /// </remarks>
        public async Task<TcpClient> CreateConnection(string destinationHost, int destinationPort, CancellationToken cancellationToken)
        {
            try
            {
                // if we have no cached tcpip connection then create one
                if (_tcpClientCached == null)
                {
                    if (String.IsNullOrEmpty(_proxyHost))
                        throw new ProxyException("ProxyHost property must contain a value.");

                    if (_proxyPort <= 0 || _proxyPort > 65535)
                        throw new ProxyException("ProxyPort value must be greater than zero and less than 65535");

                    //  create new tcp client object to the proxy server
                    _tcpClient = new TcpClient();
                    _tcpClient.SendTimeout = _sendTimeout;
                    _tcpClient.ReceiveTimeout = _receiveTimeout;

                    // attempt to open the connection
                    await _tcpClient.ConnectAsync(_proxyHost, _proxyPort);
                }
                else
                {
                    _tcpClient = _tcpClientCached;
                }

                //  send connection command to proxy host for the specified destination host and port
                await SendConnectionCommand(destinationHost, destinationPort,cancellationToken);

                // remove the private reference to the tcp client so the proxy object does not keep it
                // return the open proxied tcp client object to the caller for normal use
                TcpClient rtn = _tcpClient;
                _tcpClient = null;
                return rtn;
            }
            catch (SocketException ex)
            {
                throw new ProxyException(String.Format(CultureInfo.InvariantCulture, "Connection to proxy host {0} on port {1} failed.", Utils.GetHost(_tcpClient), Utils.GetPort(_tcpClient)), ex);
            }
        }


        private async Task SendConnectionCommand(string host, int port, CancellationToken cancellationToken)
        {
            NetworkStream stream = _tcpClient.GetStream();

            string connectCmd = CreateCommandString(host, port);

            byte[] request = ASCIIEncoding.ASCII.GetBytes(connectCmd);

            // send the connect request
            await stream.WriteAsync(request, 0, request.Length, cancellationToken);

            // wait for the proxy server to respond
            await WaitForData(stream, cancellationToken);

            // PROXY SERVER RESPONSE
            // =======================================================================
            //HTTP/1.0 200 Connection Established<CR><LF>
            //[.... other HTTP header lines ending with <CR><LF>..
            //ignore all of them]
            //<CR><LF>    // Last Empty Line

            // create an byte response array  
            if (_tcpClient == null)
            {
                return;
            }
            byte[] response = new byte[_tcpClient.ReceiveBufferSize];
            StringBuilder sbuilder = new StringBuilder();
            int bytes = 0;
            long total = 0;

            do
            {
                bytes = await stream.ReadAsync(response, 0, _tcpClient.ReceiveBufferSize, cancellationToken);
                total += bytes;
                sbuilder.Append(System.Text.ASCIIEncoding.UTF8.GetString(response, 0, bytes));
            } while (stream.DataAvailable);

            ParseResponse(sbuilder.ToString());

            //  evaluate the reply code for an error condition
            if (_respCode != HttpResponseCodes.OK)
                HandleProxyCommandError(host, port);
        }

        private string CreateCommandString(string host, int port)
        {
            string connectCmd;
            var head = HeadStringFunc?.Invoke() ?? string.Empty;
            if (head.Length > 0 && !head.EndsWith("\r\n"))
            {
                head += "\r\n";
            }
            if (!string.IsNullOrEmpty(_proxyUsername))
            {
                //  gets the user/pass into base64 encoded string in the form of [username]:[password]
                string auth = Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", _proxyUsername, _proxyPassword)));

                // PROXY SERVER REQUEST
                // =======================================================================
                //CONNECT starksoft.com:443 HTTP/1.0<CR><LF>
                //HOST starksoft.com:443<CR><LF>
                //Proxy-Authorization: username:password<CR><LF>
                //              NOTE: username:password string will be base64 encoded as one 
                //                        concatenated string
                //[... other HTTP header lines ending with <CR><LF> if required]>
                //<CR><LF>    // Last Empty Line
                connectCmd = String.Format(CultureInfo.InvariantCulture, HTTP_PROXY_AUTHENTICATE_CMD, host, port.ToString(CultureInfo.InvariantCulture), auth, head);
            }
            else
            {
                // PROXY SERVER REQUEST
                // =======================================================================
                //CONNECT starksoft.com:443 HTTP/1.0 <CR><LF>
                //HOST starksoft.com:443<CR><LF>
                //[... other HTTP header lines ending with <CR><LF> if required]>
                //<CR><LF>    // Last Empty Line
                
                connectCmd = String.Format(CultureInfo.InvariantCulture, HTTP_PROXY_CONNECT_CMD, host, port.ToString(CultureInfo.InvariantCulture), head);
            }

            return connectCmd;
        }

        private void HandleProxyCommandError(string host, int port)
        {
            string msg;

            switch (_respCode)
            {
                case HttpResponseCodes.None:
                    msg = String.Format(CultureInfo.InvariantCulture, "Proxy destination {0} on port {1} failed to return a recognized HTTP response code.  Server response: {2}", Utils.GetHost(_tcpClient), Utils.GetPort(_tcpClient), _respText);
                    break;

                case HttpResponseCodes.BadGateway:
                    //HTTP/1.1 502 Proxy Error (The specified Secure Sockets Layer (SSL) port is not allowed. ISA Server is not configured to allow SSL requests from this port. Most Web browsers use port 443 for SSL requests.)
                    msg = String.Format(CultureInfo.InvariantCulture, "Proxy destination {0} on port {1} responded with a 502 code - Bad Gateway.  If you are connecting to a Microsoft ISA destination please refer to knowledge based article Q283284 for more information.  Server response: {2}", Utils.GetHost(_tcpClient), Utils.GetPort(_tcpClient), _respText);
                    break;

                default:
                    msg = String.Format(CultureInfo.InvariantCulture, "Proxy destination {0} on port {1} responded with a {2} code - {3}", Utils.GetHost(_tcpClient), Utils.GetPort(_tcpClient), ((int)_respCode).ToString(CultureInfo.InvariantCulture), _respText);
                    break;
            }

            //  throw a new application exception 
            throw new ProxyException(msg);
        }

        private async Task WaitForData(NetworkStream stream, CancellationToken cancellationToken)
        {
            int sleepTime = 0;
            while (!stream.DataAvailable)
            {
                await Task.Delay(WAIT_FOR_DATA_INTERVAL, cancellationToken);
                sleepTime += WAIT_FOR_DATA_INTERVAL;
                if (sleepTime > WAIT_FOR_DATA_TIMEOUT)
                    throw new ProxyException(String.Format("A timeout while waiting for the proxy server at {0} on port {1} to respond.", Utils.GetHost(_tcpClient), Utils.GetPort(_tcpClient)));
            }
        }

        private void ParseResponse(string response)
        {
            string[] data = null;

            //  get rid of the LF character if it exists and then split the string on all CR
            data = response.Replace('\n', ' ').Split('\r');

            ParseCodeAndText(data[0]);
        }

        private void ParseCodeAndText(string line)
        {
            int begin = 0;
            int end = 0;
            string val = null;

            if (line.IndexOf("HTTP") == -1)
                throw new ProxyException(String.Format("No HTTP response received from proxy destination.  Server response: {0}.", line));

            begin = line.IndexOf(" ") + 1;
            end = line.IndexOf(" ", begin);

            val = line.Substring(begin, end - begin);
            Int32 code = 0;

            if (!Int32.TryParse(val, out code))
                throw new ProxyException(String.Format("An invalid response code was received from proxy destination.  Server response: {0}.", line));

            _respCode = (HttpResponseCodes)code;
            _respText = line.Substring(end + 1).Trim();
        }


        #region "Async Methods"

        private BackgroundWorker _asyncWorker;
        private Exception _asyncException;
        bool _asyncCancelled;
        private int _sendTimeout;
        private int _receiveTimeout;

        /// <summary>
        /// Gets a value indicating whether an asynchronous operation is running.
        /// </summary>
        /// <remarks>Returns true if an asynchronous operation is running; otherwise, false.
        /// </remarks>
        public bool IsBusy
        {
            get { return _asyncWorker == null ? false : _asyncWorker.IsBusy; }
        }

        /// <summary>
        /// Gets a value indicating whether an asynchronous operation is cancelled.
        /// </summary>
        /// <remarks>Returns true if an asynchronous operation is cancelled; otherwise, false.
        /// </remarks>
        public bool IsAsyncCancelled
        {
            get { return _asyncCancelled; }
        }

        public Func<string> HeadStringFunc { get ; set ; }

        /// <summary>
        /// Cancels any asychronous operation that is currently active.
        /// </summary>
        public void CancelAsync()
        {
            if (_asyncWorker != null && !_asyncWorker.CancellationPending && _asyncWorker.IsBusy)
            {
                _asyncCancelled = true;
                _asyncWorker.CancelAsync();
            }
        }

        private void CreateAsyncWorker()
        {
            if (_asyncWorker != null)
                _asyncWorker.Dispose();
            _asyncException = null;
            _asyncWorker = null;
            _asyncCancelled = false;
            _asyncWorker = new BackgroundWorker();
        }

        /// <summary>
        /// Event handler for CreateConnectionAsync method completed.
        /// </summary>
        public event EventHandler<CreateConnectionAsyncCompletedEventArgs> CreateConnectionAsyncCompleted;

        /// <summary>
        /// Asynchronously creates a remote TCP connection through a proxy server to the destination host on the destination port.
        /// </summary>
        /// <param name="destinationHost">Destination host name or IP address.</param>
        /// <param name="destinationPort">Port number to connect to on the destination host.</param>
        /// <returns>
        /// Returns an open TcpClient object that can be used normally to communicate
        /// with the destination server
        /// </returns>
        /// <remarks>
        /// This method creates a connection to the proxy server and instructs the proxy server
        /// to make a pass through connection to the specified destination host on the specified
        /// port.  
        /// </remarks>
        public void CreateConnectionAsync(string destinationHost, int destinationPort)
        {
            if (_asyncWorker != null && _asyncWorker.IsBusy)
                throw new InvalidOperationException("The HttpProxy object is already busy executing another asynchronous operation.  You can only execute one asychronous method at a time.");

            CreateAsyncWorker();
            _asyncWorker.WorkerSupportsCancellation = true;
            _asyncWorker.DoWork += new DoWorkEventHandler(CreateConnectionAsync_DoWork);
            _asyncWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(CreateConnectionAsync_RunWorkerCompleted);
            Object[] args = new Object[2];
            args[0] = destinationHost;
            args[1] = destinationPort;
            _asyncWorker.RunWorkerAsync(args);
        }

        private async void CreateConnectionAsync_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                Object[] args = (Object[])e.Argument;
                e.Result =await CreateConnection((string)args[0], (int)args[1],new CancellationToken());
            }
            catch (Exception ex)
            {
                _asyncException = ex;
            }
        }

        private void CreateConnectionAsync_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (CreateConnectionAsyncCompleted != null)
                CreateConnectionAsyncCompleted(this, new CreateConnectionAsyncCompletedEventArgs(_asyncException, _asyncCancelled, (TcpClient)e.Result));
        }

        #endregion
    }
}