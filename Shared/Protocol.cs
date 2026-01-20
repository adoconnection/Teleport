using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Teleport.Shared
{
    public enum Command : byte
    {
        Upload = 0x01,
        Download = 0x02,
        List = 0x03,
        Reset = 0x04,
        MkDir = 0x05
    }

    public static class Protocol
    {
        public static byte[] PackRequest(Command cmd, string slot, string path, long offset, long size, byte[] data)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms, Encoding.UTF8);

            writer.Write((byte)cmd);
            writer.Write(slot ?? "");
            writer.Write(path ?? "");
            writer.Write(offset);
            writer.Write(size);

            if (data != null && data.Length > 0)
            {
                writer.Write(data.Length);
                writer.Write(data);
            }
            else
            {
                writer.Write(0);
            }

            return ms.ToArray();
        }

        public static (Command cmd, string slot, string path, long offset, long size, byte[] data) UnpackRequest(byte[] packet)
        {
            using var ms = new MemoryStream(packet);
            using var reader = new BinaryReader(ms, Encoding.UTF8);

            var cmd = (Command)reader.ReadByte();
            var slot = reader.ReadString();
            var path = reader.ReadString();
            var offset = reader.ReadInt64();
            var size = reader.ReadInt64();
            var dataLength = reader.ReadInt32();

            byte[] data = null;
            if (dataLength > 0)
            {
                data = reader.ReadBytes(dataLength);
            }

            return (cmd, slot, path, offset, size, data);
        }

        public static byte[] PackResponse(bool success, byte[] data, string error = null)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms, Encoding.UTF8);

            writer.Write(success ? (byte)0x00 : (byte)0x01);

            if (!success && error != null)
            {
                var errorBytes = Encoding.UTF8.GetBytes(error);
                writer.Write(errorBytes.Length);
                writer.Write(errorBytes);
            }
            else if (data != null)
            {
                writer.Write(data.Length);
                writer.Write(data);
            }
            else
            {
                writer.Write(0);
            }

            return ms.ToArray();
        }

        public static (bool success, byte[] data, string error) UnpackResponse(byte[] packet)
        {
            using var ms = new MemoryStream(packet);
            using var reader = new BinaryReader(ms, Encoding.UTF8);

            var status = reader.ReadByte();
            var success = status == 0x00;
            var dataLength = reader.ReadInt32();

            if (!success && dataLength > 0)
            {
                var errorBytes = reader.ReadBytes(dataLength);
                return (false, null, Encoding.UTF8.GetString(errorBytes));
            }

            byte[] data = null;
            if (dataLength > 0)
            {
                data = reader.ReadBytes(dataLength);
            }

            return (success, data, null);
        }

        public static byte[] PackEntryList(IList<Entry> entries)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms, Encoding.UTF8);

            writer.Write(entries.Count);
            foreach (var entry in entries)
            {
                writer.Write(entry.Path);
                writer.Write(entry.IsDirectory);
                writer.Write(entry.Size);
            }

            return ms.ToArray();
        }

        public static List<Entry> UnpackEntryList(byte[] data)
        {
            var entries = new List<Entry>();

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms, Encoding.UTF8);

            var count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                entries.Add(new Entry
                {
                    Path = reader.ReadString(),
                    IsDirectory = reader.ReadBoolean(),
                    Size = reader.ReadInt64()
                });
            }

            return entries;
        }
    }
}
