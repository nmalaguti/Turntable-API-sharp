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

namespace AutoBop
{
    using System;
    using Newtonsoft.Json.Linq;
    using TurntableBotSharp;

    /// <summary>
    /// Example program that will AutoBop to every new song.
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

            TurntableBot bot = new TurntableBot(userId, auth, roomId);

            bot.NewSongStarted += new TurntableBot.TurntableBotEventHandler(delegate(object sender, TurntableBotEventArgs e)
                {
                    JObject json = e.Json;
                    bot.Vote(
                        TurntableBot.VoteOptions.Up,
                        delegate(JObject responseJson)
                    {
                        if ((bool?)responseJson["success"] ?? false)
                        {
                            string artist = (string)json["room"]["metadata"]["current_song"]["metadata"]["artist"];
                            string song = (string)json["room"]["metadata"]["current_song"]["metadata"]["song"];

                            Console.WriteLine(string.Format("Voted Awesome for {0} - {1}.", artist, song));
                        }
                        else
                        {
                            string err = (string)responseJson["err"] ?? "unknown reason";
                            Console.WriteLine(string.Format("Could not vote: {0}.", err));
                        }
                    });
                });

            Console.ReadLine();
        }
    }
}