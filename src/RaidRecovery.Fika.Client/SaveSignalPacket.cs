using Fika.Core.Networking.LiteNetLib.Utils;

namespace RaidRecovery.Fika.Client
{
    /// <summary>
    /// Sent by the host when it starts reading a snapshot: every player saves their character now. Read at
    /// the same moment, an item handed from one player to another is in one snapshot only.
    /// </summary>
    public sealed class SaveSignalPacket : INetSerializable
    {
        /// <summary>Counts the snapshots of the host in this raid. Written in the log, to pair them up.</summary>
        public int Tick;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Tick);
        }

        public void Deserialize(NetDataReader reader)
        {
            Tick = reader.GetInt();
        }
    }
}
