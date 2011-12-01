//-----------------------------------------------------------------------
// <copyright file="WebSocket.cs" company="Nick Malaguti">
//   Copyright (c) Nick Malaguti.
// </copyright>
// <license>
//   This source code is subject to the MIT License
//   See http://www.opensource.org/licenses/mit-license.html
//   All other rights reserved.
// </license>
//-----------------------------------------------------------------------

namespace WebSocketClient
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;

    /// <summary>
    /// Client implementation of the WebSocket protocol draft http://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-10
    /// </summary>
    public class WebSocket
    {
        /// <summary>
        /// GUID appended to Sec-WebSocket-Key by the server as part of the WebSocket handshake
        /// </summary>
        private const string KeyGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        /// <summary>
        /// WebSocket protocol version supplied as Sec-WebSocket-Protocol in the opening handshake
        /// </summary>
        private const string ProtocolVersion = "8";

        /// <summary>
        /// <c>true</c> if the WebSocket has sent a close opcode to the server; otherwise, <c>false</c>.
        /// </summary>
        private bool hasSentClose = false;

        /// <summary>
        /// TCP socket used to communicate with the WebSocket server
        /// </summary>
        private Socket socket = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebSocket"/> class with the specified server URI and website origin.
        /// </summary>
        /// <param name="uri">The URI of the WebSocket server to connect to.</param>
        /// <param name="origin">The website origin supplied in the Sec-WebSocket-Origin header of the client handshake.</param>
        public WebSocket(Uri uri, string origin)
        {
            this.Uri = uri;
            this.Origin = origin;
        }

        /// <summary>
        /// Represents the method that will handle a WebSocket event that has event data
        /// </summary>
        /// <param name="sender">The WebSocket instance generating the event.</param>
        /// <param name="e">The <see cref="WebSocketClient.WebSocketEventArgs"/> instance containing the event data.</param>
        public delegate void WebSocketEventHandler(object sender, WebSocketEventArgs e);

        /// <summary>
        /// Occurs when the WebSocket is closed.
        /// </summary>
        public event EventHandler Closed;

        /// <summary>
        /// Occurs when the WebSocket is connected.
        /// </summary>
        public event EventHandler Connected;

        /// <summary>
        /// Occurs when the WebSocket receives a message.
        /// </summary>
        public event WebSocketEventHandler MessageReceived;

        /// <summary>
        /// Occurs when the WebSocket sends a message.
        /// </summary>
        public event EventHandler MessageSent;

        /// <summary>
        /// The opcodes that may be specified in a WebSocket frame header 
        /// </summary>
        public enum Opcode : int
        {
            /// <summary>
            /// A continuation frame. Append this data to the previous frame.
            /// </summary>
            Continuation = 0x0,

            /// <summary>
            /// A text frame.
            /// </summary>
            Text = 0x1,

            /// <summary>
            /// A binary frame.
            /// </summary>
            Binary = 0x2,

            /// <summary>
            /// A close frame.
            /// </summary>
            Close = 0x8,

            /// <summary>
            /// A ping frame.
            /// </summary>
            Ping = 0x9,

            /// <summary>
            /// A pong frame.
            /// </summary>
            Pong = 0xA,
        }

        /// <summary>
        /// The closure reasons that may be specified in a frame with a close opcode.
        /// </summary>
        private enum CloseReason : ushort
        {
            /// <summary>
            /// Indicates a normal closure, meaning whatever purpose the
            /// connection was established for has been fulfilled.
            /// </summary>
            Normal = 1000,

            /// <summary>
            /// Indicates that an endpoint is "going away", such as a server
            /// going down, or a browser having navigated away from a page.
            /// </summary>
            GoingAway = 1001,

            /// <summary>
            /// indicates that an endpoint is terminating the connection due
            /// to a protocol error.
            /// </summary>
            ProtocolError = 1002,

            /// <summary>
            /// indicates that an endpoint is terminating the connection
            /// because it has received a type of data it cannot accept (e.g. an
            /// endpoint that understands only text data MAY send this if it
            /// receives a binary message).
            /// </summary>
            UnacceptableDatatype = 1003,

            /// <summary>
            /// 1004 indicates that an endpoint is terminating the connection
            /// because it has received a frame that is too large.
            /// </summary>
            FrameTooLarge = 1004,
        }

        /// <summary>
        /// Gets a value indicating whether this WebSocket is connected.
        /// </summary>
        /// <value>
        /// <c>true</c> if this WebSocket is connected; otherwise, <c>false</c>.
        /// </value>
        public bool IsConnected
        {
            get { return this.socket.Connected; }
        }

        /// <summary>
        /// Gets the origin. This is the value supplied for the Sec-WebSocket-Origin header in the client handshake.
        /// </summary>
        public string Origin
        {
            get; private set;
        }

        /// <summary>
        /// Gets the URI of the WebSocket server to connect to.
        /// </summary>
        public Uri Uri
        {
            get; private set;
        }

        /// <summary>
        /// Gets or sets the host of the WebSocket server to connect to.
        /// </summary>
        /// <value>
        /// The host of the WebSocket server to connect to.
        /// </value>
        private string Host
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the path of the WebSocket server to connect to.
        /// </summary>
        /// <value>
        /// The path of the WebSocket server to connect to.
        /// </value>
        private string Path
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the port of the WebSocket server to connect to.
        /// </summary>
        /// <value>
        /// The port of the WebSocket server to connect to.
        /// </value>
        private int Port
        {
            get; set;
        }

        /// <summary>
        /// Initiates close on the WebSocket.
        /// </summary>
        public void Close()
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "WebSocket Public Close Called. Thread ID: {0}. Socket Connected: {1}", Thread.CurrentThread.ManagedThreadId, this.socket.Connected));

            if (this.socket.Connected)
            {
                this.Close(CloseReason.Normal, false);
            }
        }

        /// <summary>
        /// Initiates connect on the WebSocket.
        /// </summary>
        public void Connect()
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "WebSocket Constructor Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));

            this.GetSocket();
            this.ReceiveHandshake(this.SendHandshake());

            this.OnConnected();

            StateObject so = new StateObject();
            so.Socket = this.socket;
            this.socket.BeginReceive(so.Buffer, 0, StateObject.BufferSize, SocketFlags.None, new AsyncCallback(this.ReadCallback), so);
        }

        /// <summary>
        /// Sends the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Send(string message)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "WebSocket Public Send Called. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId, this.socket.Connected));

            byte[] utf8message = Encoding.UTF8.GetBytes(message);
            this.Send(Opcode.Text, utf8message);
        }

        /// <summary>
        /// Raises the <see cref="E:Closed"/> event.
        /// </summary>
        protected void OnClosed()
        {
            if (this.Closed != null)
            {
                this.Closed(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Raises the <see cref="E:Connected"/> event.
        /// </summary>
        protected void OnConnected()
        {
            if (this.Connected != null)
            {
                this.Connected(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Raises the <see cref="E:MessageReceived"/> event.
        /// </summary>
        /// <param name="e">The <see cref="WebSocketClient.WebSocketEventArgs"/> instance containing the event data.</param>
        protected void OnMessageReceived(WebSocketEventArgs e)
        {
            if (this.MessageReceived != null)
            {
                this.MessageReceived(this, e);
            }
        }

        /// <summary>
        /// Raises the <see cref="E:MessageSent"/> event.
        /// </summary>
        protected void OnMessageSent()
        {
            if (this.MessageSent != null)
            {
                this.MessageSent(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// asynchronously closes the WebSocket with the specified reason.
        /// </summary>
        /// <param name="reason">The reason.</param>
        /// <param name="hasReceivedClose">if set to <c>true</c> the server initiated close first; otherwise, <c>false</c>.</param>
        private void Close(CloseReason reason, bool hasReceivedClose)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "WebSocket Close. Thread ID: {0}. Reason: {1}", Thread.CurrentThread.ManagedThreadId, reason.ToString()));

            if (!this.hasSentClose)
            {
                Debug.WriteLine("WebSocket Sending Close to server.");
                this.Send(Opcode.Close, BitConverter.GetBytes(IPAddress.HostToNetworkOrder((ushort)reason)));
                this.hasSentClose = true;
            }

            if (this.hasSentClose && hasReceivedClose)
            {
                Debug.WriteLine("WebSocket Received close from server and already sent close. Shutting down.");
                this.socket.Shutdown(SocketShutdown.Both);
                this.socket.Close();
                this.OnClosed();
            }
        }

        /// <summary>
        /// Creates a socket and connects to the WebSocket server.
        /// </summary>
        /// <exception cref="ArgumentException">Throws an ArgumentException when the socket cannot connect to the specified host and port.</exception>
        private void GetSocket()
        {
            if (this.Uri.Scheme != "ws" ||
                string.IsNullOrEmpty(this.Uri.Host))
            {
                throw new NotImplementedException(string.Format(CultureInfo.InvariantCulture, "{0} is not a supported WebSocket URI", this.Uri));
            }

            this.Host = this.Uri.Host;
            this.Port = this.Uri.Port < 0 ? 80 : this.Uri.Port;
            this.Path = this.Uri.AbsolutePath;

            IPHostEntry hostEntry = Dns.GetHostEntry(this.Host);

            foreach (IPAddress address in hostEntry.AddressList)
            {
                IPEndPoint ipe = new IPEndPoint(address, this.Port);
                Socket tempSocket = new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                tempSocket.Connect(ipe);

                if (tempSocket.Connected)
                {
                    this.socket = tempSocket;
                    break;
                }
                else
                {
                    continue;
                }
            }

            if (this.socket == null)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Host {0} could not be resolved", this.Host));
            }
        }

        /// <summary>
        /// Callback invoked when an asynchronous socket read completes
        /// </summary>
        /// <param name="asyncResult">An IAsyncResult that stores any state information and any user defined data for this asynchronous operation.</param>
        private void ReadCallback(IAsyncResult asyncResult)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "WebSocket ReadCallback. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));

            StateObject so = (StateObject)asyncResult.AsyncState;
            Socket s = so.Socket;
            byte[] buffer = so.Buffer;

            int length = s.EndReceive(asyncResult);

            if (length > 0)
            {
                int index = 0;

                while (index < length)
                {
                    if (so.IsFinalFragment.HasValue && so.Opcode.HasValue && so.PayloadLengthFinalized && so.IsMasked.HasValue)
                    {
                        if (so.Opcode == Opcode.Continuation)
                        {
                            so.Opcode = so.FirstOpcode;
                        }

                        int leftToGo = (int)so.PayloadLength - so.Index;
                        int remainingBuffer = length - index;

                        if (so.Opcode == Opcode.Text)
                        {
                            if (leftToGo <= remainingBuffer)
                            {
                                // all buffer read
                                so.Text.Append(Encoding.UTF8.GetString(buffer, index, leftToGo));
                                index += leftToGo;

                                StringBuilder sb = null;
                                Opcode firstOpcode = so.Opcode.Value;

                                if (so.IsFinalFragment.Value)
                                {
                                    // final fragment
                                    WebSocketEventArgs args = new WebSocketEventArgs(so.Text.ToString());
                                    this.OnMessageReceived(args);
                                }
                                else
                                {
                                    // more fragments
                                    sb = so.Text;
                                }

                                so = new StateObject();
                                so.Socket = s;
                                so.FirstOpcode = firstOpcode;

                                if (sb != null)
                                {
                                    so.Text = sb;
                                }
                            }
                            else
                            {
                                // more unread buffer to go!
                                so.Text.Append(Encoding.UTF8.GetString(buffer, index, remainingBuffer));
                                index = length;
                                so.Index += remainingBuffer;
                            }
                        }
                        else if (so.Opcode == Opcode.Binary)
                        {
                            // binary
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Client closing due to an unacceptable datatype: Binary"));
                            this.Close(CloseReason.UnacceptableDatatype, false);

                            so = new StateObject();
                            so.Socket = s;

                            break;
                        }
                        else if (so.Opcode == Opcode.Close)
                        {
                            Debug.WriteLine("WebSocket Close opcode received.");

                            // connection close
                            if (leftToGo <= remainingBuffer)
                            {
                                // all buffer read
                                byte[] temp = new byte[leftToGo];
                                Array.Copy(buffer, index, temp, 0, leftToGo);
                                so.ControlFrameBinaryData.AddRange(temp);
                                index += leftToGo;

                                byte[] closeData = so.ControlFrameBinaryData.ToArray();

                                ushort closeReason = 0;
                                if (closeData.Length == 2)
                                {
                                    closeReason = BitConverter.ToUInt16(closeData, 0);
                                }

                                string closeReasonText = string.Empty;
                                if (closeData.Length > 2)
                                {
                                    closeReasonText = Encoding.UTF8.GetString(closeData, 2, closeData.Length - 2);
                                }

                                if (closeReason == (ushort)CloseReason.Normal)
                                {
                                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "Server closing normally: {0}", closeReasonText));
                                }
                                else if (closeReason == (ushort)CloseReason.GoingAway)
                                {
                                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "Server closing because server is going away: {0}", closeReasonText));
                                }
                                else if (closeReason == (ushort)CloseReason.FrameTooLarge)
                                {
                                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "Server closing because I sent a frame that was too large: {0}", closeReasonText));
                                }
                                else if (closeReason == (ushort)CloseReason.ProtocolError)
                                {
                                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "Server closing due to a Protocol Error: {0}", closeReasonText));
                                }
                                else if (closeReason == (ushort)CloseReason.UnacceptableDatatype)
                                {
                                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "Server closing because I sent an unacceptable datatype: {0}", closeReasonText));
                                }
                                else
                                {
                                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "Server closing for unknown reason: {0} ({1})", closeReasonText, closeReason));
                                }

                                Close(CloseReason.Normal, true);

                                so = new StateObject();
                                so.Socket = s;

                                break;
                            }
                            else
                            {
                                // more unread buffer to go!
                                byte[] temp = new byte[leftToGo];
                                Array.Copy(buffer, index, temp, 0, remainingBuffer);
                                so.ControlFrameBinaryData.AddRange(temp);
                                index = length;
                                so.Index += remainingBuffer;
                            }
                        }
                        else if (so.Opcode == Opcode.Ping)
                        {
                            Debug.WriteLine("WebSocket Ping opcode received.");

                            // ping
                            if (leftToGo <= remainingBuffer)
                            {
                                // all buffer read
                                byte[] temp = new byte[leftToGo];
                                Array.Copy(buffer, index, temp, 0, leftToGo);
                                so.ControlFrameBinaryData.AddRange(temp);
                                index += leftToGo;

                                StringBuilder sb = null;
                                Opcode firstOpcode = so.Opcode.Value;

                                // Send Pong
                                this.Send(Opcode.Pong, so.ControlFrameBinaryData.ToArray());

                                if (so.Text.Length > 0)
                                {
                                    sb = so.Text;
                                }

                                so = new StateObject();
                                so.Socket = s;
                                so.FirstOpcode = firstOpcode;

                                if (sb != null)
                                {
                                    so.Text = sb;
                                }
                            }
                            else
                            {
                                // more unread buffer to go!
                                byte[] temp = new byte[leftToGo];
                                Array.Copy(buffer, index, temp, 0, remainingBuffer);
                                so.ControlFrameBinaryData.AddRange(temp);
                                index = length;
                                so.Index += remainingBuffer;
                            }
                        }
                        else if (so.Opcode == Opcode.Pong)
                        {
                            // pong
                            throw new NotImplementedException("Pong opcode not implemented");
                        }
                        else
                        {
                            // this shouldn't be set
                            throw new NotImplementedException(string.Format(CultureInfo.InvariantCulture, "Unknown opcode: {0}", so.Opcode));
                        }
                    }
                    else
                    {
                        if (index < length && (!so.IsFinalFragment.HasValue || !so.Opcode.HasValue))
                        {
                            byte first = buffer[index++];
                            so.IsFinalFragment = (first & 0x80) > 0;
                            int opcode = first & 0xF;
                            if (Enum.IsDefined(typeof(Opcode), opcode))
                            {
                                so.Opcode = (Opcode)Enum.ToObject(typeof(Opcode), opcode);
                            }
                            else
                            {
                                throw new NotImplementedException(string.Format(CultureInfo.InvariantCulture, "Unknown opcode: {0}", so.Opcode));
                            }
                        }

                        if (index < length && (!so.IsMasked.HasValue || so.PayloadLength < 0))
                        {
                            byte second = buffer[index++];
                            so.IsMasked = (second & 0x80) > 0;
                            so.PayloadLength = second & 0x7F;
                        }

                        if (!so.PayloadLengthFinalized && so.PayloadLength > -1)
                        {
                            if (so.PayloadLength <= 125)
                            {
                                so.PayloadLengthFinalized = true;
                            }
                            else if (so.PayloadLength == 126)
                            {
                                while (index < length && so.PayloadLengthIndex < 2)
                                {
                                    so.PayloadLengthByteArray[so.PayloadLengthIndex++] = buffer[index++];
                                }

                                if (so.PayloadLengthIndex == 2)
                                {
                                    so.PayloadLength = (ushort)IPAddress.HostToNetworkOrder(BitConverter.ToInt16(so.PayloadLengthByteArray, 0));
                                    so.PayloadLengthFinalized = true;
                                }
                            }
                            else if (so.PayloadLength == 127)
                            {
                                while (index < length && so.PayloadLengthIndex < 8)
                                {
                                    so.PayloadLengthByteArray[so.PayloadLengthIndex++] = buffer[index++];
                                }

                                if (so.PayloadLengthIndex == 8)
                                {
                                    so.PayloadLength = IPAddress.HostToNetworkOrder(BitConverter.ToInt64(so.PayloadLengthByteArray, 0));
                                    so.PayloadLengthFinalized = true;
                                }
                            }
                        }

                        if (so.PayloadLength > int.MaxValue)
                        {
                            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "Client closing because I received a frame that was too large"));
                            Close(CloseReason.FrameTooLarge, false);

                            so = new StateObject();
                            so.Socket = s;

                            break;
                        }

                        if (so.IsMasked.HasValue && so.IsMasked.Value)
                        {
                            while (index < length && so.MaskIndex < 4)
                            {
                                so.Mask[so.MaskIndex++] = buffer[index++];
                            }
                        }
                    }
                }

                if (so.PayloadLength > -1)
                {
                    if (so.Opcode == Opcode.Close)
                    {
                        Debug.WriteLine("WebSocket Close opcode received.");
                        Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "Server closing for unknown reason"));

                        this.Close(CloseReason.Normal, true);

                        so = new StateObject();
                        so.Socket = s;
                    }
                }

                try
                {
                    s.BeginReceive(so.Buffer, 0, StateObject.BufferSize, SocketFlags.None, new AsyncCallback(this.ReadCallback), so);
                }
                catch (ObjectDisposedException)
                {
                    // the socket has been closed, so we don't need to read anymore
                }
            }
            else
            {
                if (this.hasSentClose)
                {
                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "WebSocket Socket Closed after sending close opcode. Thread ID: {0}. Socket Connected: {1}", Thread.CurrentThread.ManagedThreadId, this.socket.Connected));
                }
                else
                {
                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "WebSocket Socket Closed unexpectedly. Thread ID: {0}. Socket Connected: {1}", Thread.CurrentThread.ManagedThreadId, this.socket.Connected));
                }

                s.Close();
                this.OnClosed();
            }
        }

        /// <summary>
        /// Synchronously listens for the handshake response from the server.
        /// </summary>
        /// <param name="key">The key supplied in the Sec-Websocket-Key header of the client handshake.</param>
        private void ReceiveHandshake(string key)
        {
            HashAlgorithm hashAlg = new SHA1Managed();
            string responseKey = Convert.ToBase64String(hashAlg.ComputeHash(Encoding.ASCII.GetBytes(key + KeyGuid)));

            // the response shouldn't be longer than 1024 characters
            byte[] buffer = new byte[1024];
            int length = 0;

            length = this.socket.Receive(buffer);
            string response = Encoding.ASCII.GetString(buffer, 0, length);

            string[] headers = response.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            bool success = false;
            string description = string.Empty;
            int code = 0;

            foreach (string header in headers)
            {
                if (header.StartsWith("HTTP/1.1", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = header.Split(new char[] { ' ' }, 3);
                    code = int.Parse(parts[1]);
                    description = parts[2];

                    if (code == 101)
                    {
                        success = true;
                    }
                    else
                    {
                        success = false;
                    }
                }
                else if (success && header.StartsWith("Upgrade:", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = header.Split(new char[] { ' ' }, 2);
                    success = parts[1].Equals("websocket", StringComparison.OrdinalIgnoreCase);
                }
                else if (success && header.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = header.Split(new char[] { ' ' }, 2);
                    success = parts[1].Equals("Upgrade", StringComparison.OrdinalIgnoreCase);
                }
                else if (success && header.StartsWith("Sec-WebSocket-Accept:", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = header.Split(new char[] { ' ' }, 2);
                    success = parts[1].Equals(responseKey, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (!success)
            {
                this.Close();
                throw new ApplicationException(string.Format(CultureInfo.InvariantCulture, "Handshake failed: {0} ({1})", description, code));
            }
        }

        /// <summary>
        /// Asynchronously sends the specified data with the specified opcode.
        /// </summary>
        /// <param name="opcode">The opcode.</param>
        /// <param name="data">The data.</param>
        private void Send(Opcode opcode, byte[] data)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "WebSocket Send. Thread ID: {0}. Opcode: {1}", Thread.CurrentThread.ManagedThreadId, ((Opcode)opcode).ToString()));

            List<byte> buffer = new List<byte>();

            byte first = 0x80; // fin = 1
            first |= (byte)opcode;

            buffer.Add(first);

            byte second = 0x80; // mask = 1

            if (opcode == Opcode.Text)
            {
                byte[] extendedPayloadLength = null;

                if (data.Length > 125)
                {
                    if (data.Length > 0xFFFF)
                    {
                        second |= 127;
                        extendedPayloadLength = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((long)data.Length));
                    }
                    else
                    {
                        second |= 126;
                        extendedPayloadLength = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)data.Length));
                    }
                }
                else
                {
                    second |= (byte)data.Length;
                }

                buffer.Add(second);

                if (extendedPayloadLength != null)
                {
                    buffer.AddRange(extendedPayloadLength);
                }
            }
            else if (opcode == Opcode.Binary)
            {
                throw new NotImplementedException("Binary opcode not implemented");
            }
            else if (opcode == Opcode.Ping)
            {
                throw new NotImplementedException("Ping opcode not implemented");
            }
            else if (opcode == Opcode.Pong || opcode == Opcode.Close)
            {
                if (data.Length > 125)
                {
                    throw new ArgumentOutOfRangeException("data", "Control frames must have less than 125 bytes of data");
                }

                second |= (byte)data.Length;
                buffer.Add(second);
            }
            else
            {
                throw new NotImplementedException(string.Format(CultureInfo.InvariantCulture, "Unknown opcode: {0}", opcode));
            }

            Random random = new Random();

            byte[] mask = new byte[4];
            random.NextBytes(mask);

            buffer.AddRange(mask);

            for (int i = 0; i < data.Length; i++)
            {
                int j = i % 4;
                buffer.Add((byte)(data[i] ^ mask[j]));
            }

            byte[] message = buffer.ToArray();

            StateObject so = new StateObject();
            so.Socket = this.socket;
            so.PayloadLength = message.Length;

            this.socket.BeginSend(message, 0, message.Length, SocketFlags.None, new AsyncCallback(this.SendCallback), so);
        }

        /// <summary>
        /// Callback invoked when an asynchronous socket send completes
        /// </summary>
        /// <param name="asyncResult">An IAsyncResult that stores any state information and any user defined data for this asynchronous operation.</param>
        private void SendCallback(IAsyncResult asyncResult)
        {
            StateObject so = (StateObject)asyncResult.AsyncState;
            Socket s = so.Socket;

            try
            {
                int length = s.EndSend(asyncResult);
            }
            catch (ObjectDisposedException)
            {
                // didn't send because socket was closed. Doesn't really matter.
            }

            this.OnMessageSent();
        }

        /// <summary>
        /// Synchronously sends the client handshake.
        /// </summary>
        /// <returns>The key supplied in the Sec-Websocket-Key header of the client handshake.</returns>
        private string SendHandshake()
        {
            Random random = new Random();

            byte[] keyBytes = new byte[16];
            random.NextBytes(keyBytes);

            string key = Convert.ToBase64String(keyBytes);

            // handshake
            StringBuilder handshake = new StringBuilder();
            handshake.Append(string.Format(CultureInfo.InvariantCulture, "GET {0} HTTP/1.1\r\n", this.Path));
            handshake.Append(string.Format(CultureInfo.InvariantCulture, "Upgrade: websocket\r\n"));
            handshake.Append(string.Format(CultureInfo.InvariantCulture, "Connection: Upgrade\r\n"));
            handshake.Append(string.Format(CultureInfo.InvariantCulture, "Host: {0}\r\n", this.Host));

            if (!string.IsNullOrEmpty(this.Origin))
            {
                handshake.Append(string.Format(CultureInfo.InvariantCulture, "Origin: {0}\r\n", this.Origin));
            }

            handshake.Append(string.Format(CultureInfo.InvariantCulture, "Sec-Websocket-Key: {0}\r\n", key));
            handshake.Append(string.Format(CultureInfo.InvariantCulture, "Sec-Websocket-Version: {0}\r\n", ProtocolVersion));
            handshake.Append(string.Format(CultureInfo.InvariantCulture, "\r\n"));

            this.socket.Send(Encoding.ASCII.GetBytes(handshake.ToString()));

            return key;
        }
    }
}