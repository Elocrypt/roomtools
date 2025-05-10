## RoomTools

**RoomTools** is a builder-oriented debug overlay that allows players with chat privileges to inspect rooms visually using commands. This mod replicates and improves on the /debug rooms functionality without requiring debug privileges.

---

### Features

* Index-based room selection
* Room listing per chunk
* Color-coded overlays for cellars, open rooms, and invalid rooms
* Automatic re-highlighting toggle

---

### Commands

| Command               | Description                                                      |
| --------------------- | -----------------------------------------------------------------|
| `/rooms show`         | Displays the room the player is inside                           |
| `/rooms hide`         | Clears the current room overlay                                  |
| `/rooms list`         | Show a list of rooms in the current chunk                        |
| `/rooms show [index]` | Highlight a specific room by list index                          |
| `.rooms auto on/off`  | Enable or disable auto-refresh of room highlight every 5 seconds |

#### Permissions
> Requires only the `chat` privilege. No debug permissions needed.

---

### Use Cases

* Validating cellar construction for cooling.
* Troubleshooting why a room doesn't count as "enclosed."
* Highlighting heat-retention areas without enabling debug mode.

---

### Installation

1. Place the compiled mod `.zip` or `/RoomTools/` folder into your `VintagestoryData/Mods` directory.
2. Make sure the server and all clients have the mod installed (it's a dual mod).
3. Launch the game and use `/rooms show` in chat!

---

## Support

If you enjoy this mod and want to support development:
**[Ko-fi.com/elocrypt](https://ko-fi.com/elocrypt)**

---