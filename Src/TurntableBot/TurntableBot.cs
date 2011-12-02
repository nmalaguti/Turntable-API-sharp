//-----------------------------------------------------------------------
// <copyright file="TurntableBot.cs" company="Nick Malaguti">
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
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using Newtonsoft.Json.Linq;
    using WebSocketClient;

    /// <summary>
    /// Class that implements the Turntable API. Based on https://github.com/alaingilbert/Turntable-API.
    /// </summary>
    public class TurntableBot
    {
        /// <summary>
        /// Text data sent by Turntable for heartbeats
        /// </summary>
        private const string HeartbeatSeparator = "~h~";

        /// <summary>
        /// Number of .Net ticks at the start of the Javascript epoch
        /// </summary>
        private const long InitialJavaScriptDateTicks = 621355968000000000;

        /// <summary>
        /// Separator at the beginning of text messages from Turntable to separate the message length from the message data
        /// </summary>
        private const string MessageSeparator = "~m~";

        /// <summary>
        /// Origin supplied to the WebSocket 
        /// </summary>
        private const string Origin = "http://turntable.fm";

        /// <summary>
        /// Chat server URIs used by Turntable
        /// </summary>
        private static readonly Uri[] ChatServers = new Uri[]
        {
            new Uri("ws://chat2.turntable.fm:80/socket.io/websocket"),
            new Uri("ws://chat3.turntable.fm:80/socket.io/websocket")
        };

        /// <summary>
        /// Dictionary used to store messages sent to the server that the server will eventually respond to
        /// </summary>
        private Dictionary<int, TurntableCommand> commands = new Dictionary<int, TurntableCommand>();

        /// <summary>
        /// Delegate to be called in order to join a room after connecting to a chat server 
        /// </summary>
        private Action connectDelegate;

        /// <summary>
        /// Set to <c>true</c> after authenticating with Turntable; Set to <c>false</c> when disconnecting.
        /// </summary>
        private bool isConnected = false;

        /// <summary>
        /// Used to correlate sent messages with their server responses. Monotonically increasing with each sent message.
        /// </summary>
        private int messageId = 0;

        /// <summary>
        /// Current uri of the chat server
        /// </summary>
        private Uri uri;

        /// <summary>
        /// WebSocket used to communicate with the Turntable chat server
        /// </summary>
        private WebSocket webSocket;

        /// <summary>
        /// Initializes a new instance of the <see cref="TurntableBot"/> class with the specified user ID and auth token.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="userAuth">The user's auth token.</param>
        public TurntableBot(string userId, string userAuth)
            : this(userId, userAuth, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TurntableBot"/> class with the specified user ID, auth token, and room ID.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="userAuth">The user's auth token.</param>
        /// <param name="roomId">The room ID.</param>
        public TurntableBot(string userId, string userAuth, string roomId)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot Constructor. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));

            Random random = new Random();

            this.ClientId = string.Format(CultureInfo.InvariantCulture, "{0}-{1}", (DateTime.UtcNow.Ticks - InitialJavaScriptDateTicks) / 10000, random.NextDouble());
            this.UserId = userId;
            this.UserAuth = userAuth;
            this.RoomId = roomId;

            this.CurrentSongId = null;

            this.LastHeartbeat = DateTime.UtcNow;
            this.LastActivity = DateTime.UtcNow;

            this.uri = ChatServers[this.HashMod(roomId ?? random.NextDouble().ToString(), ChatServers.Length)];

            if (roomId != null)
            {
                this.connectDelegate = delegate
                {
                    JObject json = new JObject(
                        new JProperty("api", "room.register"),
                        new JProperty("roomid", roomId));

                    this.Send(json);
                };
            }

            this.ConnectWebSocket();
        }

        /// <summary>
        /// Represents the method that will handle a Turntable event that has event data.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="TurntableBotSharp.TurntableBotEventArgs"/> instance containing the event data.</param>
        public delegate void TurntableBotEventHandler(object sender, TurntableBotEventArgs e);

        /// <summary>
        /// Occurs when the user is successfully authenticated with Turntable.
        /// </summary>
        public event EventHandler Authenticated;

        /// <summary>
        /// Occurs when the user is disconnected from Turntable.
        /// </summary>
        public event EventHandler Disconnected;

        /// <summary>
        /// Occurs when a DJ is added to the stage.
        /// </summary>
        public event TurntableBotEventHandler DJAdded;

        /// <summary>
        /// Occurs when a DJ is removed from the stage.
        /// </summary>
        public event TurntableBotEventHandler DJRemoved;

        /// <summary>
        /// Occurs when the user is kicked from Turntable.
        /// </summary>
        public event TurntableBotEventHandler KillDashNined;

        /// <summary>
        /// Occurs when a moderator is added to the room.
        /// </summary>
        public event TurntableBotEventHandler ModeratorAdded;

        /// <summary>
        /// Occurs when a moderator is removed.
        /// </summary>
        public event TurntableBotEventHandler ModeratorRemoved;

        /// <summary>
        /// Occurs when a new song starts or when there is a song playing in the room when joining.
        /// </summary>
        public event TurntableBotEventHandler NewSongStarted;

        /// <summary>
        /// Occurs when the user has connected and no room is specified.
        /// </summary>
        public event EventHandler NoRoomWhenConnected;

        /// <summary>
        /// Occurs when no song is playing in the current room.
        /// </summary>
        public event TurntableBotEventHandler NoSongPlaying;

        /// <summary>
        /// Occurs when the user joins a new room.
        /// </summary>
        public event TurntableBotEventHandler RoomChanged;

        /// <summary>
        /// Occurs when the currently playing song ends or when there is no song playing in the room when joining or when leaving the room.
        /// </summary>
        public event EventHandler SongEnded;

        /// <summary>
        /// Occurs when a user adds the playing song to their queue.
        /// </summary>
        public event TurntableBotEventHandler SongSnagged;

        /// <summary>
        /// Occurs when a user is booted from the room.
        /// </summary>
        public event TurntableBotEventHandler UserBooted;

        /// <summary>
        /// Occurs when a user leaves the room.
        /// </summary>
        public event TurntableBotEventHandler UserDeregistered;

        /// <summary>
        /// Occurs when a user joins the room.
        /// </summary>
        public event TurntableBotEventHandler UserRegistered;

        /// <summary>
        /// Occurs when a user speaks.
        /// </summary>
        public event TurntableBotEventHandler UserSpoke;

        /// <summary>
        /// Occurs when a user updates their profile.
        /// </summary>
        public event TurntableBotEventHandler UserUpdated;

        /// <summary>
        /// Occurs when a user votes.
        /// </summary>
        public event TurntableBotEventHandler VotesUpdated;

        /// <summary>
        /// The types of laptops that may be specified in ModifyLaptop
        /// </summary>
        public enum Laptop
        {
            /// <summary>
            /// A linux PC
            /// </summary>
            Linux,

            /// <summary>
            /// A Mac, iPhone, or iPad
            /// </summary>
            Mac,

            /// <summary>
            /// A Windows PC
            /// </summary>
            PC,

            /// <summary>
            /// A Chromebook
            /// </summary>
            Chrome,
        }

        /// <summary>
        /// The types of presence that can be specified in PresenceSet
        /// </summary>
        public enum Presence
        {
            /// <summary>
            /// User is available
            /// </summary>
            Available,

            /// <summary>
            /// User is idle
            /// </summary>
            Away,
        }

        /// <summary>
        /// The types of votes that may be specified in Vote
        /// </summary>
        public enum VoteOptions
        {
            /// <summary>
            /// A vote to awesome the song
            /// </summary>
            Up,

            /// <summary>
            /// A vote to lame the song
            /// </summary>
            Down,
        }

        /// <summary>
        /// Gets the client ID.
        /// </summary>
        public string ClientId
        {
            get; private set;
        }

        /// <summary>
        /// Gets the current song ID.
        /// </summary>
        public string CurrentSongId
        {
            get; private set;
        }

        /// <summary>
        /// Gets the last time the client did an activity.
        /// </summary>
        public DateTime LastActivity
        {
            get; private set;
        }

        /// <summary>
        /// Gets the last time the client responded to a heartbeat.
        /// </summary>
        public DateTime LastHeartbeat
        {
            get; private set;
        }

        /// <summary>
        /// Gets the current room ID.
        /// </summary>
        public string RoomId
        {
            get; private set;
        }

        /// <summary>
        /// Gets the user auth token.
        /// </summary>
        public string UserAuth
        {
            get; private set;
        }

        /// <summary>
        /// Gets the user ID.
        /// </summary>
        public string UserId
        {
            get; private set;
        }

        /// <summary>
        /// Computes the SHA1 hash of a string input and returns it as a hex string.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <returns>The SHA1 hash of the input as a hex string.</returns>
        public static string SHA1Hex(string input)
        {
            HashAlgorithm hashAlg = new SHA1Managed();

            byte[] hashBytes = hashAlg.ComputeHash(Encoding.ASCII.GetBytes(input));

            StringBuilder sb = new StringBuilder();
            foreach (byte b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Adds the user as a DJ if there is an open spot on stage.
        /// </summary>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void AddDJ(Action<JObject> callback)
        {
            JObject json = new JObject(
                new JProperty("api", "room.add_dj"),
                new JProperty("roomid", this.RoomId));

            this.Send(json, callback);
        }

        /// <summary>
        /// Adds the specified user as a moderator to the current room. Must be a moderator or owner of the current room.
        /// </summary>
        /// <param name="userId">The user ID of the user to add as a moderator.</param>
        public void AddModerator(string userId)
        {
            this.AddModerator(userId, null);
        }

        /// <summary>
        /// Adds the specified user as a moderator to the current room. Must be a moderator or owner of the current room.
        /// </summary>
        /// <param name="userId">The user ID of the user to add as a moderator.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void AddModerator(string userId, Action<JObject> callback)
        {
            JObject json = new JObject(
                new JProperty("api", "room.add_moderator"),
                new JProperty("roomid", this.RoomId),
                new JProperty("target_userid", userId));

            this.Send(json, callback);
        }

        /// <summary>
        /// Become a fan of the specified user.
        /// </summary>
        /// <param name="userId">The user ID to become a fan of.</param>
        public void BecomeFan(string userId)
        {
            this.BecomeFan(userId, null);
        }

        /// <summary>
        /// Become a fan of the specified user.
        /// </summary>
        /// <param name="userId">The user ID to become a fan of.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void BecomeFan(string userId, Action<JObject> callback)
        {
            JObject json = new JObject(
                new JProperty("api", "user.become_fan"),
                new JProperty("djid", userId));

            this.Send(json, callback);
        }

        /// <summary>
        /// Boots the specified user from the room. Must be a moderator or owner of the current room.
        /// </summary>
        /// <param name="userId">The user ID to boot.</param>
        /// <param name="reason">The reason they were booted.</param>
        public void BootUser(string userId, string reason)
        {
            this.BootUser(userId, reason, null);
        }

        /// <summary>
        /// Boots the specified user from the room. Must be a moderator or owner of the current room.
        /// </summary>
        /// <param name="userId">The user ID to boot.</param>
        /// <param name="reason">The reason they were booted.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void BootUser(string userId, string reason, Action<JObject> callback)
        {
            JObject json = new JObject(
                new JProperty("api", "room.boot_user"),
                new JProperty("roomid", this.RoomId),
                new JProperty("target_userid", userId),
                new JProperty("reason", reason));

            this.Send(json, callback);
        }

        /// <summary>
        /// Closes this instance.
        /// </summary>
        public void Close()
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot Public Close. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));
            this.isConnected = false;
            this.webSocket.Close();
        }

        /// <summary>
        /// Disconnects from the current chat server. Used when switching rooms.
        /// </summary>
        public void Disconnect()
        {
            this.Disconnect(true);
        }

        /// <summary>
        /// Disconnects from the current chat server. Used when switching rooms.
        /// </summary>
        /// <param name="isConnected">if set to <c>true</c> a new WebSocket will be connected to the new chat server.</param>
        public void Disconnect(bool isConnected)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot Public Disconnect. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));
            this.isConnected = isConnected;
            this.Send("disconnect");
        }

        /// <summary>
        /// Gets the profile for the specified user ID.
        /// </summary>
        /// <param name="userId">The user ID to get the profile for.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void GetProfile(string userId, Action<JObject> callback)
        {
            JObject json = new JObject(
                new JProperty("api", "user.get_profile"));

            if (!string.IsNullOrEmpty(userId))
            {
                json.Add(new JProperty("userid", userId));
            }

            this.Send(json, callback);
        }

        /// <summary>
        /// Lists the available rooms.
        /// </summary>
        /// <param name="skip">The number of rooms from the top to skip.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void ListRooms(int skip, Action<JObject> callback)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot Public ListRooms. Thread ID: {0}. Skip: {1}.", Thread.CurrentThread.ManagedThreadId, skip));

            JObject json = new JObject(
                new JProperty("api", "room.list_rooms"),
                new JProperty("skip", skip));

            this.Send(json, callback);
        }

        /// <summary>
        /// Modifies the laptop of the user.
        /// </summary>
        /// <param name="laptop">The laptop type to set.</param>
        public void ModifyLaptop(Laptop laptop)
        {
            this.ModifyLaptop(laptop, null);
        }

        /// <summary>
        /// Modifies the laptop of the user.
        /// </summary>
        /// <param name="laptop">The laptop type to set.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void ModifyLaptop(Laptop laptop, Action<JObject> callback)
        {
            JObject json = new JObject(
                new JProperty("api", "user.modify"),
                new JProperty("laptop", laptop.ToString().ToLower()));

            this.Send(json, callback);
        }

        /// <summary>
        /// Modifies the user name.
        /// </summary>
        /// <param name="name">The name to set.</param>
        public void ModifyName(string name)
        {
            this.ModifyName(name, null);
        }

        /// <summary>
        /// Modifies the user name.
        /// </summary>
        /// <param name="name">The name to set.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void ModifyName(string name, Action<JObject> callback)
        {
            JObject json = new JObject(
                new JProperty("api", "user.modify"),
                new JProperty("name", name));

            this.Send(json, callback);
        }

        /// <summary>
        /// Modifies the user's profile.
        /// </summary>
        /// <param name="profile">The profile object to apply. Any properties that are null or empty will not be applied.</param>
        public void ModifyProfile(Profile profile)
        {
            this.ModifyProfile(profile);
        }

        /// <summary>
        /// Modifies the user's profile.
        /// </summary>
        /// <param name="profile">The profile object to apply. Any properties that are null or empty will not be applied.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void ModifyProfile(Profile profile, Action<JObject> callback)
        {
            JObject json = JObject.FromObject(profile);

            if (!string.IsNullOrEmpty(profile.About))
            {
                json.Add(new JProperty("about", profile.About));
            }

            if (!string.IsNullOrEmpty(profile.About))
            {
                json.Add(new JProperty("facebook", profile.Facebook));
            }

            if (!string.IsNullOrEmpty(profile.About))
            {
                json.Add(new JProperty("hangout", profile.Hangout));
            }

            if (!string.IsNullOrEmpty(profile.About))
            {
                json.Add(new JProperty("name", profile.Name));
            }

            if (!string.IsNullOrEmpty(profile.About))
            {
                json.Add(new JProperty("topartists", profile.TopArtists));
            }

            if (!string.IsNullOrEmpty(profile.About))
            {
                json.Add(new JProperty("twitter", profile.Twitter));
            }

            if (!string.IsNullOrEmpty(profile.About))
            {
                json.Add(new JProperty("website", profile.Website));
            }

            json.Add(new JProperty("api", "user.modify_profile"));

            this.Send(json, callback);
        }

        /// <summary>
        /// Sets the user's presence.
        /// </summary>
        /// <param name="presence">The presence to set.</param>
        public void PresenceSet(Presence presence)
        {
            this.PresenceSet(presence, null);
        }

        /// <summary>
        /// Sets the user's presence.
        /// </summary>
        /// <param name="presence">The presence to set.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void PresenceSet(Presence presence, Action<JObject> callback)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot Public PresenceSet. Thread ID: {0}. Status: {1}.", Thread.CurrentThread.ManagedThreadId, presence.ToString().ToLower()));
            JObject json = new JObject(
                new JProperty("api", "presence.set"),
                new JProperty("status", presence.ToString().ToLower()));

            this.Send(json, callback);
        }

        /// <summary>
        /// Removes the specified DJ from the stage. Specify the current user ID to step down.
        /// </summary>
        /// <param name="djId">The DJ ID.</param>
        public void RemoveDJ(string djId)
        {
            this.RemoveDJ(djId, null);
        }

        /// <summary>
        /// Removes the specified DJ from the stage. Specify the current user ID to step down.
        /// </summary>
        /// <param name="djId">The DJ ID.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void RemoveDJ(string djId, Action<JObject> callback)
        {
            JObject json = new JObject(
                new JProperty("api", "room.rem_dj"),
                new JProperty("roomid", this.RoomId));

            if (!string.IsNullOrEmpty(djId))
            {
                json.Add(new JProperty("djid", djId));
            }

            this.Send(json, callback);
        }

        /// <summary>
        /// Removes the user as a fan of the specified user ID.
        /// </summary>
        /// <param name="userId">The user ID to unfan.</param>
        public void RemoveFan(string userId)
        {
            this.RemoveFan(userId, null);
        }

        /// <summary>
        /// Removes the user as a fan of the specified user ID.
        /// </summary>
        /// <param name="userId">The user ID to unfan.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void RemoveFan(string userId, Action<JObject> callback)
        {
            JObject json = new JObject(
                new JProperty("api", "user.remove_fan"),
                new JProperty("djid", userId));

            this.Send(json, callback);
        }

        /// <summary>
        /// Removes the moderator privileges of the specified user. Must be an owner of the current room.
        /// </summary>
        /// <param name="userId">The user ID to remove as a moderator.</param>
        public void RemoveModerator(string userId)
        {
            this.RemoveModerator(userId);
        }

        /// <summary>
        /// Removes the moderator privileges of the specified user. Must be an owner of the current room.
        /// </summary>
        /// <param name="userId">The user ID to remove as a moderator.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void RemoveModerator(string userId, Action<JObject> callback)
        {
            JObject json = new JObject(
                new JProperty("api", "room.rem_moderator"),
                new JProperty("roomid", this.RoomId),
                new JProperty("target_userid", userId));

            this.Send(json, callback);
        }

        /// <summary>
        /// Leaves the current room.
        /// </summary>
        public void RoomDeregister()
        {
            this.RoomDeregister(null);
        }

        /// <summary>
        /// Leaves the current room.
        /// </summary>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void RoomDeregister(Action<JObject> callback)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot Public RoomDeregister. Thread ID: {0}.", Thread.CurrentThread.ManagedThreadId));
            JObject json = new JObject(
                new JProperty("api", "room.deregister"),
                new JProperty("roomid", this.RoomId));

            this.Send(json, callback);
        }

        /// <summary>
        /// Gets info for the current room.
        /// </summary>
        public void RoomInfo()
        {
            this.RoomInfo(false, null);
        }

        /// <summary>
        /// Gets info for the current room.
        /// </summary>
        /// <param name="extended">if set to <c>true</c> gets the recently played songs in the room info.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void RoomInfo(bool extended, Action<JObject> callback)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot Public RoomInfo. Thread ID: {0}.", Thread.CurrentThread.ManagedThreadId));

            JObject json = new JObject(
                new JProperty("api", "room.info"),
                new JProperty("roomid", this.RoomId),
                new JProperty("extended", extended));

            this.Send(json, callback);
        }

        /// <summary>
        /// Gets the time in the current room.
        /// </summary>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void RoomNow(Action<JObject> callback)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot Public RoomNow. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));

            JObject json = new JObject(
                new JProperty("api", "room.now"));

            this.Send(json, callback);
        }

        /// <summary>
        /// Joins the specified room.
        /// </summary>
        /// <param name="roomId">The room ID to join.</param>
        public void RoomRegister(string roomId)
        {
            Uri uri = ChatServers[this.HashMod(roomId, ChatServers.Length)];

            this.RoomRegister(roomId, uri, null);
        }

        /// <summary>
        /// Joins the specified room on the specified chatserver.
        /// </summary>
        /// <param name="roomId">The room ID to join.</param>
        /// <param name="chatServer">The chat server the room is on.</param>
        /// <param name="port">The port of the chat server.</param>
        public void RoomRegister(string roomId, string chatServer, int port)
        {
            Uri uri = new Uri(string.Format(CultureInfo.InvariantCulture, "ws://{0}:{1}/socket.io/websocket", chatServer, port));

            this.RoomRegister(roomId, uri, null);
        }

        /// <summary>
        /// Joins the specified room on the specified chatserver URI.
        /// </summary>
        /// <param name="roomId">The room ID to join.</param>
        /// <param name="chatServerUri">The URI of the chat server that the room is on.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void RoomRegister(string roomId, Uri chatServerUri, Action<JObject> callback)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot Public RoomRegister. Thread ID: {0}. Room Id: {1}. Current Uri: {2}. Uri: {3}.", Thread.CurrentThread.ManagedThreadId, roomId, this.uri, chatServerUri));

            this.connectDelegate = delegate
            {
                JObject json = new JObject(
                    new JProperty("api", "room.register"),
                    new JProperty("roomid", roomId));

                this.Send(json, callback);
            };

            if (chatServerUri != this.uri)
            {
                this.uri = chatServerUri;
                this.Disconnect(true);
            }
            else
            {
                this.connectDelegate();
            }
        }

        /// <summary>
        /// Sets the avatar for the user.
        /// </summary>
        /// <param name="avatarId">The avatar ID.</param>
        public void SetAvatar(int avatarId)
        {
            this.SetAvatar(avatarId, null);
        }

        /// <summary>
        /// Sets the avatar for the user.
        /// </summary>
        /// <param name="avatarId">The avatar ID.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void SetAvatar(int avatarId, Action<JObject> callback)
        {
            JObject json = new JObject(
                new JProperty("api", "user.set_avatar"),
                new JProperty("avatarid", avatarId));

            this.Send(json, callback);
        }

        /// <summary>
        /// Speaks the specified message.
        /// </summary>
        /// <param name="message">The message to speak.</param>
        public void Speak(string message)
        {
            this.Speak(message, null);
        }

        /// <summary>
        /// Speaks the specified message.
        /// </summary>
        /// <param name="message">The message to speak.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void Speak(string message, Action<JObject> callback)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot Public Speak. Thread ID: {0}. Message: {1}.", Thread.CurrentThread.ManagedThreadId, message));
            JObject json = new JObject(
                new JProperty("api", "room.speak"),
                new JProperty("roomid", this.RoomId),
                new JProperty("text", message));

            this.Send(json, callback);
        }

        /// <summary>
        /// Skips the user's currently playing song.
        /// </summary>
        public void StopSong()
        {
            this.StopSong(null);
        }

        /// <summary>
        /// Skips the user's currently playing song.
        /// </summary>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void StopSong(Action<JObject> callback)
        {
            JObject json = new JObject(
                new JProperty("api", "room.stop_song"),
                new JProperty("roomid", this.RoomId));

            this.Send(json, callback);
        }

        /// <summary>
        /// Authenticates the user to Turntable.
        /// </summary>
        public void UserAuthenticate()
        {
            this.UserAuthenticate(null);
        }

        /// <summary>
        /// Authenticates the user to Turntable.
        /// </summary>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void UserAuthenticate(Action<JObject> callback)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot Public UserAuthenticate. Thread ID: {0}.", Thread.CurrentThread.ManagedThreadId));

            JObject json = new JObject(
                new JProperty("api", "user.authenticate"));

            this.Send(json, callback);
        }

        /// <summary>
        /// Gets the user info for the user.
        /// </summary>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void UserInfo(Action<JObject> callback)
        {
            JObject json = new JObject(
                new JProperty("api", "user.info"));

            this.Send(json, callback);
        }

        /// <summary>
        /// Votes with the specified value.
        /// </summary>
        /// <param name="value">The value to vote with.</param>
        public void Vote(VoteOptions value)
        {
            this.Vote(value, null);
        }

        /// <summary>
        /// Votes with the specified value.
        /// </summary>
        /// <param name="value">The value to vote with.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        public void Vote(VoteOptions value, Action<JObject> callback)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot Public Vote. Thread ID: {0}. Value: {1}.", Thread.CurrentThread.ManagedThreadId, value.ToString()));

            Random random = new Random();

            string vh = SHA1Hex(string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}", this.RoomId, value.ToString().ToLower(), this.CurrentSongId));
            string th = SHA1Hex(random.NextDouble().ToString());
            string ph = SHA1Hex(random.NextDouble().ToString());

            JObject json = new JObject(
                new JProperty("api", "room.vote"),
                new JProperty("roomid", this.RoomId),
                new JProperty("val", value.ToString().ToLower()),
                new JProperty("vh", vh),
                new JProperty("th", th),
                new JProperty("ph", ph));

            this.Send(json, callback);
        }

        /// <summary>
        /// Called when the client has successfully authenticated with Turntable.
        /// </summary>
        protected void OnAuthenticated()
        {
            if (this.Authenticated != null)
            {
                this.Authenticated(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Called when the client is disconnected and will not reconnect.
        /// </summary>
        protected void OnDisconnected()
        {
            if (this.Disconnected != null)
            {
                this.Disconnected(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Called when a DJ is added to the stage.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnDJAdded(JObject json)
        {
            if (this.DJAdded != null)
            {
                this.DJAdded(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when a DJ is removed from the stage.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnDJRemoved(JObject json)
        {
            if (this.DJRemoved != null)
            {
                this.DJRemoved(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when the user is kicked from Turntable.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnKillDashNined(JObject json)
        {
            if (this.KillDashNined != null)
            {
                this.KillDashNined(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when a moderator is added to the room.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnModeratorAdded(JObject json)
        {
            if (this.ModeratorAdded != null)
            {
                this.ModeratorAdded(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when a moderator removed.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnModeratorRemoved(JObject json)
        {
            if (this.ModeratorRemoved != null)
            {
                this.ModeratorRemoved(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when a new song is started.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnNewSongStarted(JObject json)
        {
            if (this.NewSongStarted != null)
            {
                this.NewSongStarted(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when the client connects and no room is specified.
        /// </summary>
        protected void OnNoRoomWhenConnected()
        {
            if (this.NoRoomWhenConnected != null)
            {
                this.NoRoomWhenConnected(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Called when no song is playing in the current room.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnNoSongPlaying(JObject json)
        {
            if (this.NoSongPlaying != null)
            {
                this.NoSongPlaying(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when the user joins a new room.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnRoomChanged(JObject json)
        {
            if (this.RoomChanged != null)
            {
                this.RoomChanged(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when the currently playing song ends or if there is no song in the room.
        /// </summary>
        protected void OnSongEnded()
        {
            if (this.SongEnded != null)
            {
                this.SongEnded(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Called when a user adds the playing song to their queue..
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnSongSnagged(JObject json)
        {
            if (this.SongSnagged != null)
            {
                this.SongSnagged(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when a user is booted from the room.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnUserBooted(JObject json)
        {
            if (this.UserBooted != null)
            {
                this.UserBooted(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when a user leaves the room.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnUserDeregistered(JObject json)
        {
            if (this.UserDeregistered != null)
            {
                this.UserDeregistered(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when a user joins the room.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnUserRegistered(JObject json)
        {
            if (this.UserRegistered != null)
            {
                this.UserRegistered(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when a user speaks.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnUserSpoke(JObject json)
        {
            if (this.UserSpoke != null)
            {
                this.UserSpoke(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when a user updates their profile.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnUserUpdated(JObject json)
        {
            if (this.UserUpdated != null)
            {
                this.UserUpdated(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Called when a user votes.
        /// </summary>
        /// <param name="json">The json object supplied by the server.</param>
        protected void OnVotesUpdated(JObject json)
        {
            if (this.VotesUpdated != null)
            {
                this.VotesUpdated(this, new TurntableBotEventArgs(json));
            }
        }

        /// <summary>
        /// Creates a WebSocket and connects to the current chat server.
        /// </summary>
        private void ConnectWebSocket()
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot ConnectWebSocket. Creating new WebSocket and connecting. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));
            this.webSocket = new WebSocket(this.uri, Origin);
            this.webSocket.MessageReceived += new WebSocket.WebSocketEventHandler(this.MessageReceived);
            this.webSocket.Closed += new EventHandler(this.WebsocketClosed);
            this.webSocket.Connect();
        }

        /// <summary>
        /// The hash function to choose which chat server to use
        /// </summary>
        /// <param name="roomId">The room ID to join.</param>
        /// <param name="length">The number of available chat servers to choose from.</param>
        /// <returns>An int between 0 and length - 1</returns>
        private int HashMod(string roomId, int length)
        {
            string d = SHA1Hex(roomId);
            int c = 0;

            for (int i = 0; i < d.Length; i++)
            {
                c += d[i];
            }

            return c % length;
        }

        /// <summary>
        /// Listens to the WebSocket message received event.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="WebSocketClient.WebSocketEventArgs"/> instance containing the event data.</param>
        private void MessageReceived(object sender, WebSocketEventArgs e)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot OnMessage. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));

            WebSocket ws = (WebSocket)sender;
            string data = this.ParseRawTurntableMessage(e.Data);

            if (data.StartsWith(HeartbeatSeparator, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot OnMessage Heartbeat. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));
                this.LastHeartbeat = DateTime.UtcNow;
                this.PresenceSet(Presence.Available);
                this.Send(data);
            }
            else if (data.Equals("no_session", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot OnMessage No Session. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));

                if (!this.isConnected)
                {
                    this.UserAuthenticate();
                }

                this.PresenceSet(
                    Presence.Available,
                    delegate(JObject delegateJson)
                {
                    if (this.connectDelegate != null)
                    {
                        Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot OnMessage Connecting to room. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));
                        this.connectDelegate();
                    }
                    else
                    {
                        Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot OnMessage Not in a room. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));
                        this.OnNoRoomWhenConnected();
                    }
                });
            }
            else
            {
                this.LastActivity = DateTime.UtcNow;

                JObject json = JObject.Parse(data);

                int msgId = (int?)json["msgid"] ?? -1;

                if (this.commands.ContainsKey(msgId))
                {
                    string api = (string)this.commands[msgId].Message["api"] ?? string.Empty;

                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot OnMessage Received response to sent message. Thread ID: {0}. Message ID: {1}. Api: {2}.", Thread.CurrentThread.ManagedThreadId, msgId, api));

                    if (api.Equals("user.authenticate", StringComparison.OrdinalIgnoreCase))
                    {
                        if ((bool?)json["success"] ?? false)
                        {
                            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot OnMessage Received Authentication success. Thread ID: {0}.", Thread.CurrentThread.ManagedThreadId));
                            this.isConnected = true;
                            this.OnAuthenticated();
                        }
                        else
                        {
                            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot OnMessage Received Authentication failed. Thread ID: {0}.", Thread.CurrentThread.ManagedThreadId));
                            this.Disconnect(false);
                            return;
                        }
                    }
                    else if (api.Equals("room.info", StringComparison.OrdinalIgnoreCase))
                    {
                        if ((bool?)json["success"] ?? false)
                        {
                            JObject currentSong = null;
                            try
                            {
                                currentSong = (JObject)json["room"]["metadata"]["current_song"];
                            }
                            catch (NullReferenceException)
                            {
                            }
                            catch (InvalidCastException)
                            {
                            }

                            if (currentSong != null)
                            {
                                this.CurrentSongId = (string)currentSong["_id"];
                            }
                        }
                    }
                    else if (api.Equals("room.register", StringComparison.OrdinalIgnoreCase))
                    {
                        if ((bool?)json["success"] ?? false)
                        {
                            this.RoomId = (string)this.commands[msgId].Message["roomid"];
                            this.RoomInfo(
                                false,
                                delegate(JObject delegateJson)
                            {
                                this.OnRoomChanged(delegateJson);
                                this.OnNewSongStarted(delegateJson);
                            });
                        }
                    }
                    else if (api.Equals("room.deregister", StringComparison.OrdinalIgnoreCase))
                    {
                        if ((bool?)json["success"] ?? false)
                        {
                            this.RoomId = null;
                            this.CurrentSongId = null;
                            this.OnSongEnded();
                        }
                    }

                    if (this.commands[msgId].Callback != null)
                    {
                        this.commands[msgId].Callback(json);
                    }

                    this.commands.Remove(msgId);
                }

                string command = (string)json["command"] ?? string.Empty;

                if (!string.IsNullOrEmpty(command))
                {
                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot OnMessage Command received. Thread ID: {0}. Command: {1}", Thread.CurrentThread.ManagedThreadId, command));

                    if (command.Equals("killdashnine", StringComparison.OrdinalIgnoreCase))
                    {
                        this.Disconnect(false);
                        this.OnKillDashNined(json);
                    }
                    else if (command.Equals("registered", StringComparison.OrdinalIgnoreCase))
                    {
                        this.OnUserRegistered(json);
                    }
                    else if (command.Equals("deregistered", StringComparison.OrdinalIgnoreCase))
                    {
                        this.OnUserDeregistered(json);
                    }
                    else if (command.Equals("speak", StringComparison.OrdinalIgnoreCase))
                    {
                        this.OnUserSpoke(json);
                    }
                    else if (command.Equals("nosong", StringComparison.OrdinalIgnoreCase))
                    {
                        this.CurrentSongId = null;
                        this.OnSongEnded();
                        this.OnNoSongPlaying(json);
                    }
                    else if (command.Equals("newsong", StringComparison.OrdinalIgnoreCase))
                    {
                        if (this.CurrentSongId != null)
                        {
                            this.OnSongEnded();
                        }

                        this.CurrentSongId = (string)json["room"]["metadata"]["current_song"]["_id"];
                        this.OnNewSongStarted(json);
                    }
                    else if (command.Equals("update_votes", StringComparison.OrdinalIgnoreCase))
                    {
                        this.OnVotesUpdated(json);
                    }
                    else if (command.Equals("booted_user", StringComparison.OrdinalIgnoreCase))
                    {
                        this.OnUserBooted(json);
                    }
                    else if (command.Equals("update_user", StringComparison.OrdinalIgnoreCase))
                    {
                        this.OnUserUpdated(json);
                    }
                    else if (command.Equals("add_dj", StringComparison.OrdinalIgnoreCase))
                    {
                        this.OnDJAdded(json);
                    }
                    else if (command.Equals("rem_dj", StringComparison.OrdinalIgnoreCase))
                    {
                        this.OnDJRemoved(json);
                    }
                    else if (command.Equals("new_moderator", StringComparison.OrdinalIgnoreCase))
                    {
                        this.OnModeratorAdded(json);
                    }
                    else if (command.Equals("rem_moderator", StringComparison.OrdinalIgnoreCase))
                    {
                        this.OnModeratorRemoved(json);
                    }
                    else if (command.Equals("snagged", StringComparison.OrdinalIgnoreCase))
                    {
                        this.OnSongSnagged(json);
                    }
                    else
                    {
                        Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}: {1}", command, json));
                    }
                }

                if (msgId > 0)
                {
                    if (json["success"] != null && !(bool)json["success"])
                    {
                        Debug.WriteLine(json);
                    }
                }

                if (json["err"] != null)
                {
                    Debug.WriteLine(json);
                }
            }
        }

        /// <summary>
        /// Parses the raw turntable message. Removes the length and message separators from the beginning of the message.
        /// </summary>
        /// <param name="rawMessage">The raw message.</param>
        /// <returns>The data portion of the message</returns>
        private string ParseRawTurntableMessage(string rawMessage)
        {
            int length = 0;
            int endOfLength = 0;

            if (rawMessage.StartsWith(MessageSeparator))
            {
                endOfLength = rawMessage.IndexOf(MessageSeparator, MessageSeparator.Length);
                length = int.Parse(rawMessage.Substring(MessageSeparator.Length, endOfLength - MessageSeparator.Length));
            }

            return rawMessage.Substring(endOfLength + MessageSeparator.Length, length);
        }

        /// <summary>
        /// Sends the specified message.
        /// </summary>
        /// <param name="message">The message to send.</param>
        private void Send(JObject message)
        {
            this.Send(message, null);
        }

        /// <summary>
        /// Sends the specified message. Adds authentication information to the message.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="callback">The callback delegate to invoke on the server response.</param>
        private void Send(JObject message, Action<JObject> callback)
        {
            int messageId = this.messageId++;

            message.Add("msgid", messageId);
            message.Add("clientid", this.ClientId);
            message.Add("userid", this.UserId);
            message.Add("userauth", this.UserAuth);

            this.commands.Add(messageId, new TurntableCommand(message, callback));

            this.Send(message.ToString());
        }

        /// <summary>
        /// Sends the specified message. Adds message length to the front of the message.
        /// </summary>
        /// <param name="message">The message to send.</param>
        private void Send(string message)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot Send string message. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));
            this.webSocket.Send(string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}{3}", MessageSeparator, message.Length, MessageSeparator, message));
        }

        /// <summary>
        /// Listens to the WebSocket closed event.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void WebsocketClosed(object sender, EventArgs e)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot OnWebsocketClose Called. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));

            WebSocket ws = (WebSocket)sender;

            if (this.isConnected)
            {
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot OnWebsocketClose is connected. We should reconnect. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));
                if (this.webSocket != null && !this.webSocket.IsConnected)
                {
                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot OnWebsocketClose this.webSocket isn't null and isn't connected anymore. Time to make a new connection. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));
                    this.ConnectWebSocket();
                }
            }
            else
            {
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "TurntableBot OnWebsocketClose is not connected. We shouldn't reconnect. Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));
                this.OnDisconnected();
            }
        }
    }
}