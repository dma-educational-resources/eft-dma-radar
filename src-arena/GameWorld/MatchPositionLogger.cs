using eft_dma_radar.Arena.GameWorld;
using System.Runtime.CompilerServices;

namespace eft_dma_radar.Arena.GameWorld
{
    /// <summary>
    /// Writes a per-match CSV of every player position/rotation sample so a
    /// full match replay can be reconstructed offline.
    ///
    /// Schema (comma-separated, no quotes):
    ///   timestamp_ms , player_addr , name , type , is_local ,
    ///   pos_x , pos_y , pos_z , yaw , pitch ,
    ///   transform_internal , vertices_addr , rotation_addr , status
    ///
    /// status values:
    ///   rt        = realtime transform scatter read
    ///   bone      = bone-derived fallback (transform not ready)
    ///   stale_bone= realtime was stale; overridden from bones
    ///   init      = position set during transform init
    ///
    /// • Enabled at startup by calling <see cref="Open"/>.
    /// • Flushed + closed by calling <see cref="Close"/>.
    /// • Thread-safe (lock-free single-writer: only the realtime scatter thread
    ///   calls <see cref="Record"/>).
    /// </summary>
    internal static class MatchPositionLogger
    {
        private static StreamWriter? _writer;
        private static long _matchStartMs;
        private static bool _enabled;
        private static readonly object _lock = new();

        /// <summary>Returns true when logging is active.</summary>
        internal static bool IsEnabled => _enabled;

        /// <summary>
        /// Creates a new match log file and starts recording.
        /// Safe to call multiple times — previous file is closed first.
        /// </summary>
        internal static void Open()
        {
            Close(); // close previous if any

            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "eft-dma-radar-arena");
                Directory.CreateDirectory(dir);

                string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(dir, $"matchlog_{ts}.csv");

                var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(fs, System.Text.Encoding.UTF8, 0x4000) { AutoFlush = false };
                _writer.WriteLine(
                    "timestamp_ms,player_addr,name,type,is_local," +
                    "pos_x,pos_y,pos_z,yaw,pitch," +
                    "transform_internal,vertices_addr,rotation_addr,status");

                _matchStartMs = Environment.TickCount64;
                _enabled = true;

                Misc.Log.WriteLine($"[MatchPositionLogger] Opened: {path}");
            }
            catch (Exception ex)
            {
                _enabled = false;
                Misc.Log.WriteLine($"[MatchPositionLogger] Open failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Flushes and closes the current log file.
        /// </summary>
        internal static void Close()
        {
            if (!_enabled) return;
            _enabled = false;

            try
            {
                var w = Interlocked.Exchange(ref _writer, null);
                w?.Flush();
                w?.Dispose();
                Misc.Log.WriteLine("[MatchPositionLogger] Closed.");
            }
            catch (Exception ex)
            {
                Misc.Log.WriteLine($"[MatchPositionLogger] Close failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Records a single position/rotation sample from the realtime scatter thread.
        /// Only logs rows with a valid, non-zero position to avoid polluting the CSV.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Record(Player player)
        {
            // Skip rows where the position hasn't been validated yet (zero/sentinel reads).
            if (!player.HasValidPosition) return;
            WriteRow(player, "rt");
        }

        /// <summary>
        /// Records a position sample that came from the bone-derived fallback path
        /// (realtime transform not ready or stale).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void RecordBone(Player player, bool wasStale = false)
        {
            if (!player.HasValidPosition) return;
            WriteRow(player, wasStale ? "stale_bone" : "bone");
        }

        /// <summary>
        /// Records a position sample that was set during transform init.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void RecordInit(Player player)
        {
            if (!player.HasValidPosition) return;
            WriteRow(player, "init");
        }

        private static void WriteRow(Player player, string status)
        {
            if (!_enabled) return;

            long elapsed = Environment.TickCount64 - _matchStartMs;
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            // Build the full row as a single string so the write is atomic.
            var sb = new System.Text.StringBuilder(256);
            sb.Append(elapsed);                                          sb.Append(',');
            sb.Append("0x"); sb.Append(player.Base.ToString("X"));      sb.Append(',');
            AppendSafe(sb, player.Name);                                 sb.Append(',');
            sb.Append((int)player.Type);                                 sb.Append(',');
            sb.Append(player.IsLocalPlayer ? '1' : '0');                 sb.Append(',');
            sb.Append(player.Position.X.ToString("F3", inv));            sb.Append(',');
            sb.Append(player.Position.Y.ToString("F3", inv));            sb.Append(',');
            sb.Append(player.Position.Z.ToString("F3", inv));            sb.Append(',');
            sb.Append(player.RotationYaw.ToString("F2", inv));           sb.Append(',');
            sb.Append(player.RotationPitch.ToString("F2", inv));         sb.Append(',');
            sb.Append("0x"); sb.Append(player.TransformInternal.ToString("X")); sb.Append(',');
            sb.Append("0x"); sb.Append(player.VerticesAddr.ToString("X"));      sb.Append(',');
            sb.Append("0x"); sb.Append(player.RotationAddr.ToString("X"));      sb.Append(',');
            sb.Append(status);

            lock (_lock)
            {
                var w = _writer;
                if (w is null) return;
                w.WriteLine(sb);
            }
        }

        /// <summary>Periodic flush so the file is usable during a live match.</summary>
        internal static void Flush()
        {
            if (!_enabled) return;
            lock (_lock)
            {
                try { _writer?.Flush(); }
                catch { /* ignore */ }
            }
        }

        // Escape a player name so it can't break CSV parsing.
        private static void AppendSafe(System.Text.StringBuilder sb, string s)
        {
            if (s.IndexOfAny([',', '"', '\n', '\r']) < 0)
            {
                sb.Append(s);
            }
            else
            {
                sb.Append('"');
                foreach (char c in s)
                {
                    if (c == '"') sb.Append('"');
                    sb.Append(c);
                }
                sb.Append('"');
            }
        }
    }
}
