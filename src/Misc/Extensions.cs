using SkiaSharp;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace eft_dma_radar.Common.Misc
{
    /// <summary>
    /// Extension methods go here.
    /// </summary>
    public static class Extensions
    {
        #region Generic Extensions

        /// <summary>
        /// Contains Cached Data for Enum Extension(s) defined in this class.
        /// </summary>
        private static class EnumData<TEnum>
            where TEnum : Enum
        {
            /// <summary>
            /// Cached Descriptions of Enum Values.
            /// </summary>
            public static readonly ConcurrentDictionary<TEnum, string> Descriptions = new();
        }

        /// <summary>
        /// Returns the Description Value of an Enum.
        /// </summary>
        /// <typeparam name="TEnum">Enum Type.</typeparam>
        /// <param name="value">Enum value to get description of.</param>
        /// <returns>Description of the Enum Value.</returns>
        public static string GetDescription<TEnum>(this TEnum value)
            where TEnum : Enum
        {
            return EnumData<TEnum>.Descriptions.GetOrAdd(value,
                static key =>
                {
                    string name = key.ToString();
                    var field = key.GetType().GetField(name);

                    if (field is not null)
                    {
                        var attribute = (DescriptionAttribute)Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute));

                        if (attribute is not null)
                            return attribute.Description;
                    }
                    return name; // Return the .ToString() value as a fallback.
                });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FindUtf16NullTerminatorIndex(this ReadOnlySpan<byte> span)
        {
            for (int i = 0; i < span.Length - 1; i += 2)
            {
                if (span[i] == 0 && span[i + 1] == 0)
                {
                    return i;
                }
            }
            return -1; // Not found
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FindUtf16NullTerminatorIndex(this Span<byte> span)
        {
            for (int i = 0; i < span.Length - 1; i += 2)
            {
                if (span[i] == 0 && span[i + 1] == 0)
                {
                    return i;
                }
            }
            return -1; // Not found
        }
        /// <summary>
        /// Restarts a timer from 0. (Timer will be started if not already running)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Restart(this System.Timers.Timer t)
        {
            t.Stop();
            t.Start();
        }

        /// <summary>
        /// Converts 'Degrees' to 'Radians'.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToRadians(this float degrees) =>
            MathF.PI / 180f * degrees;
        /// <summary>
        /// Converts 'Radians' to 'Degrees'.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToDegrees(this float radians) =>
            180f / MathF.PI * radians;
        /// <summary>
        /// Converts 'Radians' to 'Degrees'.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ToDegrees(this Vector2 radians) =>
            180f / MathF.PI * radians;
        /// <summary>
        /// Converts 'Radians' to 'Degrees'.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToDegrees(this Vector3 radians) =>
            180f / MathF.PI * radians;
        /// <summary>
        /// Converts 'Degrees' to 'Radians'.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ToRadians(this Vector2 degrees) =>
            MathF.PI / 180f * degrees;
        /// <summary>
        /// Converts 'Degrees' to 'Radians'.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToRadians(this Vector3 degrees) =>
            MathF.PI / 180f * degrees;

        /// <summary>
        /// Normalize angular degrees to 0-360.
        /// </summary>
        /// <param name="angle">Angle (degrees).</param>
        /// <returns>Normalized angle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float NormalizeAngle(this float angle)
        {
            float modAngle = angle % 360.0f;

            if (modAngle < 0.0f)
                return modAngle + 360.0f;
            return modAngle;
        }
        /// <summary>
        /// Normalize angular degrees to 0-360.
        /// </summary>
        /// <param name="angle">Angle (degrees).</param>
        /// <returns>Normalized angle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 NormalizeAngles(this Vector3 angles)
        {
            angles.X = angles.X.NormalizeAngle();
            angles.Y = angles.Y.NormalizeAngle();
            angles.Z = angles.Z.NormalizeAngle();
            return angles;
        }
        /// <summary>
        /// Normalize angular degrees to 0-360.
        /// </summary>
        /// <param name="angle">Angle (degrees).</param>
        /// <returns>Normalized angle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 NormalizeAngles(this Vector2 angles)
        {
            angles.X = angles.X.NormalizeAngle();
            angles.Y = angles.Y.NormalizeAngle();
            return angles;
        }

        /// <summary>
        /// Custom implemenation to check if a float value is valid.
        /// This is the same as float.IsNormal() except it accepts 0 as a valid value.
        /// </summary>
        /// <param name="f">Float value to validate.</param>
        /// <returns>True if valid, otherwise False if invalid.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool IsNormalOrZero(this float f)
        {
            int bits = *(int*)&f & 0x7FFFFFFF; // Clears the sign bit
            return bits == 0 || (bits >= 0x00800000 && bits < 0x7F800000); // Allow 0, normal values, but not subnormal, infinity, or NaN
        }

        /// <summary>
        /// Checks if a Vector2 is valid.
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNormal(this Vector2 v)
        {
            return float.IsNormal(v.X) && float.IsNormal(v.Y);
        }

        /// <summary>
        /// Checks if a Vector3 is valid.
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNormal(this Vector3 v)
        {
            return float.IsNormal(v.X) && float.IsNormal(v.Y) && float.IsNormal(v.Z);
        }

        /// <summary>
        /// Checks if a Quaternion is valid.
        /// </summary>
        /// <param name="q"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNormal(this Quaternion q)
        {
            return float.IsNormal(q.X) && float.IsNormal(q.Y) && float.IsNormal(q.Z) && float.IsNormal(q.W);
        }

        /// <summary>
        /// Checks if a Vector2 is valid or Zero.
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNormalOrZero(this Vector2 v)
        {
            return v.X.IsNormalOrZero() && v.Y.IsNormalOrZero();
        }

        /// <summary>
        /// Checks if a Vector3 is valid or Zero.
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNormalOrZero(this Vector3 v)
        {
            return v.X.IsNormalOrZero() && v.Y.IsNormalOrZero() && v.Z.IsNormalOrZero();
        }

        /// <summary>
        /// Checks if a Quaternion is valid or Zero.
        /// </summary>
        /// <param name="q"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNormalOrZero(this Quaternion q)
        {
            return q.X.IsNormalOrZero() && q.Y.IsNormalOrZero() && q.Z.IsNormalOrZero() && q.W.IsNormalOrZero();
        }

        /// <summary>
        /// Validates a float for invalid values.
        /// </summary>
        /// <param name="q">Input Float.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormal(this float f)
        {
            if (!float.IsNormal(f))
                throw new ArgumentOutOfRangeException(nameof(f));
        }

        /// <summary>
        /// Validates a Quaternion for invalid values.
        /// </summary>
        /// <param name="q">Input Quaternion.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormal(this Quaternion q)
        {
            if (!q.IsNormal())
                throw new ArgumentOutOfRangeException(nameof(q));
        }
        /// <summary>
        /// Validates a Vector3 for invalid values.
        /// </summary>
        /// <param name="v">Input Vector3.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormal(this Vector3 v)
        {
            if (!v.IsNormal())
                throw new ArgumentOutOfRangeException(nameof(v));
        }
        /// <summary>
        /// Validates a Vector2 for invalid values.
        /// </summary>
        /// <param name="v">Input Vector2.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormal(this Vector2 v)
        {
            if (!v.IsNormal())
                throw new ArgumentOutOfRangeException(nameof(v));
        }

        /// <summary>
        /// Validates a float for invalid values.
        /// </summary>
        /// <param name="q">Input Float.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormalAndNotZero(this float f)
        {
            if (!f.IsNormalOrZero())
                throw new ArgumentOutOfRangeException(nameof(f));
        }

        /// <summary>
        /// Validates a Quaternion for invalid values.
        /// </summary>
        /// <param name="q">Input Quaternion.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormalAndNotZero(this Quaternion q)
        {
            if (!q.IsNormalOrZero())
                throw new ArgumentOutOfRangeException(nameof(q));
        }
        /// <summary>
        /// Validates a Vector3 for invalid values.
        /// </summary>
        /// <param name="v">Input Vector3.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormalAndNotZero(this Vector3 v)
        {
            if (!v.IsNormalOrZero())
                throw new ArgumentOutOfRangeException(nameof(v));
        }
        /// <summary>
        /// Validates a Vector2 for invalid values.
        /// </summary>
        /// <param name="v">Input Vector2.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormalAndNotZero(this Vector2 v)
        {
            if (!v.IsNormalOrZero())
                throw new ArgumentOutOfRangeException(nameof(v));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 AsVector2(this SKPoint point) =>
            Unsafe.BitCast<SKPoint, Vector2>(point);

        #endregion

        #region Memory Extensions

        /// <summary>
        /// Checks if a Virtual Address is valid.
        /// </summary>
        /// <param name="va">Virtual Address to validate.</param>
        /// <returns>True if valid, otherwise False.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidVirtualAddress(this ulong va) =>
            Utils.IsValidVirtualAddress(va);

        /// <summary>
        /// Throws an exception if the Virtual Address is invalid.
        /// </summary>
        /// <param name="va">Virtual address to validate.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfInvalidVirtualAddress(this ulong va)
        {
            if (!Utils.IsValidVirtualAddress(va))
                throw new ArgumentException($"Invalid Virtual Address: 0x{va.ToString("X")}");
        }

        #endregion
    }
}
