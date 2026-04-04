/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

namespace VmmSharpEx.Extensions.Input
{
    /// <summary>
    /// Win32 Virtual Key Codes.
    /// </summary>
    /// <remarks>
    /// See: <see href="https://learn.microsoft.com/windows/win32/inputdev/virtual-key-codes"/>
    /// </remarks>
    public enum Win32VirtualKey : uint
    {
        /// <summary>Left mouse button</summary>
        LBUTTON = 0x01,
        /// <summary>Right mouse button</summary>
        RBUTTON = 0x02,
        /// <summary>Control-break processing</summary>
        CANCEL = 0x03,
        /// <summary>Middle mouse button</summary>
        MBUTTON = 0x04,
        /// <summary>X1 mouse button</summary>
        XBUTTON1 = 0x05,
        /// <summary>X2 mouse button</summary>
        XBUTTON2 = 0x06,
        /// <summary>Backspace key</summary>
        BACK = 0x08,
        /// <summary>Tab key</summary>
        TAB = 0x09,
        /// <summary>Clear key</summary>
        CLEAR = 0x0C,
        /// <summary>Enter key</summary>
        RETURN = 0x0D,
        /// <summary>Shift key</summary>
        SHIFT = 0x10,
        /// <summary>Ctrl key</summary>
        CONTROL = 0x11,
        /// <summary>Alt key</summary>
        MENU = 0x12,
        /// <summary>Pause key</summary>
        PAUSE = 0x13,
        /// <summary>Caps lock key</summary>
        CAPITAL = 0x14,
        /// <summary>IME Kana mode</summary>
        KANA = 0x15,
        /// <summary>IME On</summary>
        IME_ON = 0x16,
        /// <summary>IME Junja mode</summary>
        JUNJA = 0x17,
        FINAL = 0x18,
        /// <summary>IME Hanja mode</summary>
        HANJA = 0x19,
        /// <summary>IME Off</summary>
        IME_OFF = 0x1A,
        /// <summary>Esc key</summary>
        ESCAPE = 0x1B,
        /// <summary>IME convert</summary>
        CONVERT = 0x1C,
        /// <summary>IME nonconvert</summary>
        NONCONVERT = 0x1D,
        /// <summary>IME accept</summary>
        ACCEPT = 0x1E,
        /// <summary>IME mode change request</summary>
        MODECHANGE = 0x1F,
        /// <summary>Spacebar key</summary>
        SPACE = 0x20,
        /// <summary>Page up key</summary>
        PRIOR = 0x21,
        /// <summary>Page down key</summary>
        NEXT = 0x22,
        /// <summary>End key</summary>
        END = 0x23,
        /// <summary>Home key</summary>
        HOME = 0x24,
        /// <summary>Left arrow key</summary>
        LEFT = 0x25,
        /// <summary>Up arrow key</summary>
        UP = 0x26,
        /// <summary>Right arrow key</summary>
        RIGHT = 0x27,
        /// <summary>Down arrow key</summary>
        DOWN = 0x28,
        /// <summary>Select key</summary>
        /// <summary>Print key</summary>
        PRINT = 0x2A,
        /// <summary>Execute key</summary>
        EXECUTE = 0x2B,
        /// <summary>Print screen key</summary>
        SNAPSHOT = 0x2C,
        /// <summary>Insert key</summary>
        INSERT = 0x2D,
        /// <summary>Delete key</summary>
        DELETE = 0x2E,
        /// <summary>Help key</summary>
        HELP = 0x2F,
        /// <summary>0 key</summary>
        D0 = 0x30,
        /// <summary>1 key</summary>
        D1 = 0x31,
        /// <summary>2 key</summary>
        D2 = 0x32,
        /// <summary>3 key</summary>
        D3 = 0x33,
        /// <summary>4 key</summary>
        D4 = 0x34,
        /// <summary>5 key</summary>
        D5 = 0x35,
        /// <summary>6 key</summary>
        D6 = 0x36,
        /// <summary>7 key</summary>
        D7 = 0x37,
        /// <summary>8 key</summary>
        D8 = 0x38,
        /// <summary>9 key</summary>
        D9 = 0x39,
        /// <summary>A key</summary>
        A = 0x41,
        /// <summary>B key</summary>
        B = 0x42,
        /// <summary>C key</summary>
        C = 0x43,
        /// <summary>D key</summary>
        D = 0x44,
        /// <summary>E key</summary>
        E = 0x45,
        /// <summary>F key</summary>
        F = 0x46,
        /// <summary>G key</summary>
        G = 0x47,
        /// <summary>H key</summary>
        H = 0x48,
        /// <summary>I key</summary>
        I = 0x49,
        /// <summary>J key</summary>
        J = 0x4A,
        /// <summary>K key</summary>
        K = 0x4B,
        /// <summary>L key</summary>
        L = 0x4C,
        /// <summary>M key</summary>
        M = 0x4D,
        /// <summary>N key</summary>
        N = 0x4E,
        /// <summary>O key</summary>
        O = 0x4F,
        /// <summary>P key</summary>
        P = 0x50,
        /// <summary>Q key</summary>
        Q = 0x51,
        /// <summary>R key</summary>
        R = 0x52,
        /// <summary>S key</summary>
        S = 0x53,
        /// <summary>T key</summary>
        T = 0x54,
        /// <summary>U key</summary>
        U = 0x55,
        /// <summary>V key</summary>
        V = 0x56,
        /// <summary>W key</summary>
        W = 0x57,
        /// <summary>X key</summary>
        X = 0x58,
        /// <summary>Y key</summary>
        Y = 0x59,
        /// <summary>Z key</summary>
        Z = 0x5A,
        /// <summary>Left Windows logo key</summary>
        LWIN = 0x5B,
        /// <summary>Right Windows logo key</summary>
        RWIN = 0x5C,
        /// <summary>Application key</summary>
        APPS = 0x5D,
        /// <summary>Computer Sleep key</summary>
        SLEEP = 0x5F,
        /// <summary>Numeric keypad 0 key</summary>
        NUMPAD0 = 0x60,
        /// <summary>Numeric keypad 1 key</summary>
        NUMPAD1 = 0x61,
        /// <summary>Numeric keypad 2 key</summary>
        NUMPAD2 = 0x62,
        /// <summary>Numeric keypad 3 key</summary>
        NUMPAD3 = 0x63,
        /// <summary>Numeric keypad 4 key</summary>
        NUMPAD4 = 0x64,
        /// <summary>Numeric keypad 5 key</summary>
        NUMPAD5 = 0x65,
        /// <summary>Numeric keypad 6 key</summary>
        NUMPAD6 = 0x66,
        /// <summary>Numeric keypad 7 key</summary>
        NUMPAD7 = 0x67,
        /// <summary>Numeric keypad 8 key</summary>
        NUMPAD8 = 0x68,
        /// <summary>Numeric keypad 9 key</summary>
        NUMPAD9 = 0x69,
        /// <summary>Multiply key</summary>
        MULTIPLY = 0x6A,
        /// <summary>Add key</summary>
        ADD = 0x6B,
        /// <summary>Separator key</summary>
        SEPARATOR = 0x6C,
        /// <summary>Subtract key</summary>
        SUBTRACT = 0x6D,
        /// <summary>Decimal key</summary>
        DECIMAL = 0x6E,
        /// <summary>Divide key</summary>
        DIVIDE = 0x6F,
        /// <summary>F1 key</summary>
        F1 = 0x70,
        /// <summary>F2 key</summary>
        F2 = 0x71,
        /// <summary>F3 key</summary>
        F3 = 0x72,
        /// <summary>F4 key</summary>
        F4 = 0x73,
        /// <summary>F5 key</summary>
        F5 = 0x74,
        /// <summary>F6 key</summary>
        F6 = 0x75,
        /// <summary>F7 key</summary>
        F7 = 0x76,
        /// <summary>F8 key</summary>
        F8 = 0x77,
        /// <summary>F9 key</summary>
        F9 = 0x78,
        /// <summary>F10 key</summary>
        F10 = 0x79,
        /// <summary>F11 key</summary>
        F11 = 0x7A,
        /// <summary>F12 key</summary>
        F12 = 0x7B,
        /// <summary>F13 key</summary>
        F13 = 0x7C,
        /// <summary>F14 key</summary>
        F14 = 0x7D,
        /// <summary>F15 key</summary>
        F15 = 0x7E,
        /// <summary>F16 key</summary>
        F16 = 0x7F,
        /// <summary>F17 key</summary>
        F17 = 0x80,
        /// <summary>F18 key</summary>
        F18 = 0x81,
        /// <summary>F19 key</summary>
        F19 = 0x82,
        /// <summary>F20 key</summary>
        F20 = 0x83,
        /// <summary>F21 key</summary>
        F21 = 0x84,
        /// <summary>F22 key</summary>
        F22 = 0x85,
        /// <summary>F23 key</summary>
        F23 = 0x86,
        /// <summary>F24 key</summary>
        F24 = 0x87,
        /// <summary>Num lock key</summary>
        NUMLOCK = 0x90,
        /// <summary>Scroll lock key</summary>
        SCROLL = 0x91,
        /// <summary>Left Shift key</summary>
        LSHIFT = 0xA0,
        /// <summary>Right Shift key</summary>
        RSHIFT = 0xA1,
        /// <summary>Left Ctrl key</summary>
        LCONTROL = 0xA2,
        /// <summary>Right Ctrl key</summary>
        RCONTROL = 0xA3,
        /// <summary>Left Alt key</summary>
        LMENU = 0xA4,
        /// <summary>Right Alt key</summary>
        RMENU = 0xA5,
        /// <summary>Browser Back key</summary>
        BROWSER_BACK = 0xA6,
        /// <summary>Browser Forward key</summary>
        BROWSER_FORWARD = 0xA7,
        /// <summary>Browser Refresh key</summary>
        BROWSER_REFRESH = 0xA8,
        /// <summary>Browser Stop key</summary>
        BROWSER_STOP = 0xA9,
        /// <summary>Browser Search key</summary>
        BROWSER_SEARCH = 0xAA,
        /// <summary>Browser Favorites key</summary>
        BROWSER_FAVORITES = 0xAB,
        /// <summary>Browser Start and Home key</summary>
        BROWSER_HOME = 0xAC,
        /// <summary>Volume Mute key</summary>
        VOLUME_MUTE = 0xAD,
        /// <summary>Volume Down key</summary>
        VOLUME_DOWN = 0xAE,
        /// <summary>Volume Up key</summary>
        VOLUME_UP = 0xAF,
        /// <summary>Next Track key</summary>
        MEDIA_NEXT_TRACK = 0xB0,
        /// <summary>Previous Track key</summary>
        MEDIA_PREV_TRACK = 0xB1,
        /// <summary>Stop Media key</summary>
        MEDIA_STOP = 0xB2,
        /// <summary>Play/Pause Media key</summary>
        MEDIA_PLAY_PAUSE = 0xB3,
        /// <summary>Start Mail key</summary>
        LAUNCH_MAIL = 0xB4,
        /// <summary>Select Media key</summary>
        LAUNCH_MEDIA_SELECT = 0xB5,
        /// <summary>Start Application 1 key</summary>
        LAUNCH_APP1 = 0xB6,
        /// <summary>Start Application 2 key</summary>
        LAUNCH_APP2 = 0xB7,
        /// <summary>Semiсolon and Colon key (US ANSI)</summary>
        OEM_1 = 0xBA,
        /// <summary>Equals and Plus key</summary>
        OEM_PLUS = 0xBB,
        /// <summary>Comma and Less Than key</summary>
        OEM_COMMA = 0xBC,
        /// <summary>Dash and Underscore key</summary>
        OEM_MINUS = 0xBD,
        /// <summary>Period and Greater Than key</summary>
        OEM_PERIOD = 0xBE,
        /// <summary>Forward Slash and Question Mark key (US ANSI)</summary>
        OEM_2 = 0xBF,
        /// <summary>Grave Accent and Tilde key (US ANSI)</summary>
        OEM_3 = 0xC0,
        /// <summary>Gamepad A button</summary>
        GAMEPAD_A = 0xC3,
        /// <summary>Gamepad B button</summary>
        GAMEPAD_B = 0xC4,
        /// <summary>Gamepad X button</summary>
        GAMEPAD_X = 0xC5,
        /// <summary>Gamepad Y button</summary>
        GAMEPAD_Y = 0xC6,
        /// <summary>Gamepad Right Shoulder button</summary>
        GAMEPAD_RIGHT_SHOULDER = 0xC7,
        /// <summary>Gamepad Left Shoulder button</summary>
        GAMEPAD_LEFT_SHOULDER = 0xC8,
        /// <summary>Gamepad Left Trigger button</summary>
        GAMEPAD_LEFT_TRIGGER = 0xC9,
        /// <summary>Gamepad Right Trigger button</summary>
        GAMEPAD_RIGHT_TRIGGER = 0xCA,
        /// <summary>Gamepad D-pad Up button</summary>
        GAMEPAD_DPAD_UP = 0xCB,
        /// <summary>Gamepad D-pad Down button</summary>
        GAMEPAD_DPAD_DOWN = 0xCC,
        /// <summary>Gamepad D-pad Left button</summary>
        GAMEPAD_DPAD_LEFT = 0xCD,
        /// <summary>Gamepad D-pad Right button</summary>
        GAMEPAD_DPAD_RIGHT = 0xCE,
        /// <summary>Gamepad Menu/Start button</summary>
        GAMEPAD_MENU = 0xCF,
        /// <summary>Gamepad View/Back button</summary>
        GAMEPAD_VIEW = 0xD0,
        /// <summary>Gamepad Left Thumbstick button</summary>
        GAMEPAD_LEFT_THUMBSTICK_BUTTON = 0xD1,
        /// <summary>Gamepad Right Thumbstick button</summary>
        GAMEPAD_RIGHT_THUMBSTICK_BUTTON = 0xD2,
        /// <summary>Gamepad Left Thumbstick up</summary>
        GAMEPAD_LEFT_THUMBSTICK_UP = 0xD3,
        /// <summary>Gamepad Left Thumbstick down</summary>
        GAMEPAD_LEFT_THUMBSTICK_DOWN = 0xD4,
        /// <summary>Gamepad Left Thumbstick right</summary>
        GAMEPAD_LEFT_THUMBSTICK_RIGHT = 0xD5,
        /// <summary>Gamepad Left Thumbstick left</summary>
        GAMEPAD_LEFT_THUMBSTICK_LEFT = 0xD6,
        /// <summary>Gamepad Right Thumbstick up</summary>
        GAMEPAD_RIGHT_THUMBSTICK_UP = 0xD7,
        /// <summary>Gamepad Right Thumbstick down</summary>
        GAMEPAD_RIGHT_THUMBSTICK_DOWN = 0xD8,
        /// <summary>Gamepad Right Thumbstick right</summary>
        GAMEPAD_RIGHT_THUMBSTICK_RIGHT = 0xD9,
        /// <summary>Gamepad Right Thumbstick left</summary>
        GAMEPAD_RIGHT_THUMBSTICK_LEFT = 0xDA,
        /// <summary>Left Brace key (US ANSI)</summary>
        OEM_4 = 0xDB,
        /// <summary>Backslash and Pipe key (US ANSI)</summary>
        OEM_5 = 0xDC,
        /// <summary>Right Brace key (US ANSI)</summary>
        OEM_6 = 0xDD,
        /// <summary>Apostrophe and Double Quotation Mark key (US ANSI)</summary>
        OEM_7 = 0xDE,
        /// <summary>Right Ctrl key (Canadian CSA)</summary>
        OEM_8 = 0xDF,
        /// <summary>Backslash and Pipe key (European ISO)</summary>
        OEM_102 = 0xE2,
        /// <summary>IME PROCESS key</summary>
        PROCESSKEY = 0xE5,
        /// <summary>Used to pass Unicode characters as keystrokes</summary>
        PACKET = 0xE7,
        /// <summary>Attn key</summary>
        ATTN = 0xF6,
        /// <summary>CrSel key</summary>
        CRSEL = 0xF7,
        /// <summary>ExSel key</summary>
        EXSEL = 0xF8,
        /// <summary>Erase EOF key</summary>
        EREOF = 0xF9,
        /// <summary>Play key</summary>
        PLAY = 0xFA,
        /// <summary>Zoom key</summary>
        ZOOM = 0xFB,
        /// <summary>PA1 key</summary>
        PA1 = 0xFD,
        /// <summary>Clear key</summary>
        OEM_CLEAR = 0xFE
    }
}
