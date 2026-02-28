using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Pools;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using SkiaSharp;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace eft_dma_radar.Common.Unity
{
    public static class HashUtil
    {
        public static ulong RuntimeHash(string str)
        {
            unchecked
            {
                ulong hash = 5381;
                foreach (char c in str)
                    hash = ((hash << 5) + hash) + c;
                return hash;
            }
        }
    }    
    /// <summary>
    /// GOM List Node.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct BaseObject
    {
        /// <summary>
        /// Previous ListNode
        /// </summary>
        public readonly ulong PreviousObjectLink; // 0x0
        /// <summary>
        /// Next ListNode
        /// </summary>
        public readonly ulong NextObjectLink; // 0x8
        /// <summary>
        /// Current GameObject
        /// </summary>
        public readonly ulong CurrentObject; // 0x10   (to Offsets.GameObject)
    };

    [StructLayout(LayoutKind.Explicit)]
    public readonly struct GameObject
    {
        [FieldOffset(0x08)]
        public readonly int InstanceID;

        [FieldOffset((int)UnityOffsets.GameObject.ObjectClassOffset)]
        public readonly ulong ObjectClass; // m_Object

        [FieldOffset((int)UnityOffsets.GameObject.ComponentsOffset)]
        public readonly ComponentArray Components;

        [FieldOffset((int)UnityOffsets.GameObject.NameOffset)]
        public readonly ulong NamePtr;

        public string GetName(int maxLen = 128, bool useCache = true)
        {
            if (!Utils.IsValidVirtualAddress(NamePtr))
                return string.Empty;

            return Memory.ReadString(NamePtr, maxLen, useCache) ?? string.Empty;
        }

        /// <summary>
        /// Find component by class name and RETURN OBJECTCLASS POINTER
        /// (matches old Mono behaviour).
        /// </summary>
        public ulong GetComponent(string className, bool useCache = true)
        {
            if (string.IsNullOrWhiteSpace(className))
                return 0;

            var componentArr = Components;
            if (!Utils.IsValidVirtualAddress(componentArr.ArrayBase) || componentArr.Size == 0)
                return 0;

            int count = (int)Math.Min(componentArr.Size, 0x400); // safety cap
            var entries = new ComponentArray.Entry[count];

            try
            {
                Memory.ReadBuffer(componentArr.ArrayBase, entries);
            }
            catch
            {
                return 0;
            }

            for (int i = 0; i < count; i++)
            {
                var compPtr = entries[i].Component;
                if (!Utils.IsValidVirtualAddress(compPtr))
                    continue;

                ulong objectClass;
                try
                {
                    objectClass = Memory.ReadPtr(
                        compPtr + UnityOffsets.Component.ObjectClassOffset,
                        useCache);
                }
                catch
                {
                    continue;
                }

                if (!Utils.IsValidVirtualAddress(objectClass))
                    continue;

                string name;
                try
                {
                    name = eft_dma_radar.Common.Unity.ObjectClass.ReadName(objectClass, 128, useCache);
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(name))
                    continue;

                if (name.Equals(className, StringComparison.OrdinalIgnoreCase))
                    return objectClass; // <<<< return objectClass, NOT compPtr
            }

            return 0;
        }

        public static ulong GetComponent(ulong gameObjectPtr, string className, bool useCache = true)
        {
            if (!Utils.IsValidVirtualAddress(gameObjectPtr))
                return 0;

            var go = Memory.ReadValue<GameObject>(gameObjectPtr, useCache);
            return go.GetComponent(className, useCache);
        }
    }
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public readonly struct MonoBehaviour // Behaviour : Component : EditorExtension : Object
    {
        public const uint InstanceIDOffset = 0x8;
        public const uint ObjectClassOffset = 0x28;
        public const uint GameObjectOffset = 0x30;
        public const uint EnabledOffset = 0x38;
        public const uint IsAddedOffset = 0x39;

        [FieldOffset((int)InstanceIDOffset)]
        public readonly int InstanceID; // m_InstanceID
        [FieldOffset((int)ObjectClassOffset)]
        public readonly ulong ObjectClass; // m_Object
        [FieldOffset((int)GameObjectOffset)]
        public readonly ulong GameObject; // m_GameObject
        [FieldOffset((int)EnabledOffset)]
        public readonly bool Enabled; // m_Enabled
        [FieldOffset((int)IsAddedOffset)]
        public readonly bool IsAdded; // m_IsAdded

        /// <summary>
        /// Return the game object of this MonoBehaviour.
        /// </summary>
        /// <returns>GameObject struct.</returns>
        public readonly GameObject GetGameObject() =>
            Memory.ReadValue<GameObject>(ObjectClass);

        /// <summary>
        /// Gets a component class from a Behaviour object.
        /// </summary>
        /// <param name="behaviour">Behaviour object to scan.</param>
        /// <param name="className">Name of class of child.</param>
        /// <returns>Child class component.</returns>
        public static ulong GetComponent(ulong behaviour, string className)
        {
            var go = Memory.ReadPtr(behaviour + GameObjectOffset);
            return 0;
        }
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct ComponentArray
    {
        public readonly ulong ArrayBase; // To ComponentArrayEntry[]
        public readonly ulong MemLabelId;
        public readonly ulong Size;
        public readonly ulong Capacity;
        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        public readonly struct Entry
        {
            [FieldOffset(0x8)]
            public readonly ulong Component;
        }        
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public readonly struct ComponentArrayEntry
    {
        [FieldOffset(0x8)]
        public readonly ulong Component;
    }
    public readonly struct ComponentPair
    {
        public readonly string Name;
        public readonly ulong Component;

        public ComponentPair(string name, ulong component)
        {
            Name = name;
            Component = component;
        }
    }
    /// <summary>
    /// Most higher level EFT Assembly Classes and Game Objects.
    /// </summary>
    /// <summary>
    /// Object classes defined in Assembly-CSharp.dll
    /// </summary>
    public readonly struct ObjectClass
    {
        /// <summary>
        /// MonoBehaviour : Behaviour : Component : EditorExtension : Object
        /// </summary>
        public const uint MonoBehaviourOffset = 0x10;

        public const uint InstanceID = 0x18;          // int

        public static uint[] To_GameObject { get; } = new[] { MonoBehaviourOffset, UnityOffsets.GameObject.ObjectClassOffset };
        public static uint[] To_NamePtr { get; } = new uint[] { 0x0, 0x10 };

        /// <summary>
        /// Read the Class Name from any ObjectClass that implements UnityComponent.
        /// </summary>
        /// <param name="objectClass">ObjectClass address.</param>
        /// <returns>Name (string) of the object class given.</returns>
        public static string ReadName(ulong objectClass, int length = 128, bool useCache = true)
        {
            var namePtr = Memory.ReadPtrChain(objectClass, To_NamePtr, useCache);
            return Memory.ReadString(namePtr, length, useCache);
        }

        public static bool TryReadClassName(
            ulong objectBase,
            out string className,
            int maxLen = 64)
        {
            className = null;
        
            if (!objectBase.IsValidVirtualAddress())
                return false;
        
            // Il2CppObject.klass is always at +0x0 in IL2CPP
            ulong klass = Memory.ReadPtr(objectBase, false);
            if (!klass.IsValidVirtualAddress())
                return false;
        
            try
            {
                className = ObjectClass.ReadName(objectBase, maxLen);
                return !string.IsNullOrEmpty(className);
            }
            catch
            {
                return false;
            }
        }        
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct UnityColor
    {
        public readonly float R;
        public readonly float G;
        public readonly float B;
        public readonly float A;

        public UnityColor(float r, float g, float b, float a = 1f)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public UnityColor(byte r, byte g, byte b, byte a = 255)
        {
            R = r / 255f;
            G = g / 255f;
            B = b / 255f;
            A = a / 255f;
        }

        public UnityColor(string hex)
        {
            var color = SKColor.Parse(hex);

            R = color.Red / 255f;
            G = color.Green / 255f;
            B = color.Blue / 255f;
            A = color.Alpha / 255f;
        }

        public UnityColor(SKColor color)
        {
            R = color.Red / 255f;
            G = color.Green / 255f;
            B = color.Blue / 255f;
            A = color.Alpha / 255f;
        }

        public readonly override string ToString() => $"({R * 255}, {G * 255}, {B * 255}, {A * 255})";

        public static int GetSize()
        {
            return Unsafe.SizeOf<UnityColor>();
        }

        public static uint GetSizeU()
        {
            return (uint)GetSize();
        }

        public static UnityColor Invert(UnityColor color)
        {
            float invertedR = 1f - color.R;
            float invertedG = 1f - color.G;
            float invertedB = 1f - color.B;

            return new(invertedR, invertedG, invertedB, color.A);
        }
    }

    public enum UnityKeyCode
    {
        [Description(nameof(Backspace))]
        Backspace = 8,
        [Description(nameof(Tab))]
        Tab = 9,
        [Description(nameof(Clear))]
        Clear = 12,
        [Description(nameof(Return))]
        Return = 13,
        [Description(nameof(Pause))]
        Pause = 19,
        [Description(nameof(Escape))]
        Escape = 27,
        [Description(nameof(Space))]
        Space = 32,
        [Description(nameof(Exclaim))]
        Exclaim = 33,
        [Description(nameof(DoubleQuote))]
        DoubleQuote = 34,
        [Description(nameof(Hash))]
        Hash = 35,
        [Description(nameof(Dollar))]
        Dollar = 36,
        [Description(nameof(Percent))]
        Percent = 37,
        [Description(nameof(Ampersand))]
        Ampersand = 38,
        [Description(nameof(Quote))]
        Quote = 39,
        [Description(nameof(LeftParen))]
        LeftParen = 40,
        [Description(nameof(RightParen))]
        RightParen = 41,
        [Description(nameof(Asterisk))]
        Asterisk = 42,
        [Description(nameof(Plus))]
        Plus = 43,
        [Description(nameof(Comma))]
        Comma = 44,
        [Description(nameof(Minus))]
        Minus = 45,
        [Description(nameof(Period))]
        Period = 46,
        [Description(nameof(Slash))]
        Slash = 47,
        [Description(nameof(Alpha0))]
        Alpha0 = 48,
        [Description(nameof(Alpha1))]
        Alpha1 = 49,
        [Description(nameof(Alpha2))]
        Alpha2 = 50,
        [Description(nameof(Alpha3))]
        Alpha3 = 51,
        [Description(nameof(Alpha4))]
        Alpha4 = 52,
        [Description(nameof(Alpha5))]
        Alpha5 = 53,
        [Description(nameof(Alpha6))]
        Alpha6 = 54,
        [Description(nameof(Alpha7))]
        Alpha7 = 55,
        [Description(nameof(Alpha8))]
        Alpha8 = 56,
        [Description(nameof(Alpha9))]
        Alpha9 = 57,
        [Description(nameof(Colon))]
        Colon = 58,
        [Description(nameof(Semicolon))]
        Semicolon = 59,
        [Description(nameof(Less))]
        Less = 60,
        [Description(nameof(Equals))]
        Equals = 61,
        [Description(nameof(Greater))]
        Greater = 62,
        [Description(nameof(Question))]
        Question = 63,
        [Description(nameof(At))]
        At = 64,
        [Description(nameof(LeftBracket))]
        LeftBracket = 91,
        [Description(nameof(Backslash))]
        Backslash = 92,
        [Description(nameof(RightBracket))]
        RightBracket = 93,
        [Description(nameof(Caret))]
        Caret = 94,
        [Description(nameof(Underscore))]
        Underscore = 95,
        [Description(nameof(BackQuote))]
        BackQuote = 96,
        [Description(nameof(A))]
        A = 97,
        [Description(nameof(B))]
        B = 98,
        [Description(nameof(C))]
        C = 99,
        [Description(nameof(D))]
        D = 100,
        [Description(nameof(E))]
        E = 101,
        [Description(nameof(F))]
        F = 102,
        [Description(nameof(G))]
        G = 103,
        [Description(nameof(H))]
        H = 104,
        [Description(nameof(I))]
        I = 105,
        [Description(nameof(J))]
        J = 106,
        [Description(nameof(K))]
        K = 107,
        [Description(nameof(L))]
        L = 108,
        [Description(nameof(M))]
        M = 109,
        [Description(nameof(N))]
        N = 110,
        [Description(nameof(O))]
        O = 111,
        [Description(nameof(P))]
        P = 112,
        [Description(nameof(Q))]
        Q = 113,
        [Description(nameof(R))]
        R = 114,
        [Description(nameof(S))]
        S = 115,
        [Description(nameof(T))]
        T = 116,
        [Description(nameof(U))]
        U = 117,
        [Description(nameof(V))]
        V = 118,
        [Description(nameof(W))]
        W = 119,
        [Description(nameof(X))]
        X = 120,
        [Description(nameof(Y))]
        Y = 121,
        [Description(nameof(Z))]
        Z = 122,
        [Description(nameof(LeftCurlyBracket))]
        LeftCurlyBracket = 123,
        [Description(nameof(Pipe))]
        Pipe = 124,
        [Description(nameof(RightCurlyBracket))]
        RightCurlyBracket = 125,
        [Description(nameof(Tilde))]
        Tilde = 126,
        [Description(nameof(Delete))]
        Delete = 127,
        [Description(nameof(Keypad0))]
        Keypad0 = 256,
        [Description(nameof(Keypad1))]
        Keypad1 = 257,
        [Description(nameof(Keypad2))]
        Keypad2 = 258,
        [Description(nameof(Keypad3))]
        Keypad3 = 259,
        [Description(nameof(Keypad4))]
        Keypad4 = 260,
        [Description(nameof(Keypad5))]
        Keypad5 = 261,
        [Description(nameof(Keypad6))]
        Keypad6 = 262,
        [Description(nameof(Keypad7))]
        Keypad7 = 263,
        [Description(nameof(Keypad8))]
        Keypad8 = 264,
        [Description(nameof(Keypad9))]
        Keypad9 = 265,
        [Description(nameof(KeypadPeriod))]
        KeypadPeriod = 266,
        [Description(nameof(KeypadDivide))]
        KeypadDivide = 267,
        [Description(nameof(KeypadMultiply))]
        KeypadMultiply = 268,
        [Description(nameof(KeypadMinus))]
        KeypadMinus = 269,
        [Description(nameof(KeypadPlus))]
        KeypadPlus = 270,
        [Description(nameof(KeypadEnter))]
        KeypadEnter = 271,
        [Description(nameof(KeypadEquals))]
        KeypadEquals = 272,
        [Description(nameof(UpArrow))]
        UpArrow = 273,
        [Description(nameof(DownArrow))]
        DownArrow = 274,
        [Description(nameof(RightArrow))]
        RightArrow = 275,
        [Description(nameof(LeftArrow))]
        LeftArrow = 276,
        [Description(nameof(Insert))]
        Insert = 277,
        [Description(nameof(Home))]
        Home = 278,
        [Description(nameof(End))]
        End = 279,
        [Description(nameof(PageUp))]
        PageUp = 280,
        [Description(nameof(PageDown))]
        PageDown = 281,
        [Description(nameof(F1))]
        F1 = 282,
        [Description(nameof(F2))]
        F2 = 283,
        [Description(nameof(F3))]
        F3 = 284,
        [Description(nameof(F4))]
        F4 = 285,
        [Description(nameof(F5))]
        F5 = 286,
        [Description(nameof(F6))]
        F6 = 287,
        [Description(nameof(F7))]
        F7 = 288,
        [Description(nameof(F8))]
        F8 = 289,
        [Description(nameof(F9))]
        F9 = 290,
        [Description(nameof(F10))]
        F10 = 291,
        [Description(nameof(F11))]
        F11 = 292,
        [Description(nameof(F12))]
        F12 = 293,
        [Description(nameof(F13))]
        F13 = 294,
        [Description(nameof(F14))]
        F14 = 295,
        [Description(nameof(F15))]
        F15 = 296,
        [Description(nameof(Numlock))]
        Numlock = 300,
        [Description(nameof(CapsLock))]
        CapsLock = 301,
        [Description(nameof(ScrollLock))]
        ScrollLock = 302,
        [Description(nameof(RightShift))]
        RightShift = 303,
        [Description(nameof(LeftShift))]
        LeftShift = 304,
        [Description(nameof(RightControl))]
        RightControl = 305,
        [Description(nameof(LeftControl))]
        LeftControl = 306,
        [Description(nameof(RightAlt))]
        RightAlt = 307,
        [Description(nameof(LeftAlt))]
        LeftAlt = 308,
        [Description(nameof(RightMeta))]
        RightMeta = 309,
        [Description(nameof(LeftApple))]
        LeftApple = 310,
        [Description(nameof(LeftWindows))]
        LeftWindows = 311,
        [Description(nameof(RightWindows))]
        RightWindows = 312,
        [Description(nameof(AltGr))]
        AltGr = 313,
        [Description(nameof(Help))]
        Help = 315,
        [Description(nameof(Print))]
        Print = 316,
        [Description(nameof(SysReq))]
        SysReq = 317,
        [Description(nameof(Break))]
        Break = 318,
        [Description(nameof(Menu))]
        Menu = 319,
        [Description(nameof(Mouse0))]
        Mouse0 = 323,
        [Description(nameof(Mouse1))]
        Mouse1 = 324,
        [Description(nameof(Mouse2))]
        Mouse2 = 325,
        [Description(nameof(Mouse3))]
        Mouse3 = 326,
        [Description(nameof(Mouse4))]
        Mouse4 = 327,
        [Description(nameof(Mouse5))]
        Mouse5 = 328,
        [Description(nameof(Mouse6))]
        Mouse6 = 329,
        [Description(nameof(JoystickButton0))]
        JoystickButton0 = 330,
        [Description(nameof(JoystickButton1))]
        JoystickButton1 = 331,
        [Description(nameof(JoystickButton2))]
        JoystickButton2 = 332,
        [Description(nameof(JoystickButton3))]
        JoystickButton3 = 333,
        [Description(nameof(JoystickButton4))]
        JoystickButton4 = 334,
        [Description(nameof(JoystickButton5))]
        JoystickButton5 = 335,
        [Description(nameof(JoystickButton6))]
        JoystickButton6 = 336,
        [Description(nameof(JoystickButton7))]
        JoystickButton7 = 337,
        [Description(nameof(JoystickButton8))]
        JoystickButton8 = 338,
        [Description(nameof(JoystickButton9))]
        JoystickButton9 = 339,
        [Description(nameof(JoystickButton10))]
        JoystickButton10 = 340,
        [Description(nameof(JoystickButton11))]
        JoystickButton11 = 341,
        [Description(nameof(JoystickButton12))]
        JoystickButton12 = 342,
        [Description(nameof(JoystickButton13))]
        JoystickButton13 = 343,
        [Description(nameof(JoystickButton14))]
        JoystickButton14 = 344,
        [Description(nameof(JoystickButton15))]
        JoystickButton15 = 345,
        [Description(nameof(JoystickButton16))]
        JoystickButton16 = 346,
        [Description(nameof(JoystickButton17))]
        JoystickButton17 = 347,
        [Description(nameof(JoystickButton18))]
        JoystickButton18 = 348,
        [Description(nameof(JoystickButton19))]
        JoystickButton19 = 349,
        [Description(nameof(Joystick1Button0))]
        Joystick1Button0 = 350,
        [Description(nameof(Joystick1Button1))]
        Joystick1Button1 = 351,
        [Description(nameof(Joystick1Button2))]
        Joystick1Button2 = 352,
        [Description(nameof(Joystick1Button3))]
        Joystick1Button3 = 353,
        [Description(nameof(Joystick1Button4))]
        Joystick1Button4 = 354,
        [Description(nameof(Joystick1Button5))]
        Joystick1Button5 = 355,
        [Description(nameof(Joystick1Button6))]
        Joystick1Button6 = 356,
        [Description(nameof(Joystick1Button7))]
        Joystick1Button7 = 357,
        [Description(nameof(Joystick1Button8))]
        Joystick1Button8 = 358,
        [Description(nameof(Joystick1Button9))]
        Joystick1Button9 = 359,
        [Description(nameof(Joystick1Button10))]
        Joystick1Button10 = 360,
        [Description(nameof(Joystick1Button11))]
        Joystick1Button11 = 361,
        [Description(nameof(Joystick1Button12))]
        Joystick1Button12 = 362,
        [Description(nameof(Joystick1Button13))]
        Joystick1Button13 = 363,
        [Description(nameof(Joystick1Button14))]
        Joystick1Button14 = 364,
        [Description(nameof(Joystick1Button15))]
        Joystick1Button15 = 365,
        [Description(nameof(Joystick1Button16))]
        Joystick1Button16 = 366,
        [Description(nameof(Joystick1Button17))]
        Joystick1Button17 = 367,
        [Description(nameof(Joystick1Button18))]
        Joystick1Button18 = 368,
        [Description(nameof(Joystick1Button19))]
        Joystick1Button19 = 369,
        [Description(nameof(Joystick2Button0))]
        Joystick2Button0 = 370,
        [Description(nameof(Joystick2Button1))]
        Joystick2Button1 = 371,
        [Description(nameof(Joystick2Button2))]
        Joystick2Button2 = 372,
        [Description(nameof(Joystick2Button3))]
        Joystick2Button3 = 373,
        [Description(nameof(Joystick2Button4))]
        Joystick2Button4 = 374,
        [Description(nameof(Joystick2Button5))]
        Joystick2Button5 = 375,
        [Description(nameof(Joystick2Button6))]
        Joystick2Button6 = 376,
        [Description(nameof(Joystick2Button7))]
        Joystick2Button7 = 377,
        [Description(nameof(Joystick2Button8))]
        Joystick2Button8 = 378,
        [Description(nameof(Joystick2Button9))]
        Joystick2Button9 = 379,
        [Description(nameof(Joystick2Button10))]
        Joystick2Button10 = 380,
        [Description(nameof(Joystick2Button11))]
        Joystick2Button11 = 381,
        [Description(nameof(Joystick2Button12))]
        Joystick2Button12 = 382,
        [Description(nameof(Joystick2Button13))]
        Joystick2Button13 = 383,
        [Description(nameof(Joystick2Button14))]
        Joystick2Button14 = 384,
        [Description(nameof(Joystick2Button15))]
        Joystick2Button15 = 385,
        [Description(nameof(Joystick2Button16))]
        Joystick2Button16 = 386,
        [Description(nameof(Joystick2Button17))]
        Joystick2Button17 = 387,
        [Description(nameof(Joystick2Button18))]
        Joystick2Button18 = 388,
        [Description(nameof(Joystick2Button19))]
        Joystick2Button19 = 389,
        [Description(nameof(Joystick3Button0))]
        Joystick3Button0 = 390,
        [Description(nameof(Joystick3Button1))]
        Joystick3Button1 = 391,
        [Description(nameof(Joystick3Button2))]
        Joystick3Button2 = 392,
        [Description(nameof(Joystick3Button3))]
        Joystick3Button3 = 393,
        [Description(nameof(Joystick3Button4))]
        Joystick3Button4 = 394,
        [Description(nameof(Joystick3Button5))]
        Joystick3Button5 = 395,
        [Description(nameof(Joystick3Button6))]
        Joystick3Button6 = 396,
        [Description(nameof(Joystick3Button7))]
        Joystick3Button7 = 397,
        [Description(nameof(Joystick3Button8))]
        Joystick3Button8 = 398,
        [Description(nameof(Joystick3Button9))]
        Joystick3Button9 = 399,
        [Description(nameof(Joystick3Button10))]
        Joystick3Button10 = 400,
        [Description(nameof(Joystick3Button11))]
        Joystick3Button11 = 401,
        [Description(nameof(Joystick3Button12))]
        Joystick3Button12 = 402,
        [Description(nameof(Joystick3Button13))]
        Joystick3Button13 = 403,
        [Description(nameof(Joystick3Button14))]
        Joystick3Button14 = 404,
        [Description(nameof(Joystick3Button15))]
        Joystick3Button15 = 405,
        [Description(nameof(Joystick3Button16))]
        Joystick3Button16 = 406,
        [Description(nameof(Joystick3Button17))]
        Joystick3Button17 = 407,
        [Description(nameof(Joystick3Button18))]
        Joystick3Button18 = 408,
        [Description(nameof(Joystick3Button19))]
        Joystick3Button19 = 409,
        [Description(nameof(Joystick4Button0))]
        Joystick4Button0 = 410,
        [Description(nameof(Joystick4Button1))]
        Joystick4Button1 = 411,
        [Description(nameof(Joystick4Button2))]
        Joystick4Button2 = 412,
        [Description(nameof(Joystick4Button3))]
        Joystick4Button3 = 413,
        [Description(nameof(Joystick4Button4))]
        Joystick4Button4 = 414,
        [Description(nameof(Joystick4Button5))]
        Joystick4Button5 = 415,
        [Description(nameof(Joystick4Button6))]
        Joystick4Button6 = 416,
        [Description(nameof(Joystick4Button7))]
        Joystick4Button7 = 417,
        [Description(nameof(Joystick4Button8))]
        Joystick4Button8 = 418,
        [Description(nameof(Joystick4Button9))]
        Joystick4Button9 = 419,
        [Description(nameof(Joystick4Button10))]
        Joystick4Button10 = 420,
        [Description(nameof(Joystick4Button11))]
        Joystick4Button11 = 421,
        [Description(nameof(Joystick4Button12))]
        Joystick4Button12 = 422,
        [Description(nameof(Joystick4Button13))]
        Joystick4Button13 = 423,
        [Description(nameof(Joystick4Button14))]
        Joystick4Button14 = 424,
        [Description(nameof(Joystick4Button15))]
        Joystick4Button15 = 425,
        [Description(nameof(Joystick4Button16))]
        Joystick4Button16 = 426,
        [Description(nameof(Joystick4Button17))]
        Joystick4Button17 = 427,
        [Description(nameof(Joystick4Button18))]
        Joystick4Button18 = 428,
        [Description(nameof(Joystick4Button19))]
        Joystick4Button19 = 429,
        [Description(nameof(Joystick5Button0))]
        Joystick5Button0 = 430,
        [Description(nameof(Joystick5Button1))]
        Joystick5Button1 = 431,
        [Description(nameof(Joystick5Button2))]
        Joystick5Button2 = 432,
        [Description(nameof(Joystick5Button3))]
        Joystick5Button3 = 433,
        [Description(nameof(Joystick5Button4))]
        Joystick5Button4 = 434,
        [Description(nameof(Joystick5Button5))]
        Joystick5Button5 = 435,
        [Description(nameof(Joystick5Button6))]
        Joystick5Button6 = 436,
        [Description(nameof(Joystick5Button7))]
        Joystick5Button7 = 437,
        [Description(nameof(Joystick5Button8))]
        Joystick5Button8 = 438,
        [Description(nameof(Joystick5Button9))]
        Joystick5Button9 = 439,
        [Description(nameof(Joystick5Button10))]
        Joystick5Button10 = 440,
        [Description(nameof(Joystick5Button11))]
        Joystick5Button11 = 441,
        [Description(nameof(Joystick5Button12))]
        Joystick5Button12 = 442,
        [Description(nameof(Joystick5Button13))]
        Joystick5Button13 = 443,
        [Description(nameof(Joystick5Button14))]
        Joystick5Button14 = 444,
        [Description(nameof(Joystick5Button15))]
        Joystick5Button15 = 445,
        [Description(nameof(Joystick5Button16))]
        Joystick5Button16 = 446,
        [Description(nameof(Joystick5Button17))]
        Joystick5Button17 = 447,
        [Description(nameof(Joystick5Button18))]
        Joystick5Button18 = 448,
        [Description(nameof(Joystick5Button19))]
        Joystick5Button19 = 449,
        [Description(nameof(Joystick6Button0))]
        Joystick6Button0 = 450,
        [Description(nameof(Joystick6Button1))]
        Joystick6Button1 = 451,
        [Description(nameof(Joystick6Button2))]
        Joystick6Button2 = 452,
        [Description(nameof(Joystick6Button3))]
        Joystick6Button3 = 453,
        [Description(nameof(Joystick6Button4))]
        Joystick6Button4 = 454,
        [Description(nameof(Joystick6Button5))]
        Joystick6Button5 = 455,
        [Description(nameof(Joystick6Button6))]
        Joystick6Button6 = 456,
        [Description(nameof(Joystick6Button7))]
        Joystick6Button7 = 457,
        [Description(nameof(Joystick6Button8))]
        Joystick6Button8 = 458,
        [Description(nameof(Joystick6Button9))]
        Joystick6Button9 = 459,
        [Description(nameof(Joystick6Button10))]
        Joystick6Button10 = 460,
        [Description(nameof(Joystick6Button11))]
        Joystick6Button11 = 461,
        [Description(nameof(Joystick6Button12))]
        Joystick6Button12 = 462,
        [Description(nameof(Joystick6Button13))]
        Joystick6Button13 = 463,
        [Description(nameof(Joystick6Button14))]
        Joystick6Button14 = 464,
        [Description(nameof(Joystick6Button15))]
        Joystick6Button15 = 465,
        [Description(nameof(Joystick6Button16))]
        Joystick6Button16 = 466,
        [Description(nameof(Joystick6Button17))]
        Joystick6Button17 = 467,
        [Description(nameof(Joystick6Button18))]
        Joystick6Button18 = 468,
        [Description(nameof(Joystick6Button19))]
        Joystick6Button19 = 469,
        [Description(nameof(Joystick7Button0))]
        Joystick7Button0 = 470,
        [Description(nameof(Joystick7Button1))]
        Joystick7Button1 = 471,
        [Description(nameof(Joystick7Button2))]
        Joystick7Button2 = 472,
        [Description(nameof(Joystick7Button3))]
        Joystick7Button3 = 473,
        [Description(nameof(Joystick7Button4))]
        Joystick7Button4 = 474,
        [Description(nameof(Joystick7Button5))]
        Joystick7Button5 = 475,
        [Description(nameof(Joystick7Button6))]
        Joystick7Button6 = 476,
        [Description(nameof(Joystick7Button7))]
        Joystick7Button7 = 477,
        [Description(nameof(Joystick7Button8))]
        Joystick7Button8 = 478,
        [Description(nameof(Joystick7Button9))]
        Joystick7Button9 = 479,
        [Description(nameof(Joystick7Button10))]
        Joystick7Button10 = 480,
        [Description(nameof(Joystick7Button11))]
        Joystick7Button11 = 481,
        [Description(nameof(Joystick7Button12))]
        Joystick7Button12 = 482,
        [Description(nameof(Joystick7Button13))]
        Joystick7Button13 = 483,
        [Description(nameof(Joystick7Button14))]
        Joystick7Button14 = 484,
        [Description(nameof(Joystick7Button15))]
        Joystick7Button15 = 485,
        [Description(nameof(Joystick7Button16))]
        Joystick7Button16 = 486,
        [Description(nameof(Joystick7Button17))]
        Joystick7Button17 = 487,
        [Description(nameof(Joystick7Button18))]
        Joystick7Button18 = 488,
        [Description(nameof(Joystick7Button19))]
        Joystick7Button19 = 489,
        [Description(nameof(Joystick8Button0))]
        Joystick8Button0 = 490,
        [Description(nameof(Joystick8Button1))]
        Joystick8Button1 = 491,
        [Description(nameof(Joystick8Button2))]
        Joystick8Button2 = 492,
        [Description(nameof(Joystick8Button3))]
        Joystick8Button3 = 493,
        [Description(nameof(Joystick8Button4))]
        Joystick8Button4 = 494,
        [Description(nameof(Joystick8Button5))]
        Joystick8Button5 = 495,
        [Description(nameof(Joystick8Button6))]
        Joystick8Button6 = 496,
        [Description(nameof(Joystick8Button7))]
        Joystick8Button7 = 497,
        [Description(nameof(Joystick8Button8))]
        Joystick8Button8 = 498,
        [Description(nameof(Joystick8Button9))]
        Joystick8Button9 = 499,
        [Description(nameof(Joystick8Button10))]
        Joystick8Button10 = 500,
        [Description(nameof(Joystick8Button11))]
        Joystick8Button11 = 501,
        [Description(nameof(Joystick8Button12))]
        Joystick8Button12 = 502,
        [Description(nameof(Joystick8Button13))]
        Joystick8Button13 = 503,
        [Description(nameof(Joystick8Button14))]
        Joystick8Button14 = 504,
        [Description(nameof(Joystick8Button15))]
        Joystick8Button15 = 505,
        [Description(nameof(Joystick8Button16))]
        Joystick8Button16 = 506,
        [Description(nameof(Joystick8Button17))]
        Joystick8Button17 = 507,
        [Description(nameof(Joystick8Button18))]
        Joystick8Button18 = 508,
        [Description(nameof(Joystick8Button19))]
        Joystick8Button19 = 509
    }
}