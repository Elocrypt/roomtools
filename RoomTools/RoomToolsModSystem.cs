using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace RoomTools
{
    public class RoomToolsModSystem : ModSystem
    {
        private Dictionary<BlockPos, int> lastOverlayCache = new Dictionary<BlockPos, int>();
        private BlockPos lastPlayerBlockPos;
        private ICoreServerAPI _sapi;
        private ICoreClientAPI _capi;
        private const int highlightId = 9999;
        private bool autoRefreshEnabled = false;
        private double timeSinceLastRefresh = 0;

        #region Entry Points

        public override void Start(ICoreAPI api)
        {
            if (api.Side == EnumAppSide.Client)
                StartClientSide(api as ICoreClientAPI);
            else
                StartServerSide(api as ICoreServerAPI);
        }

        public override void StartClientSide(ICoreClientAPI capi)
        {
            _capi = capi;

            capi.Network.RegisterChannel("roomtools")
                .RegisterMessageType<RoomOverlayMessage>()
                .SetMessageHandler<RoomOverlayMessage>(msg =>
                {
                    if (msg.Clear)
                    {
                        capi.World.HighlightBlocks(capi.World.Player, highlightId, new List<BlockPos>(), new List<int>());
                    }
                    else
                    {
                        capi.World.HighlightBlocks(capi.World.Player, highlightId, msg.Positions, msg.Colors);
                    }
                });
            RegisterClientCommands(capi);
            capi.Event.RegisterGameTickListener(OnClientTick, 100);
        }
        public override void StartServerSide(ICoreServerAPI sapi)
        {
            _sapi = sapi;

            sapi.Network.RegisterChannel("roomtools")
                .RegisterMessageType<RoomOverlayMessage>();

            RegisterServerCommands(sapi);
        }

        #endregion

        #region Commands

        private void RegisterClientCommands(ICoreClientAPI capi)
        {
            if (capi.ChatCommands.Get("rooms") != null)
            {
                capi.Logger.Warning("[RoomTools] Command '.rooms' already registered, skipping.");
                return;
            }

            capi.ChatCommands.Create("rooms")
                .BeginSubCommand("auto")
                .WithArgs(capi.ChatCommands.Parsers.OptionalWord("state"))
                .HandleWith(args =>
                {
                    string state = args[0]?.ToString().ToLowerInvariant() ?? "";
                    if (state == "on")
                    {
                        autoRefreshEnabled = true;
                        capi.ShowChatMessage("Room auto-refresh enabled.");
                    }
                    else if (state == "off")
                    {
                        autoRefreshEnabled = false;
                        capi.ShowChatMessage("Room auto-refresh disabled.");
                    }
                    else
                    {
                        capi.ShowChatMessage("Usage: /rooms auto on|off");
                    }
                    return TextCommandResult.Success();
                })
                .EndSubCommand();
        }


        private void RegisterServerCommands(ICoreServerAPI sapi)
        {
            if (sapi.ChatCommands.Get("rooms") != null)
            {
                sapi.Logger.Warning("[RoomTools] Command '/rooms' already registered, skipping.");
                return;
            }

            sapi.ChatCommands.Create("rooms")
                .WithDescription("Toggle room debug overlay")
                .RequiresPrivilege(Privilege.chat)

                .BeginSubCommand("show")
                    .WithDescription("Highlight current room or by index")
                    .WithArgs(_sapi.ChatCommands.Parsers.OptionalInt("roomIndex"))
                    .HandleWith(HandleShowCommand)
                .EndSubCommand()

                .BeginSubCommand("hide")
                    .WithDescription("Clear highlight")
                    .HandleWith(HandleHideCommand)
                .EndSubCommand()

                .BeginSubCommand("list")
                    .WithDescription("List all rooms in your current chunk")
                    .HandleWith(HandleListCommand)
                .EndSubCommand();
        }

        private TextCommandResult HandleShowCommand(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player as IServerPlayer;
            var pos = player.Entity.Pos.AsBlockPos;
            var registry = _sapi.ModLoader.GetModSystem<RoomRegistry>();

            // Force recalculation for up-to-date info
            Room recalculated = ForceRoomRecalculationAndReturn(pos);
            var chunkRooms = GetChunkRooms(registry, pos);
            if (chunkRooms == null || chunkRooms.Rooms.Count == 0)
                return TextCommandResult.Success("No rooms found in this chunk.");

            Room selectedRoom = null;

            if (args.Parsers[0].IsMissing)
            {
                // Try to get updated room at player location
                selectedRoom = TryFindContainingRoom(registry, pos);
                if (selectedRoom == null)
                    return TextCommandResult.Success("No valid room found at your current location.");
            }
            else
            {
                int index = (int)args[0];
                if (index < 0 || index >= chunkRooms.Rooms.Count)
                    return TextCommandResult.Success($"Invalid room index. Select between 0 and {chunkRooms.Rooms.Count - 1}.");

                selectedRoom = chunkRooms.Rooms[index];
            }
            if (selectedRoom.ExitCount > 0)
            {
                player.SendMessage(0, "[RoomTools] Room has exits — highlighting problem areas.", EnumChatType.Notification);
            }
            return SendRoomHighlight(player, selectedRoom);
        }

        private TextCommandResult HandleHideCommand(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player as IServerPlayer;

            _sapi.Network.GetChannel("roomtools")
                .SendPacket(new RoomOverlayMessage { Positions = new List<BlockPos>(), Colors = new List<int>(), Clear = true }, player);

            return TextCommandResult.Success("Room overlay hidden.");
        }

        private TextCommandResult HandleListCommand(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player as IServerPlayer;
            var pos = player.Entity.Pos.AsBlockPos;
            var registry = _sapi.ModLoader.GetModSystem<RoomRegistry>();

            var chunkRooms = GetChunkRooms(registry, pos);
            if (chunkRooms == null || chunkRooms.Rooms.Count == 0)
                return TextCommandResult.Success("No rooms found in this chunk.");

            string result = $"Found {chunkRooms.Rooms.Count} rooms in this chunk:\n";

            for (int i = 0; i < chunkRooms.Rooms.Count; i++)
            {
                var room = chunkRooms.Rooms[i];
                var loc = room.Location;
                int sx = loc.X2 - loc.X1 + 1;
                int sy = loc.Y2 - loc.Y1 + 1;
                int sz = loc.Z2 - loc.Z1 + 1;

                bool enclosed = room.ExitCount == 0;
                string marker = enclosed ? "OK" : "X";
                string type = room.IsSmallRoom ? "Cellar" : "Room";
                string exits = room.ExitCount == 0 ? "enclosed OK" : $"open ({room.ExitCount} exits) X";
                result += $"[{i}] {sx}x{sy}x{sz} {type}, {exits}\n";

            }

            return TextCommandResult.Success(result);
        }

        private TextCommandResult SendRoomHighlight(IServerPlayer player, Room room)
        {
            _sapi.Network.GetChannel("roomtools")
                .SendPacket(new RoomOverlayMessage {
                    Positions = new List<BlockPos>(), Colors = new List<int>(), Clear = true
                }, player);

            _sapi.World.RegisterCallback((_) => {
                ActuallySendRoomHighlight(player, room);
            }, 100);

            return TextCommandResult.Success("Room overlay shown.");
        }

        #endregion

        #region Utility

        private void ActuallySendRoomHighlight(IServerPlayer player, Room room)
        {
            _sapi.Logger.Notification($"[RoomTools] Room bounds: {room.Location}, exits: {room.ExitCount}, skylights: {room.SkylightCount}, non-skylights: {room.NonSkylightCount}");

            var poses = new List<BlockPos>();
            var colors = new List<int>();

            var ba = _sapi.World.BlockAccessor;
            bool isEnclosed = room.ExitCount == 0;
            bool isGreenhouse = room.SkylightCount > room.NonSkylightCount;

            int sizex = room.Location.X2 - room.Location.X1 + 1;
            int sizey = room.Location.Y2 - room.Location.Y1 + 1;
            int sizez = room.Location.Z2 - room.Location.Z1 + 1;
            /*
                        if (isEnclosed)
                        {
                            for (int dx = 0; dx < sizex; dx++)
                                for (int dy = 0; dy < sizey; dy++)
                                    for (int dz = 0; dz < sizez; dz++)
                                    {
                                        int index = (dy * sizez + dz) * sizex + dx;
                                        if ((room.PosInRoom[index / 8] & (1 << (index % 8))) == 0) continue;

                                        var pos = new BlockPos(room.Location.X1 + dx, room.Location.Y1 + dy, room.Location.Z1 + dz);
                                        var block = ba.GetBlock(pos);
                                    }
                        }*/

            _sapi.Logger.Notification($"[RoomTools] Greenhouse detected in room: {isGreenhouse}");
            lastOverlayCache.Clear(); // Clear cache before a full room rescan

            for (int dx = 0; dx < sizex; dx++)
                for (int dy = 0; dy < sizey; dy++)
                    for (int dz = 0; dz < sizez; dz++)
                    {
                        int index = (dy * sizex * sizez) + (dz * sizex) + dx;
                        if ((room.PosInRoom[index / 8] & (1 << (index % 8))) == 0) continue;

                        var pos = new BlockPos(room.Location.X1 + dx, room.Location.Y1 + dy, room.Location.Z1 + dz);
                        var block = ba.GetBlock(pos, BlockLayersAccess.Default);
                        var fluid = ba.GetBlock(pos, BlockLayersAccess.Fluid);
                        var solid = ba.GetBlock(pos, BlockLayersAccess.Solid);

                        bool isSeal = IsLikelyRoomSeal(solid);
                        bool isHole = !isSeal && (
                            block?.CollisionBoxes == null ||
                            block.CollisionBoxes.Length == 0 ||
                            block.Code.Path.Contains("slab") ||
                            block.BlockMaterial == EnumBlockMaterial.Plant ||
                            block.IsLiquid()
                        );
                        bool isPartial = !isSeal && !isHole;

                        int color = 0;

                        if (!isEnclosed)
                        {
                            if (isHole)
                            {
                                color = block.Code.Path.Contains("glass") || block.Code.Path.Contains("trapdoor")
                                    ? ColorUtil.ColorFromRgba(255, 180, 0, 120) // orange
                                    : ColorUtil.ColorFromRgba(255, 0, 0, 180);   // red
                            }
                            else if (isPartial)
                            {
                                color = ColorUtil.ColorFromRgba(180, 180, 180, 120); // gray
                            }
                        }
                        else if (isGreenhouse)
                        {
                            color = ColorUtil.ColorFromRgba(50, 255, 100, 120); // green
                        }
                        else
                        {
                            color = room.IsSmallRoom
                                ? ColorUtil.ColorFromRgba(0, 180, 255, 120)   // blue
                                : ColorUtil.ColorFromRgba(255, 200, 0, 120);  // yellow
                        }

                        // Cache optimization (Q3)
                        int hash = color.GetHashCode();
                        if (lastOverlayCache.TryGetValue(pos, out int oldHash) && oldHash == hash)
                            continue;

                        lastOverlayCache[pos] = hash;

                        poses.Add(pos);
                        colors.Add(color);

                        if (isHole || isPartial)
                        {
                            _sapi.Logger.Notification($"[RoomTools] Problem block at {pos} — {block?.Code}?");
                        }
                    }



            _sapi.Network.GetChannel("roomtools")
                .SendPacket(new RoomOverlayMessage
                {
                    Positions = poses,
                    Colors = colors,
                    Clear = false
                }, player);
        }

        private ChunkRooms GetChunkRooms(RoomRegistry registry, BlockPos pos)
        {
            FieldInfo field = typeof(RoomRegistry).GetField("roomsByChunkIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = field?.GetValue(registry) as Dictionary<long, ChunkRooms>;

            int chunkX = pos.X / 32;
            int chunkY = pos.Y / 32;
            int chunkZ = pos.Z / 32;
            int sizeX = _sapi.World.BlockAccessor.MapSizeX / 32;
            int sizeZ = _sapi.World.BlockAccessor.MapSizeZ / 32;
            long index3D = MapUtil.Index3dL(chunkX, chunkY, chunkZ, sizeX, sizeZ);

            if (dict != null && dict.TryGetValue(index3D, out var chunkRooms))
                return chunkRooms;

            return null;
        }
        private Room ForceRoomRecalculationAndReturn(BlockPos pos)
        {
            var registry = _sapi.ModLoader.GetModSystem<RoomRegistry>();
            var method = typeof(RoomRegistry).GetMethod(
                "FindRoomForPosition",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(BlockPos), typeof(ChunkRooms) },
                null
            );

            if (method == null)
            {
                _sapi.Logger.Error("[RoomTools] Could not reflect FindRoomForPosition.");
                return null;
            }

            var chunkRooms = GetChunkRooms(registry, pos);
            if (chunkRooms == null)
            {
                _sapi.Logger.Warning("[RoomTools] Cannot recalculate room: chunkRooms was null.");
                return null;
            }

            return (Room)method.Invoke(registry, new object[] { pos, chunkRooms });
        }


        private Room TryFindContainingRoom(RoomRegistry registry, BlockPos origin)
        {
            // Force recalculation for the current position
            ForceRoomRecalculationAndReturn(origin);

            List<Room> candidates = new();
            BlockPos tempPos = origin.Copy();

            // 1. Vertical scan
            for (int dy = 0; dy <= 5; dy++)
            {
                try
                {
                    Room testRoom = registry.GetRoomForPosition(tempPos);
                    if (testRoom != null && IsRoomValid(testRoom))
                        candidates.Add(testRoom);
                }
                catch { }
                tempPos.Up();
            }

            // 2. 3x3x3 proximity scan
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = 0; dy <= 2; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        BlockPos checkPos = origin.AddCopy(dx, dy, dz);
                        try
                        {
                            Room testRoom = registry.GetRoomForPosition(checkPos) ?? FindRoomByLocation(registry, checkPos);
                            if (testRoom != null && IsRoomValid(testRoom))
                                candidates.Add(testRoom);
                        }
                        catch { }
                    }

            // 3. Choose best room
            Room best = null;
            int bestScore = -1;

            foreach (Room room in candidates)
            {
                if (room?.Location == null) continue;

                int sizeX = room.Location.X2 - room.Location.X1 + 1;
                int sizeY = room.Location.Y2 - room.Location.Y1 + 1;
                int sizeZ = room.Location.Z2 - room.Location.Z1 + 1;
                if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0) continue;

                int enclosureBonus = room.ExitCount == 0 ? 10000 : 0;
                int skylightBonus = room.SkylightCount > room.NonSkylightCount ? 1000 : 0;
                int volume = sizeX * sizeY * sizeZ;
                int score = enclosureBonus + skylightBonus + volume;

                if (score > bestScore)
                {
                    best = room;
                    bestScore = score;
                }
            }

            return best;
        }

        private Room FindRoomByLocation(RoomRegistry registry, BlockPos pos)
        {
            var chunkRooms = GetChunkRooms(registry, pos);
            if (chunkRooms == null) return null;

            foreach (var room in chunkRooms.Rooms)
            {
                if (room.Location?.Contains(pos) == true)
                    return room;
            }

            return null;
        }

        private bool IsRoomValid(Room room)
        {
            if (room?.Location == null) return false;
            int sx = room.Location.X2 - room.Location.X1 + 1;
            int sy = room.Location.Y2 - room.Location.Y1 + 1;
            int sz = room.Location.Z2 - room.Location.Z1 + 1;
            return !(sx == 1 && sy == 1 && sz == 1);
        }
        private bool IsLikelyRoomSeal(Block block)
        {
            if (block == null) return false;

            string code = block.Code?.Path ?? "";

            // Allowed types even if replaceable
            bool allowedByPath = code.Contains("glass") ||
                                  code.Contains("trapdoor") ||
                                  code.Contains("bars") ||
                                  code.Contains("grate");

            return
                block.Replaceable < 6000 &&
                !block.IsLiquid() &&
                block.BlockMaterial != EnumBlockMaterial.Air &&
                (block.SideSolid[0] || block.SideSolid[1] || block.SideSolid[2] ||
                 block.SideSolid[3] || block.SideSolid[4] || block.SideSolid[5] || allowedByPath);
        }

        private void OnClientTick(float deltaTime)
        {
            if (!autoRefreshEnabled || _capi == null || _capi.World?.Player == null)
                return;

            var currentPos = _capi.World.Player.Entity.Pos.AsBlockPos;

            if (!currentPos.Equals(lastPlayerBlockPos))
            {
                lastPlayerBlockPos = currentPos.Copy();
                timeSinceLastRefresh = 0;
                return;
            }

            timeSinceLastRefresh += deltaTime;
            if (timeSinceLastRefresh >= 5f)
            {
                _capi.SendChatMessage("/rooms show");
                timeSinceLastRefresh = 0;
            }
        }
        #endregion
    }
}