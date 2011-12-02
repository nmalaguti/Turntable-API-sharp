//-----------------------------------------------------------------------
// <copyright file="TurntableCommand.cs" company="Nick Malaguti">
//   Copyright (c) Nick Malaguti.
// </copyright>
// <license>
//   This source code is subject to the MIT License
//   See http://www.opensource.org/licenses/mit-license.html
//   All other rights reserved.
// </license>
//-----------------------------------------------------------------------

namespace TurntableBotSharp
{
    using System;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// Class used to store messages sent to the server that the server will eventually respond to
    /// </summary>
    internal class TurntableCommand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TurntableCommand"/> class.
        /// </summary>
        /// <param name="message">The message sent to the server.</param>
        /// <param name="callback">The callback to invoke when the server response is received.</param>
        public TurntableCommand(JObject message, Action<JObject> callback)
        {
            this.Message = message;
            this.Callback = callback;
        }

        /// <summary>
        /// Gets the callback to invoke when the server response is received.
        /// </summary>
        public Action<JObject> Callback
        {
            get; private set;
        }

        /// <summary>
        /// Gets the message sent to the server.
        /// </summary>
        public JObject Message
        {
            get; private set;
        }
    }
}