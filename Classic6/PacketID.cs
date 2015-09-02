using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Classic6
{
    public enum PacketID
    {
        // Two way
        Identification = 0x00,
        PositionAndOrientation = 0x08,
        ChatMessage = 0x0D,

        // Client to server
        ClientSetBlock = 0x05,

        // Server to client
        Ping = 0x01,
        LevelInitialize = 0x02,
        LevelDataChunk = 0x03,
        LevelFinalize = 0x04,
        ServerSetBlock = 0x06,
        SpawnPlayer = 0x07,
        DespawnPlayer = 0x0C,
        DisconnectPlayer = 0x0E,
        UpdatePlayerType = 0x0F,
    }
}
