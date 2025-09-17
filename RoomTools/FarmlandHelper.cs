using System.Reflection;
using Vintagestory.GameContent;

namespace RoomTools
{
    public static class FarmlandHelper
    {
        private static FieldInfo roomnessField = typeof(BlockEntityFarmland).GetField("roomness", BindingFlags.NonPublic | BindingFlags.Instance);

        public static int GetRoomness(this BlockEntityFarmland be)
        {
            return roomnessField != null ? (int)roomnessField.GetValue(be) : 0;
        }
    }
}
