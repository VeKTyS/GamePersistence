using System.Collections.Generic;
using Fika.Core.Networking.LiteNetLib.Utils;

namespace RaidRecovery.Fika.Client
{
    /// <summary>
    /// Sent by the host once it put its doors and switches back as the snapshot has them. Fika gives the
    /// doors to a player when they join, which is before: without this, they see them closed.
    /// </summary>
    public sealed class DoorsPacket : INetSerializable
    {
        /// <summary>State of each door that changed, by identifier.</summary>
        public Dictionary<string, byte> States;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(States?.Count ?? 0);
            if (States == null)
            {
                return;
            }

            foreach (var state in States)
            {
                writer.Put(state.Key);
                writer.Put(state.Value);
            }
        }

        public void Deserialize(NetDataReader reader)
        {
            var count = reader.GetInt();
            States = new Dictionary<string, byte>(count);
            for (var i = 0; i < count; i++)
            {
                var id = reader.GetString();
                States[id] = reader.GetByte();
            }
        }
    }
}
