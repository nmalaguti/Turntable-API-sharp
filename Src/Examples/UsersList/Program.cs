//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Nick Malaguti">
//   Copyright (c) Nick Malaguti.
// </copyright>
// <license>
//   This source code is subject to the MIT License
//   See http://www.opensource.org/licenses/mit-license.html
//   All other rights reserved.
// </license>
//-----------------------------------------------------------------------

namespace UsersList
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json.Linq;
    using TurntableBotSharp;

    /// <summary>
    /// Example program that will keep a list of users in a room.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main method.
        /// </summary>
        /// <param name="args">Command line args.</param>
        public static void Main(string[] args)
        {
            string userId = "xxxxxxxxxxxxxxxxxxxxxxxx";
            string auth = "auth+live+xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";
            string roomId = "xxxxxxxxxxxxxxxxxxxxxxxx";

            Dictionary<string, JObject> usersList = new Dictionary<string, JObject>();

            TurntableBot bot = new TurntableBot(userId, auth, roomId);

            bot.RoomChanged += new TurntableBot.TurntableBotEventHandler(delegate(object sender, TurntableBotEventArgs e)
                {
                    JObject json = e.Json;
                    
                    // reset the users list
                    usersList = new Dictionary<string, JObject>();
                    JArray users = (JArray)json["users"];

                    foreach (JObject user in users)
                    {
                        usersList[(string)user["userid"]] = user;
                    }
                });

            bot.UserRegistered += new TurntableBot.TurntableBotEventHandler(delegate(object sender, TurntableBotEventArgs e)
                {
                    JObject json = e.Json;

                    JObject user = (JObject)json["user"][0];
                    usersList[(string)user["userid"]] = user;
                });

            bot.UserDeregistered += new TurntableBot.TurntableBotEventHandler(delegate(object sender, TurntableBotEventArgs e)
                {
                    JObject json = e.Json;

                    JObject user = (JObject)json["user"][0];
                    usersList.Remove((string)user["userid"]);
                });

            Console.ReadLine();
        }
    }
}