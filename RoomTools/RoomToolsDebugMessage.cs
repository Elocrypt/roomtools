using ProtoBuf;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RoomTools
{
    [ProtoContract]
    public class RoomOverlayMessage
    {
        [ProtoMember(1)]
        public List<BlockPos> Positions { get; set; }

        [ProtoMember(2)]
        public List<int> Colors { get; set; }

        [ProtoMember(3)]
        public bool Clear { get; set; }
    }
}
