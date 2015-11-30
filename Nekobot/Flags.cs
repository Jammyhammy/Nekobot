﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Threading.Tasks;
using Discord;
using Nekobot.Commands.Permissions.Levels;

namespace Nekobot
{
    class Flags
    {
        internal static bool GetIgnoredFlag(Channel chan, User user)
        {
            return GetIgnoredFlag(chan) || GetIgnoredFlag(user);
        }

        internal static bool GetIgnoredFlag(User user)
        {
            return GetIgnoredFlag("user", "users", user.Id);
        }

        internal static bool GetIgnoredFlag(Channel chan)
        {
            return GetIgnoredFlag("channel", "flags", chan.Id);
        }

        internal static bool GetIgnoredFlag(string row, string table, long id)
        {
            SQLiteDataReader reader = Program.ExecuteReader($"select ignored from {table} where {row} = '{id}'");
            while (reader.Read())
                if (int.Parse(reader["ignored"].ToString()) == 1)
                    return true;
            return false;
        }

        internal static async Task SetIgnoredFlag(string row, string table, long id, string insertdata, char symbol, string reply, Action<string> setreply)
        {
            bool in_table = Program.ExecuteScalarPos($"select count({row}) from {table} where {row}='{id}'");
            bool isIgnored = in_table && GetIgnoredFlag(row, table, id);
            await Program.ExecuteNonQueryAsync(in_table
                ? $"update {table} set ignored={Convert.ToInt32(!isIgnored)} where {row}='{id}'"
                : $"insert into {table} values ('{id}'{insertdata})");
            if (reply != "")
                reply += '\n';
            reply += $"<{symbol}{id}> is " + (isIgnored ? "now" : "no longer") + " ignored.";
            setreply(reply);
        }

        internal static bool GetMusicFlag(User user)
        {
            SQLiteDataReader reader = Program.ExecuteReader("select channel from flags where music = 1");
            List<long> streams = new List<long>();
            while (reader.Read())
                streams.Add(Convert.ToInt64(reader["channel"].ToString()));
            return user.VoiceChannel != null && streams.Contains(user.VoiceChannel.Id);
        }

        internal static bool GetNsfwFlag(Channel chan)
        {
            SQLiteDataReader reader = Program.ExecuteReader("select nsfw from flags where channel = '" + chan.Id + "'");
            while (reader.Read())
                if (int.Parse(reader["nsfw"].ToString()) == 1)
                    return true;
            return false;
        }

        internal static void AddCommands(Commands.CommandGroupBuilder group)
        {
            group.CreateCommand("nsfw status")
                .Alias("canlewd status")
                .Description("I'll tell you if this channel allows nsfw commands.")
                .Do(async e =>
                {
                    bool nsfw = Flags.GetNsfwFlag(e.Channel);
                    if (nsfw)
                        await Program.client.SendMessage(e.Channel, "This channel allows nsfw commands.");
                    else
                        await Program.client.SendMessage(e.Channel, "This channel doesn't allow nsfw commands.");
                });

            // Moderator Commands
            group.CreateCommand("nsfw")
                .Alias("canlewd")
                .Parameter("on/off", Commands.ParameterType.Required)
                .MinPermissions(1)
                .Description("I'll set a channel's nsfw flag to on or off.")
                .Do(async e =>
                {
                    bool on = e.Args[0] == "on";
                    bool off = !on && e.Args[0] == "off";
                    if (on || off)
                    {
                        bool nsfw = Flags.GetNsfwFlag(e.Channel);
                        string status = on ? "allow" : "disallow";
                        if (nsfw == on || nsfw != off)
                            await Program.client.SendMessage(e.Channel, $"<@{e.User.Id}>, this channel is already {status}ing nsfw commands.");
                        else
                        {
                            await Program.ExecuteNonQueryAsync(off ? $"update flags set nsfw=0 where channel='{e.Channel.Id}'"
                                : Program.ExecuteScalarPos($"select count(channel) from flags where channel='{e.Channel.Id}'")
                                ? $"update flags set nsfw=1 where channel='{e.Channel.Id}'"
                                : $"insert into flags values ('{e.Channel.Id}', 1, 0, 0)");
                            await Program.client.SendMessage(e.Channel, $"I've set this channel to {status} nsfw commands.");
                        }
                    }
                    else await Program.client.SendMessage(e.Channel, $"<@{e.User.Id}>, '{String.Join(" ", e.Args)}' isn't a valid argument. Please use on or off instead.");
                });

            // Administrator Commands
            group.CreateCommand("ignore")
                .Parameter("channel", Commands.ParameterType.Optional)
                .Parameter("user", Commands.ParameterType.Optional)
                .Parameter("...", Commands.ParameterType.Multiple)
                .MinPermissions(3)
                .Description("I'll ignore commands coming from a particular channel or user")
                .Do(async e =>
                {
                    if (e.Message.MentionedChannels.Count() > 0 || e.Message.MentionedUsers.Count() > 0)
                    {
                        string reply = "";
                        Action<string> setreply = x => reply = x;
                        foreach (Channel c in e.Message.MentionedChannels)
                            await Flags.SetIgnoredFlag("channel", "flags", c.Id, "0, 0, 1", '#', reply, setreply);
                        foreach (User u in e.Message.MentionedUsers)
                            await Flags.SetIgnoredFlag("user", "users", u.Id, ", 0, 1", '@', reply, setreply);
                        await Program.client.SendMessage(e.Channel, reply);
                    }
                    else
                    {
                        await Program.client.SendMessage(e.Channel, "You need to mention at least one user or channel!");
                    }
                });
        }
    }
}