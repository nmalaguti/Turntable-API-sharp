//-----------------------------------------------------------------------
// <copyright file="StateObject.cs" company="Nick Malaguti">
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
    using System.Collections.Generic;
    using System.Net.Sockets;
    using System.Text;

    /// <summary>
    /// Class used to store data from asynchronous network reads
    /// </summary>
    internal class StateObject
    {
        /// <summary>
        /// Size of the byte buffer used to read from the network
        /// </summary>
        public const int BufferSize = 4096;

        /// <summary>
        /// The buffer used to receive data from the socket.
        /// </summary>
        private byte[] buffer = new byte[BufferSize];

        /// <summary>
        /// The control frame binary data.
        /// </summary>
        private List<byte> controlFrameBinaryData = new List<byte>();

        /// <summary>
        /// The opcode of the initial payload fragment. <c>null</c> if this fragment is the only payload fragment.
        /// </summary>
        private WebSocket.Opcode? firstOpcode = null;

        /// <summary>
        /// The index of the buffer.
        /// </summary>
        private int index = 0;

        /// <summary>
        /// <c>null</c> if the payload final fragment bit has not been parsed; <c>true</c> if this payload fragment is the final fragment; otherwise, <c>false</c>.
        /// </summary>
        private bool? isFinalFragment = false;

        /// <summary>
        /// <c>null</c> if the mask bit has not been parsed; <c>true</c> if this payload is masked; otherwise, <c>false</c>.
        /// </summary>
        private bool? isMasked = false;

        /// <summary>
        /// The mask byte array.
        /// </summary>
        private byte[] mask = new byte[4];

        /// <summary>
        /// The index of the mask byte array.
        /// </summary>
        private int maskIndex = 0;

        /// <summary>
        /// The opcode. <c>null</c> if the payload fragment opcode has not been parsed.
        /// </summary>
        private WebSocket.Opcode? opcode = null;

        /// <summary>
        /// The length of the payload.
        /// </summary>
        private long payloadLength = -1;

        /// <summary>
        /// The payload length byte array.
        /// </summary>
        private byte[] payloadLengthByteArray = new byte[8];

        /// <summary>
        /// <c>true</c> if the entire payload length has been parsed; otherwise, <c>false</c>.
        /// </summary>
        private bool payloadLengthFinalized = false;

        /// <summary>
        /// The index of the payload length byte array.
        /// </summary>
        private int payloadLengthIndex = 0;

        /// <summary>
        /// The socket.
        /// </summary>
        private Socket socket = null;

        /// <summary>
        /// The payload text.
        /// </summary>
        private StringBuilder text = new StringBuilder();

        /// <summary>
        /// Gets or sets the buffer used to receive data from the socket.
        /// </summary>
        /// <value>
        /// The buffer used to receive data from the socket.
        /// </value>
        public byte[] Buffer
        {
            get { return this.buffer; }
            set { this.buffer = value; }
        }

        /// <summary>
        /// Gets or sets the control frame binary data.
        /// </summary>
        /// <value>
        /// The control frame binary data.
        /// </value>
        public List<byte> ControlFrameBinaryData
        {
            get { return this.controlFrameBinaryData; }
            set { this.controlFrameBinaryData = value; }
        }

        /// <summary>
        /// Gets or sets the opcode of the inital payload fragment.
        /// </summary>
        /// <value>
        /// The opcode of the initial payload fragment. <c>null</c> if this fragment is the only payload fragment.
        /// </value>
        public WebSocket.Opcode? FirstOpcode
        {
            get { return this.firstOpcode; }
            set { this.firstOpcode = value; }
        }

        /// <summary>
        /// Gets or sets the index of the buffer.
        /// </summary>
        /// <value>
        /// The index of the buffer.
        /// </value>
        public int Index
        {
            get { return this.index; }
            set { this.index = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this payload fragment is the final fragment.
        /// </summary>
        /// <value>
        /// <c>null</c> if the payload final fragment bit has not been parsed; <c>true</c> if this payload fragment is the final fragment; otherwise, <c>false</c>.
        /// </value>
        public bool? IsFinalFragment
        {
            get { return this.isFinalFragment; }
            set { this.isFinalFragment = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this payload is masked.
        /// </summary>
        /// <value>
        /// <c>null</c> if the mask bit has not been parsed; <c>true</c> if this payload is masked; otherwise, <c>false</c>.
        /// </value>
        public bool? IsMasked
        {
            get { return this.isMasked; }
            set { this.isMasked = value; }
        }

        /// <summary>
        /// Gets or sets the mask byte array.
        /// </summary>
        /// <value>
        /// The mask byte array.
        /// </value>
        public byte[] Mask
        {
            get { return this.mask; }
            set { this.mask = value; }
        }

        /// <summary>
        /// Gets or sets the index of the mask.
        /// </summary>
        /// <value>
        /// The index of the mask byte array.
        /// </value>
        public int MaskIndex
        {
            get { return this.maskIndex; }
            set { this.maskIndex = value; }
        }

        /// <summary>
        /// Gets or sets the opcode.
        /// </summary>
        /// <value>
        /// The opcode. <c>null</c> if the payload fragment opcode has not been parsed.
        /// </value>
        public WebSocket.Opcode? Opcode
        {
            get { return this.opcode; }
            set { this.opcode = value; }
        }

        /// <summary>
        /// Gets or sets the length of the payload.
        /// </summary>
        /// <value>
        /// The length of the payload.
        /// </value>
        public long PayloadLength
        {
            get { return this.payloadLength; }
            set { this.payloadLength = value; }
        }

        /// <summary>
        /// Gets or sets the payload length byte array.
        /// </summary>
        /// <value>
        /// The payload length byte array.
        /// </value>
        public byte[] PayloadLengthByteArray
        {
            get { return this.payloadLengthByteArray; }
            set { this.payloadLengthByteArray = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the entire payload length has been parsed.
        /// </summary>
        /// <value>
        /// <c>true</c> if the entire payload length has been parsed; otherwise, <c>false</c>.
        /// </value>
        public bool PayloadLengthFinalized
        {
            get { return this.payloadLengthFinalized; }
            set { this.payloadLengthFinalized = value; }
        }

        /// <summary>
        /// Gets or sets the index of the payload length byte array.
        /// </summary>
        /// <value>
        /// The index of the payload length byte array.
        /// </value>
        public int PayloadLengthIndex
        {
            get { return this.payloadLengthIndex; }
            set { this.payloadLengthIndex = value; }
        }

        /// <summary>
        /// Gets or sets the socket.
        /// </summary>
        /// <value>
        /// The socket.
        /// </value>
        public Socket Socket
        {
            get { return this.socket; }
            set { this.socket = value; }
        }

        /// <summary>
        /// Gets or sets the payload text.
        /// </summary>
        /// <value>
        /// The payload text.
        /// </value>
        public StringBuilder Text
        {
            get { return this.text; }
            set { this.text = value; }
        }
    }
}