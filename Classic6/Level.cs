using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Classic6
{
    public class Level
    {
        public short Width { get; private set; }
        public short Depth { get; private set; }
        public short Height { get; private set; }
        public byte[] Data { get; private set; }
        public Vector3 Spawn { get; set; }

        public Level(short Width, short Depth, short Height)
        {
            this.Width = Width;
            this.Depth = Depth;
            this.Height = Height;
            Data = new byte[Width * Depth * Height];

            // Generate level
            for (int x = 0; x < Width; x++)
                for (int z = 0; z < Depth; z++)
                    for (int y = 0; y < 32; y++) // Why did I order these like this? O_o
                        SetBlock(new Vector3(x, y, z), 1);
            for (int x = 0; x < Width; x++)
                for (int z = 0; z < Depth; z++)
                    SetBlock(new Vector3(x, 32, z), 2);
            Spawn = new Vector3(32, 34, 32);
        }

        public void SetBlock(Vector3 position, byte value)
        {
            Data[(int)position.Z + ((int)position.X * Height) + ((int)position.Y * Height * Depth)] = value;
        }

        public byte GetBlock(Vector3 position)
        {
            return Data[(int)position.Z + ((int)position.X * Height) + ((int)position.Y * Height * Depth)];
        }
    }
}
