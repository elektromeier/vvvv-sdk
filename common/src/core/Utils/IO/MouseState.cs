﻿using System;

namespace VVVV.Utils.IO
{
    public enum MouseButton
    {
        None,
        Left,
        Middle,
        Right
    }

    public struct MouseState : IEquatable<MouseState>
    {
        public double X;
        public double Y;
        public MouseButton Button;
        public int MouseWheel;
        
        public MouseState(double x, double y, MouseButton button, int mouseWheel)
        {
            X = x;
            Y = y;
            Button = button;
            MouseWheel = mouseWheel;
        }
        
        #region Equals and GetHashCode implementation
        // The code in this region is useful if you want to use this structure in collections.
        // If you don't need it, you can just remove the region and the ": IEquatable<MouseEvent>" declaration.
        
        public override bool Equals(object obj)
        {
            if (obj is MouseState)
                return Equals((MouseState)obj); // use Equals method below
            else
                return false;
        }
        
        public bool Equals(MouseState other)
        {
            // add comparisions for all members here
            return this.X == other.X && this.Y == other.Y && this.Button == other.Button && this.MouseWheel == other.MouseWheel;
        }
        
        public override int GetHashCode()
        {
            // combine the hash codes of all members here (e.g. with XOR operator ^)
            return X.GetHashCode() ^ Y.GetHashCode() ^ Button.GetHashCode() ^ MouseWheel.GetHashCode();
        }
        
        public static bool operator ==(MouseState left, MouseState right)
        {
            return left.Equals(right);
        }
        
        public static bool operator !=(MouseState left, MouseState right)
        {
            return !left.Equals(right);
        }
        #endregion
    }
}