using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Serilog;

namespace MinecraftProximity
{
    static class NetworkLog
    {
        public enum Operation
        {
            EMPTY = 0,

            CREATE_LOBBY,
            CREATED_LOBBY,
            GET_LOBBY_CREATE_TRANSACTION,

            CONNECT_LOBBY,
            CONNECTED_LOBBY,

            DISCONNECT_LOBBY,
            DISCONNECTED_LOBBY,

            //

            CONNECT_NETWORK,
            DISCONNECT_NETWORK,
            OPEN_NETWORK_CHANNEL,

            RUN_CALLBACKS,
            FLUSH_NETWORK,
            DISPOSE,

            //

            SEND_MESSAGE,
            RECEIVE_MESSAGE,
            USER_CONNECT,
            USER_DISCONNECT
        }

        [Flags]
        public enum StateFlags
        {
            RAN_CALLBACKS = 1,
            FLUSHED_NETWORK = 2
        }

        public struct Entry
        {
            public Operation op;
            public long userId;
            public long lobbyId;
            public byte channelId;
            public long timestamp;
            public StateFlags flags;
            public int callbackCycle;
            public int flushNetworkCycle;
        }

        public static Entry[] entries = new Entry[4096];
        public static int divider = 0;
        public static bool hasHandledError = false;
        public static StateFlags state = 0;

        public static void Log(Entry entry)
        {
            if (entries == null)
                return;

            if (entry.op == Operation.FLUSH_NETWORK)
            {
                state |= StateFlags.FLUSHED_NETWORK;
                return;
            }
            if (entry.op == Operation.RUN_CALLBACKS)
            {
                state |= StateFlags.RAN_CALLBACKS;
                return;
            }

            entry.flags = state;
            state = 0;

            entry.timestamp = Environment.TickCount64;
            entry.flushNetworkCycle = Program.discord.flushNetworkCycle;
            entry.callbackCycle = Program.discord.callbackCycle;

            entries[divider] = entry;
            divider = (divider + 1) % entries.Length;
        }

        public static void Dump(bool errorCaused)
        {
            if (errorCaused && hasHandledError)
            {
                Serilog.Log.Information("Not writing error again.");
                return;
            }

            hasHandledError = false;

            if (entries == null)
                return;
            hasHandledError = true;

            string path = "";
            for (int i = 0; i < 5000; i++)
            {
                path = $"networkDump{i}.txt";
                if (!File.Exists(path))
                    break;
            }

            using FileStream fs = File.OpenWrite(path);
            using StreamWriter w = new StreamWriter(fs);

            for (int i = divider; i < entries.Length; i++)
                if (entries[i].op != Operation.EMPTY)
                    w.WriteLine(DumpLine(ref entries[i]));

            for (int i = 0; i < divider; i++)
                if (entries[i].op != Operation.EMPTY)
                    w.WriteLine(DumpLine(ref entries[i]));

            string finalState = $"[{state}:  {Program.discord.callbackCycle}, {Program.discord.flushNetworkCycle}]";

            if (errorCaused)
                w.WriteLine($"ERROR [{state}]");
            else
                w.WriteLine($"DUMP [{state}]");

            Serilog.Log.Information($"Wrote log to {path}");
        }

        public static string DumpLine(ref Entry entry)
        {
            long t = entry.timestamp - Program.discord.startedTimestamp;

            string flags = "";
            if ((entry.flags & StateFlags.RAN_CALLBACKS) != 0)
                flags += "C";
            else
                flags += " ";

            if ((entry.flags & StateFlags.FLUSHED_NETWORK) != 0)
                flags += "N";
            else
                flags += " ";

            StringBuilder builtup = new StringBuilder($"{t / 1000.0f,7:0.000} {entry.op,-20} [{flags}: {entry.callbackCycle,5}, {entry.flushNetworkCycle,5}]");
            if (entry.lobbyId != 0)
                builtup.Append($" | {entry.lobbyId % 1000:000}");
            
            if (entry.op == Operation.OPEN_NETWORK_CHANNEL || entry.op == Operation.RECEIVE_MESSAGE
                || entry.op == Operation.SEND_MESSAGE)
                builtup.Append($" [{entry.channelId}]");

            if (entry.userId != 0)
                builtup.Append($" {entry.userId % 1000:000}");

            return builtup.ToString();
        }
    }
}
