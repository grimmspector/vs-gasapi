using ProtoBuf;

namespace AsphyxiaRebreathed
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ChunkGasData
    {
        public byte[] Data;
        public int chunkX, chunkY, chunkZ;
    }
}
