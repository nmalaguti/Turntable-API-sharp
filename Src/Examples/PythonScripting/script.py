"""
Copyright (c) Nick Malaguti.

This source code is subject to the MIT License
See http://www.opensource.org/licenses/mit-license.html
All other rights reserved.
"""

# Autobop in Python
from TurntableBotSharp import *

userId = "xxxxxxxxxxxxxxxxxxxxxxxx"
userAuth = "auth+live+xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
roomId = "xxxxxxxxxxxxxxxxxxxxxxxx"

def vote(bot, e):
    bot.Vote(TurntableBot.VoteOptions.Up, callback)

def callback(json):
    if json["success"].ToString() == "True":
        print "Voted awesome"
    else:
        print "Could not vote: %s." % (json["err"])

bot = TurntableBot(userId, userAuth, roomId)
bot.NewSongStarted += vote