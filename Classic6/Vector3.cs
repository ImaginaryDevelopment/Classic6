using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Classic6
{
    public class Vector3 : IEquatable<Vector3>
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public Vector3(float X, float Y, float Z)
        {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
        }

        public bool Equals(Vector3 other)
        {
            return other.X == this.X &&
                              other.Y == this.Y &&
                              other.Z == this.Z;
        }

        public Vector3 Clone()
        {
            return (Vector3)this.MemberwiseClone();
        }
    }
}
