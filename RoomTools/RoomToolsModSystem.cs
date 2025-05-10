using System.Collections.Generic;
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
        private ICoreServerAPI _sapi;
        private const int highlightId = 9999;
        private bool autoRefreshEnabled = false;
        private BlockPos lastPlayerBlockPos = new BlockPos();
        private double timeSinceLastRefresh = 0;
        private ICoreClientAPI capiRef;

        public override void Start(ICoreAPI api)
        {
            if (api.Side == EnumAppSide.Client)
                StartClientSide(api as ICoreClientAPI);
            else
                StartServerSide(api as ICoreServerAPI);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            _sapi = sapi;

            sapi.Network.RegisterChannel("roomtools")
                .RegisterMessageType<RoomOverlayMessage>();

            RegisterCommands(sapi);
        }

        private void RegisterCommands(ICoreServerAPI sapi)
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
                    .WithDescription("Highlight current room")
                    .HandleWith(HandleShowCommand)
                .EndSubCommand()

                .BeginSubCommand("hide")
                    .WithDescription("Clear highlight")
                    .HandleWith(HandleHideCommand)
                .EndSubCommand()

                .BeginSubCommand("list")
                    .WithDescription("List all rooms in your current chunk")
                    .HandleWith(HandleListCommand)
                .EndSubCommand()

                .BeginSubCommand("show")
                    .WithDescription("Highlight room by index (default: your current room)")
                    .WithArgs(_sapi.ChatCommands.Parsers.OptionalInt("roomIndex"))
                    .HandleWith(HandleShowByIndexCommand)
                .EndSubCommand();

        }

        private TextCommandResult HandleShowCommand(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player as IServerPlayer;
            var pos = player.Entity.Pos.AsBlockPos;

            ForceRoomRecalculation(pos);  // force update
            var registry = _sapi.ModLoader.GetModSystem<RoomRegistry>();
            var room = registry.GetRoomForPosition(pos);  // now fetch updated result
            if (room == null)
                return TextCommandResult.Success("No room found at your location.");

            var poses = new List<BlockPos>();
            var colors = new List<int>();

            int sizex = room.Location.X2 - room.Location.X1 + 1;
            int sizey = room.Location.Y2 - room.Location.Y1 + 1;
            int sizez = room.Location.Z2 - room.Location.Z1 + 1;

            for (int dx = 0; dx < sizex; dx++)
            {
                for (int dy = 0; dy < sizey; dy++)
                {
                    for (int dz = 0; dz < sizez; dz++)
                    {
                        int index = (dy * sizez + dz) * sizex + dx;
                        if ((room.PosInRoom[index / 8] & (1 << (index % 8))) > 0)
                        {
                            poses.Add(new BlockPos(room.Location.X1 + dx, room.Location.Y1 + dy, room.Location.Z1 + dz));
                            colors.Add(ColorUtil.ColorFromRgba(0, 180, 255, 120));
                        }
                    }
                }
            }

            _sapi.Network.GetChannel("roomtools")
                .SendPacket(new RoomOverlayMessage { Positions = poses, Colors = colors, Clear = false }, player);

            return TextCommandResult.Success("Room overlay shown.");
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

        private TextCommandResult HandleShowByIndexCommand(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player as IServerPlayer;
            var pos = player.Entity.Pos.AsBlockPos;
            var registry = _sapi.ModLoader.GetModSystem<RoomRegistry>();

            var chunkRooms = GetChunkRooms(registry, pos);
            if (chunkRooms == null || chunkRooms.Rooms.Count == 0)
                return TextCommandResult.Success("No rooms found in this chunk.");

            Room selectedRoom = null;

            if (args.Parsers[0].IsMissing)
            {
                // No index: highlight current room
                ForceRoomRecalculation(pos);
                selectedRoom = registry.GetRoomForPosition(pos);
                if (selectedRoom == null)
                    return TextCommandResult.Success("No room found at your current location.");
            }
            else
            {
                int index = (int)args[0];

                if (index < 0 || index >= chunkRooms.Rooms.Count)
                    return TextCommandResult.Success($"Invalid room index. Select between 0 and {chunkRooms.Rooms.Count - 1}.");

                selectedRoom = chunkRooms.Rooms[index];
            }

            return SendRoomHighlight(player, selectedRoom);
        }

        private TextCommandResult SendRoomHighlight(IServerPlayer player, Room room)
        {
            var poses = new List<BlockPos>();
            var colors = new List<int>();

            int sizex = room.Location.X2 - room.Location.X1 + 1;
            int sizey = room.Location.Y2 - room.Location.Y1 + 1;
            int sizez = room.Location.Z2 - room.Location.Z1 + 1;

            for (int dx = 0; dx < sizex; dx++)
            {
                for (int dy = 0; dy < sizey; dy++)
                {
                    for (int dz = 0; dz < sizez; dz++)
                    {
                        int index = (dy * sizez + dz) * sizex + dx;
                        if ((room.PosInRoom[index / 8] & (1 << (index % 8))) > 0)
                        {
                            poses.Add(new BlockPos(room.Location.X1 + dx, room.Location.Y1 + dy, room.Location.Z1 + dz));

                            int color;
                            if (room.IsSmallRoom && room.ExitCount == 0)
                            {
                                // Valid cellar [Cool Blue-Green]
                                color = ColorUtil.ColorFromRgba(0, 180, 255, 120);
                            }
                            else if (!room.IsSmallRoom && room.ExitCount == 0)
                            {
                                // Too big to be a cellar [Yellow]
                                color = ColorUtil.ColorFromRgba(255, 200, 0, 120);
                            }
                            else
                            {
                                // Has exits [Red]
                                color = ColorUtil.ColorFromRgba(255, 50, 50, 100);
                            }

                            colors.Add(color);
                        }
                    }
                }
            }

            _sapi.Network.GetChannel("roomtools")
                .SendPacket(new RoomOverlayMessage { Positions = poses, Colors = colors, Clear = false }, player);

            return TextCommandResult.Success("Room overlay shown.");
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

        private void ForceRoomRecalculation(BlockPos pos)
        {
            var registry = _sapi.ModLoader.GetModSystem<RoomRegistry>();
            var method = typeof(RoomRegistry).GetMethod(
                "FindRoomForPosition",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(BlockPos) },
                null
            );

            if (method == null)
            {
                _sapi.Logger.Error("[RoomTools] Could not reflect FindRoomForPosition.");
                return;
            }

            method.Invoke(registry, new object[] { pos });
        }

        public override void StartClientSide(ICoreClientAPI capi)
        {
            capiRef = capi;

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

            capi.Event.RegisterGameTickListener(OnClientTick, 100);
        }

        private void OnClientTick(float deltaTime)
        {
            if (!autoRefreshEnabled || capiRef == null || capiRef.World?.Player == null)
                return;

            var currentPos = capiRef.World.Player.Entity.Pos.AsBlockPos;

            if (!currentPos.Equals(lastPlayerBlockPos))
            {
                lastPlayerBlockPos = currentPos.Copy();
                timeSinceLastRefresh = 0;
                return;
            }

            timeSinceLastRefresh += deltaTime;
            if (timeSinceLastRefresh >= 5f)
            {
                capiRef.SendChatMessage("/rooms show");
                timeSinceLastRefresh = 0;
            }
        }

    }
}
