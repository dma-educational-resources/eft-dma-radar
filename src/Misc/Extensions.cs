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
            return EnumData<TEnum>.Descriptions.AddOrUpdate(value,
                (key) =>
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
                },
                (key, existingValue) =>
                {
                    return existingValue;
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
        /// <summary>
        /// Calculate a normalized direction towards a destination position.
        /// </summary>
        /// <param name="source">Source position.</param>
        /// <param name="destination">Destination position.</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 CalculateDirection(this Vector3 source, Vector3 destination)
        {
            // Calculate the direction from source to destination
            Vector3 direction = destination - source;

            // Normalize the direction vector
            return Vector3.Normalize(direction);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 AsVector2(this SKPoint point) =>
            Unsafe.BitCast<SKPoint, Vector2>(point);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SKPoint AsSKPoint(this Vector2 vector) =>
            Unsafe.BitCast<Vector2, SKPoint>(vector);

        #endregion

        #region Memory Extensions

        /// <summary>
        /// Checks if an array contains a signature, and returns the offset where the signature occurs.
        /// </summary>
        /// <param name="array">Array to search in.</param>
        /// <param name="signature">Signature to search for. Must not be larger than array.</param>
        /// <param name="mask">Optional Signature Mask. x = check for match, ? = wildcard</param>
        /// <returns>Signature offset within array. -1 if not found.</returns>
        public static int FindSignatureOffset(this byte[] array, ReadOnlySpan<byte> signature, string mask = null)
        {
            ReadOnlySpan<byte> span = array.AsSpan();
            return span.FindSignatureOffset(signature, mask);
        }

        /// <summary>
        /// Checks if an array contains a signature, and returns the offset where the signature occurs.
        /// </summary>
        /// <param name="array">Array to search in.</param>
        /// <param name="signature">Signature to search for. Must not be larger than array.</param>
        /// <param name="mask">Optional Signature Mask. x = check for match, ? = wildcard</param>
        /// <returns>Signature offset within array. -1 if not found.</returns>
        public static int FindSignatureOffset(this Span<byte> array, ReadOnlySpan<byte> signature, string mask = null)
        {
            ReadOnlySpan<byte> span = array;
            return span.FindSignatureOffset(signature, mask);
        }
        public static uint FindSignature(this byte[] data, in byte[] signature, in string mask = null)
        {
            if (data == null || signature == null || data.Length < signature.Length)
                return uint.MaxValue;

            if (mask == null)
            {
                for (int i = 0; i <= data.Length - signature.Length; i++)
                {
                    bool isMatch = true;
                    for (int j = 0; j < signature.Length; j++)
                    {
                        if (data[i + j] != signature[j])
                        {
                            isMatch = false;
                            break;
                        }
                    }

                    if (isMatch)
                        return (uint)i;
                }
            }
            else
            {
                if (signature.Length != mask.Length)
                    throw new Exception("[FIND SIGNATURE] Invalid mask length! Make sure mask is the same length as the signature.");

                for (int i = 0; i <= data.Length - signature.Length; i++)
                {
                    bool isMatch = true;
                    for (int j = 0; j < signature.Length; j++)
                    {
                        if (mask[j] == 'x' && data[i + j] != signature[j])
                        {
                            isMatch = false;
                            break;
                        }
                        else if (mask[j] != 'x' && mask[j] != '?')
                            throw new Exception("[FIND SIGNATURE] Invalid character in mask! Use \"x\" for match and \"?\" for wildcard.");
                    }

                    if (isMatch)
                        return (uint)i;
                }
            }

            return uint.MaxValue; // Not found
        }
        /// <summary>
        /// Merges a patch into the byte[] with an optional positive or negative offset.
        /// </summary>
        public static byte[] Patch(this byte[] data, in byte[] patch, int offset = 0x0)
        {
            // Determine the start index of the patch in the merged array
            int startIndex = offset < 0 ? 0 : offset;
            // Determine additional space needed at the beginning for a negative offset
            int prependedSpace = offset < 0 ? Math.Abs(offset) : 0;
            // Final length of the merged array
            int finalLength = Math.Max(data.Length + prependedSpace, startIndex + patch.Length);

            byte[] patchedArray = new byte[finalLength];

            // Copy the original data into the mergedArray, adjusted for any prepended space due to a negative offset
            Array.Copy(data, 0, patchedArray, prependedSpace, data.Length);

            // Merge the patch
            for (int i = 0; i < patch.Length; i++)
            {
                int index = startIndex + i;
                if (index < patchedArray.Length)
                    patchedArray[index] = patch[i];
            }

            return patchedArray;
        }
        /// <summary>
        /// Checks if an array contains a signature, and returns the offset where the signature occurs.
        /// </summary>
        /// <param name="array">Array to search in.</param>
        /// <param name="signature">Signature to search for. Must not be larger than array.</param>
        /// <param name="mask">Optional Signature Mask. x = check for match, ? = wildcard</param>
        /// <returns>Signature offset within array. -1 if not found.</returns>
        public static int FindSignatureOffset(this ReadOnlySpan<byte> array, ReadOnlySpan<byte> signature, string mask = null)
        {
            ArgumentOutOfRangeException.ThrowIfZero(array.Length, nameof(array));
            ArgumentOutOfRangeException.ThrowIfZero(signature.Length, nameof(signature));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(signature.Length, array.Length, nameof(signature));
            if (mask is not null && signature.Length != mask.Length)
                throw new ArgumentException("Mask Length does not match Signature length!");

            for (int i = 0; i <= array.Length - signature.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < signature.Length; j++)
                {
                    if (mask is not null && mask[j] == '?') // Skip on wildcard mask
                        continue;
                    // If any byte does not match, set found to false and break the inner loop.
                    if (array[i + j] != signature[j])
                    {
                        found = false;
                        break;
                    }
                }

                // If all bytes match, return the current index.
                if (found)
                {
                    return i;
                }
            }

            // If the signature is not found, return -1.
            return -1;
        }

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
