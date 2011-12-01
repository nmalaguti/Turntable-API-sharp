//-----------------------------------------------------------------------
// <copyright file="WebSocketEventArgs.cs" company="Nick Malaguti">
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

    /// <summary>
    /// Custom EventArgs that takes a string as data
    /// </summary>
    public class WebSocketEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WebSocketEventArgs"/> class with the specified data value.
        /// </summary>
        /// <param name="data">The event data.</param>
        public WebSocketEventArgs(string data)
        {
            this.Data = data;
        }

        /// <summary>
        /// Gets the data for the event.
        /// </summary>
        public string Data
        {
            get; private set;
        }
    }
}