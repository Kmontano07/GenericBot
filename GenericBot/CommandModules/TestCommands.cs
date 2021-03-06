﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Discord;
using Discord.Commands;
using Discord.Net.Queue;
using Discord.WebSocket;
using GenericBot.Entities;
using LiteDB;
using Newtonsoft.Json;

namespace GenericBot.CommandModules
{
    public class TestCommands
    {
        public List<Command> GetTestCommands()
        {
            List<Command> TestCommands = new List<Command>();

            Command updateDB = new Command("updateDB");
            updateDB.Delete = false;
            updateDB.RequiredPermission = Command.PermissionLevels.GlobalAdmin;
            updateDB.ToExecute += async (client, msg, paramList) =>
            {
                await msg.GetGuild().DownloadUsersAsync();
                int newUsers = 0;

                using (var db = new LiteDatabase(GenericBot.DBConnectionString))
                {
                    var col = db.GetCollection<DBGuild>("userDatabase");
                    col.EnsureIndex(c => c.ID, true);
                    DBGuild guildDb;
                    if(col.Exists(g => g.ID.Equals(msg.GetGuild().Id)))
                        guildDb = col.FindOne(g => g.ID.Equals(msg.GetGuild().Id));
                    else guildDb = new DBGuild (msg.GetGuild().Id);
                    foreach (var user in msg.GetGuild().Users)
                    {
                        if (!guildDb.Users.Any(u => u.ID.Equals(user.Id)))
                        {
                            guildDb.Users.Add(new DBUser(user));
                            newUsers++;
                        }
                    }
                    col.Upsert(guildDb);
                    db.Dispose();
                }
                await msg.ReplyAsync($"`{newUsers}` users added to database");
            };

            TestCommands.Add(updateDB);

            Command DBStats = new Command("dbstats");
            DBStats.RequiredPermission = Command.PermissionLevels.GlobalAdmin;
            DBStats.ToExecute += async (client, msg, parameters) =>
            {
                Stopwatch stw = new Stopwatch();
                stw.Start();
                string info = "";
                using (var db = new LiteDatabase(GenericBot.DBConnectionString))
                {
                    var col = db.GetCollection<DBGuild>("userDatabase");
                    col.EnsureIndex(c => c.ID, true);
                    DBGuild guildDb;
                    if(col.Exists(g => g.ID.Equals(msg.GetGuild().Id)))
                        guildDb = col.FindOne(g => g.ID.Equals(msg.GetGuild().Id));
                    else guildDb = new DBGuild (msg.GetGuild().Id);

                    info += $"Access time: `{stw.ElapsedMilliseconds}`ms\n";
                    info += $"Registered Users: `{guildDb.Users.Count}`\n";

                    int unc = 0, nnc = 0, wnc = 0, nuc = 0;
                    foreach (var user in guildDb.Users)
                    {
                        if(user.Usernames != null && user.Usernames.Any())
                            unc += user.Usernames.Count;
                        if(user.Nicknames != null && user.Nicknames.Any())
                            nnc += user.Nicknames.Count;
                        if (user.Warnings != null && user.Warnings.Any())
                        {
                            wnc += user.Warnings.Count;
                            nuc++;
                        }
                    }

                    info += $"Stored Usernames: `{unc}`\n";
                    info += $"Stored Nicknames: `{nnc}`\n";
                    info += $"Stored Warnings:  `{wnc}`\n";
                    info += $"Users with Warnings:  `{nuc}`\n";

                    db.Dispose();
                }
                await msg.ReplyAsync(info);
            };

            TestCommands.Add(DBStats);

            return TestCommands;
        }
    }
}
