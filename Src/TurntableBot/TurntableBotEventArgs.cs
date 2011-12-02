//-----------------------------------------------------------------------
// <copyright file="TurntableBotEventArgs.cs" company="Nick Malaguti">
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
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// Custom EventArgs that takes a <see cref="JObject"/> as data
    /// </summary>
    public class TurntableBotEventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TurntableBotEventArgs"/> class.
        /// </summary>
        /// <param name="json">The event json.</param>
        public TurntableBotEventArgs(JObject json)
        {
            this.Json = json;
        }

        /// <summary>
        /// Gets the json for the event.
        /// </summary>
        public JObject Json
        {
            get; private set;
        }
    }
}