﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Net.Queue;
using Discord.Rest;
using Discord.WebSocket;
using GenericBot.Entities;
using LiteDB;

namespace GenericBot.CommandModules
{
    public class ModCommands
    {
        public List<Command> GetModCommands()
        {
            List<Command> ModCommands = new List<Command>();

            Command clear = new Command("clear");
            clear.Description = "Clear a number of messages from a channel";
            clear.Usage = "clear <number> <user>";
            clear.RequiredPermission = Command.PermissionLevels.Moderator;
            clear.ToExecute += async (client, msg, paramList) =>
            {
                if (paramList.Empty())
                {
                    await msg.ReplyAsync("You gotta tell me how many messages to delete!");
                    return;
                }

                int count;
                if (int.TryParse(paramList[0], out count))
                {
                    List<IMessage> msgs = (msg.Channel as SocketTextChannel).GetManyMessages(count).Result;
                    if (msg.GetMentionedUsers().Any())
                    {
                        var users = msg.GetMentionedUsers();
                        msgs = msgs.Where(m => users.Select(u => u.Id).Contains(m.Author.Id)).ToList();
                        msgs.Add(msg);
                    }
                    if (paramList.Count > 1 && !msg.GetMentionedUsers().Any())
                    {
                        await msg.ReplyAsync($"It looks like you're trying to mention someone but failed.");
                        return;
                    }

                    await msg.Channel.DeleteMessagesAsync(msgs.Where(m => DateTime.Now - m.CreatedAt < TimeSpan.FromDays(14)));

                    var messagesSent = new List<IMessage>();

                    messagesSent.Add(msg.ReplyAsync($"{msg.Author.Mention}, done deleting those messages!").Result);
                    if (msgs.Any(m => DateTime.Now - m.CreatedAt > TimeSpan.FromDays(14)))
                    {
                        messagesSent.Add(msg.ReplyAsync($"I couldn't delete all of them, some were older than 2 weeks old :frowning:").Result);
                    }

                    await Task.Delay(2500);
                    await msg.Channel.DeleteMessagesAsync(messagesSent);
                }
                else
                {
                    await msg.ReplyAsync("That's not a valid number");
                }
            };

            Command whois = new Command("whois");
            whois.Description = "Get information about a user";
            whois.Usage = "whois @user";
            whois.RequiredPermission = Command.PermissionLevels.Moderator;
            whois.ToExecute += async (client, msg, parameters) =>
            {
                await msg.GetGuild().DownloadUsersAsync();
                if (!msg.GetMentionedUsers().Any())
                {
                    await msg.ReplyAsync("No user found");
                    return;
                }
                SocketGuildUser user;
                ulong uid = msg.GetMentionedUsers().FirstOrDefault().Id;

                if (msg.GetGuild().Users.Any(u => u.Id == uid))
                {
                    user = msg.GetGuild().GetUser(uid);
                }
                else
                {
                    await msg.ReplyAsync("User not found");
                    return;
                }

                string nickname = msg.GetGuild().Users.All(u => u.Id != uid) || string.IsNullOrEmpty(user.Nickname) ? "None" : user.Nickname;
                string roles = "";
                foreach (var role in user.Roles.Where(r => !r.Name.Equals("@everyone")).OrderByDescending(r => r.Position))
                {
                    roles += $"`{role.Name}`, ";
                }
                DBUser dbUser;
                using (var db = new LiteDatabase(GenericBot.DBConnectionString))
                {
                    var col = db.GetCollection<DBGuild>("userDatabase");
                    col.EnsureIndex(c => c.ID, true);
                    DBGuild guildDb;
                    if(col.Exists(g => g.ID.Equals(msg.GetGuild().Id)))
                        guildDb = col.FindOne(g => g.ID.Equals(msg.GetGuild().Id));
                    else guildDb = new DBGuild (msg.GetGuild().Id);
                    if (guildDb.Users.Any(u => u.ID.Equals(user.Id))) // if already exists
                    {
                        dbUser = guildDb.Users.First(u => u.ID.Equals(user.Id));
                    }
                    else
                    {
                        dbUser = new DBUser(user);
                        col.Upsert(guildDb);
                    }
                    db.Dispose();
                }

                string uns = "";
                string nns = "";
                foreach (var s in dbUser.Usernames.Distinct())
                {
                    uns += $"`{s}`, ";
                }
                uns = uns.Trim(',');
                if (!dbUser.Nicknames.Empty())
                {
                    foreach (var s in dbUser.Nicknames.Distinct())
                    {

                    }
                }

                string info =  $"User Id:  `{user.Id}`\n";
                info += $"Username: `{user.ToString()}`\n";
                info += $"Past Usernames: `{dbUser.Usernames.Distinct().ToList().reJoin(", ")}`\n";
                info += $"Nickname: `{nickname}`\n";
                if(!dbUser.Nicknames.Empty())
                    info += $"Past Nicknames: `{dbUser.Nicknames.Distinct().ToList().reJoin(", ")}`\n";
                info += $"Created At: `{string.Format("{0:yyyy-MM-dd HH\\:mm\\:ss zzzz}", user.CreatedAt.LocalDateTime)}GMT` " +
                        $"(about {(DateTime.UtcNow - user.CreatedAt).Days} days ago)\n";
                if (user.JoinedAt.HasValue)
                    info +=
                        $"Joined At: `{string.Format("{0:yyyy-MM-dd HH\\:mm\\:ss zzzz}", user.JoinedAt.Value.LocalDateTime)}GMT`" +
                        $"(about {(DateTime.UtcNow - user.JoinedAt.Value).Days} days ago)\n";
                info += $"Roles: {roles.Trim(' ', ',')}\n";
                if(dbUser.Warnings.Any())
                    info += $"`{dbUser.Warnings.Count}` Warnings: {dbUser.Warnings.reJoin(" | ")}";


                foreach (var str in info.SplitSafe(','))
                {
                    await msg.ReplyAsync(str.TrimStart(','));
                }

            };

            ModCommands.Add(whois);

            Command archive = new Command("archive");
            archive.RequiredPermission = Command.PermissionLevels.Admin;
            archive.Description = "Save all the messages from a text channel";
            archive.ToExecute += async (client, msg, parameters) =>
            {
                var msgs = (msg.Channel as SocketTextChannel).GetManyMessages(50000).Result;

                var channel = msg.Channel;
                string str = $"{((IGuildChannel) channel).Guild.Name} | {((IGuildChannel) channel).Guild.Id}\n";
                str += $"#{channel.Name} | {channel.Id}\n";
                str += $"{DateTime.Now}\n\n";

                IMessage lastMsg = null;
                msgs.Reverse();
                msgs.Remove(msg);
                foreach (var m in msgs)
                {
                    string msgstr = "";
                    if(lastMsg != null && m.Author.Id != lastMsg.Author.Id) msgstr += $"{m.Author} | {m.Author.Id}\n";
                    if (lastMsg != null && m.Author.Id != lastMsg.Author.Id) msgstr += $"{m.Timestamp}\n";
                    msgstr += $"{m.Content}\n";
                    foreach (var a in m.Attachments)
                    {
                        msgstr += $"{a.Url}\n";
                    }
                    str += msgstr + "\n";
                    lastMsg = m;
                    await Task.Yield();
                }

                string filename = $"{channel.Name}.txt";
                File.WriteAllText("files/" + filename, str);
                await msg.Channel.SendFileAsync("files/" + filename, $"Here you go! I saved {msgs.Count()} messages");
            };

            ModCommands.Add(archive);

            ModCommands.Add(clear);

            return ModCommands;
        }
    }
}
