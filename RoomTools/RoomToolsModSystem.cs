using System.Collections.Generic;
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
                .EndSubCommand();
        }

        private TextCommandResult HandleShowCommand(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player as IServerPlayer;
            var pos = player.Entity.Pos.AsBlockPos;

            var registry = _sapi.ModLoader.GetModSystem<RoomRegistry>();
            var room = registry.GetRoomForPosition(pos);
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

        public override void StartClientSide(ICoreClientAPI capi)
        {
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
        }
    }
}
