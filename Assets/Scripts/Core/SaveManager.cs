using System;
using System.IO;
using UnityEngine;

namespace CubeFly.Core
{
    // Thin static layer on top of the filesystem. Files are written
    // atomically via temp + rename so a crash mid-write can't leave a
    // half-finished JSON behind. All operations log to the file logger;
    // none throw — load/save errors return false and surface in the
    // log (and, where applicable, the picker UI).
    //
    // Save location:
    //   • In the Editor — or when the SAVES_IN_PROJECT scripting
    //     define is active — saves go in <project root>/Saves/. This
    //     keeps the dev workflow simple (saves are right next to the
    //     project; trivially diffable) and is git-ignored.
    //   • In a built player without that define, saves go in
    //     Application.persistentDataPath/saves/ which is the
    //     OS-appropriate path on each platform.
    public static class SaveManager
    {
        public const int SlotCount = 3;
        const string TAG = "SaveManager";

        public static string SaveDirectory
        {
            get
            {
#if UNITY_EDITOR || SAVES_IN_PROJECT
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Saves"));
#else
                return Path.Combine(Application.persistentDataPath, "saves");
#endif
            }
        }

        public static string SlotPath(int slot)
            => Path.Combine(SaveDirectory, $"slot{slot}.json");

        public static bool IsValidSlot(int slot) => slot >= 0 && slot < SlotCount;

        public static bool Exists(int slot) => IsValidSlot(slot) && File.Exists(SlotPath(slot));

        // Read every slot's metadata in one pass for the picker. Empty
        // or unreadable slots return SaveSlotInfo.Empty(slot). Never
        // throws — IO errors degrade to "empty" with a logged warning.
        public static SaveSlotInfo[] ReadAllSlotMetadata()
        {
            EnsureDirectory();
            SaveSlotInfo[] result = new SaveSlotInfo[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                if (TryLoad(i, out ConstructSave save))
                    result[i] = SaveSlotInfo.From(i, save);
                else
                    result[i] = SaveSlotInfo.Empty(i);
            }
            return result;
        }

        public static bool TryLoad(int slot, out ConstructSave save)
        {
            save = null;
            if (!IsValidSlot(slot)) return false;

            string path = SlotPath(slot);
            if (!File.Exists(path)) return false;

            try
            {
                string json = File.ReadAllText(path);
                ConstructSave parsed = JsonUtility.FromJson<ConstructSave>(json);
                if (parsed == null)
                {
                    Debug.unityLogger.LogError(TAG, $"Slot {slot}: JsonUtility returned null for '{path}'.");
                    return false;
                }
                if (parsed.version > ConstructSave.CurrentVersion)
                {
                    Debug.unityLogger.LogError(TAG,
                        $"Slot {slot}: save version {parsed.version} is newer than supported " +
                        $"({ConstructSave.CurrentVersion}). Refusing to load.");
                    return false;
                }
                if (parsed.placements == null) parsed.placements = Array.Empty<PlacementRecord>();
                save = parsed;
                return true;
            }
            catch (Exception ex)
            {
                Debug.unityLogger.LogError(TAG, $"Slot {slot}: load failed ({ex.GetType().Name}: {ex.Message}).");
                return false;
            }
        }

        // Atomic write: serialize to <slot>.json.tmp, then rename over
        // the final path. Crash mid-write leaves the previous file
        // intact; the temp file gets cleaned up on the next save.
        public static bool Save(int slot, ConstructSave save)
        {
            if (!IsValidSlot(slot))
            {
                Debug.unityLogger.LogError(TAG, $"Save rejected: invalid slot {slot}.");
                return false;
            }
            if (save == null)
            {
                Debug.unityLogger.LogError(TAG, $"Save rejected for slot {slot}: null payload.");
                return false;
            }

            EnsureDirectory();
            save.version = ConstructSave.CurrentVersion;
            if (save.createdUtcTicks == 0) save.createdUtcTicks = DateTime.UtcNow.Ticks;
            save.modifiedUtcTicks = DateTime.UtcNow.Ticks;

            string finalPath = SlotPath(slot);
            string tempPath = finalPath + ".tmp";

            try
            {
                string json = JsonUtility.ToJson(save, prettyPrint: true);
                File.WriteAllText(tempPath, json);

                // Atomic replace: File.Replace overwrites destination
                // with source as a single rename on Windows and most
                // POSIX filesystems (no window where the destination
                // is missing). It requires the destination to exist,
                // so the first-time-save path falls back to Move.
                //
                // File.Replace can throw PlatformNotSupportedException
                // on some Unity runtimes (e.g. sandboxed mobile FSes,
                // certain WebGL contexts) or IOException if the
                // destination is mid-rotation. Fall back to a
                // rename-existing-to-bak pattern in those cases so we
                // never end up with neither the old NOR the new save.
                if (File.Exists(finalPath))
                {
                    try
                    {
                        File.Replace(tempPath, finalPath, destinationBackupFileName: null);
                    }
                    catch (Exception replaceEx) when (replaceEx is PlatformNotSupportedException
                                                   || replaceEx is IOException
                                                   || replaceEx is UnauthorizedAccessException)
                    {
                        Debug.unityLogger.LogWarning(TAG,
                            $"Slot {slot}: File.Replace failed ({replaceEx.GetType().Name}); using rename-to-bak fallback.");
                        AtomicReplaceFallback(tempPath, finalPath);
                    }
                }
                else
                {
                    File.Move(tempPath, finalPath);
                }

                Debug.unityLogger.Log(TAG,
                    $"Slot {slot}: saved {save.placements?.Length ?? 0} placement(s) to '{finalPath}'.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.unityLogger.LogError(TAG, $"Slot {slot}: save failed ({ex.GetType().Name}: {ex.Message}).");
                // Best-effort cleanup of the temp file. The original
                // finalPath (if it existed) is untouched because we
                // never Delete'd it before the replace attempt.
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* ignore */ }
                return false;
            }
        }

        public static bool Delete(int slot)
        {
            if (!IsValidSlot(slot)) return false;
            string path = SlotPath(slot);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Debug.unityLogger.Log(TAG, $"Slot {slot}: deleted '{path}'.");
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.unityLogger.LogError(TAG, $"Slot {slot}: delete failed ({ex.GetType().Name}: {ex.Message}).");
                return false;
            }
        }

        // Used when File.Replace isn't supported on the host
        // filesystem. Three phases:
        //   1. Move existing finalPath aside to <finalPath>.bak
        //   2. Move tempPath to finalPath
        //   3. Delete the .bak
        // If phase 2 throws, we restore from .bak in the catch so
        // the previous save is preserved. The window where neither
        // file is in the canonical slot path is shorter than the
        // original Delete+Move approach, and we always retain the
        // .bak as a recovery target until phase 3 completes.
        static void AtomicReplaceFallback(string tempPath, string finalPath)
        {
            string bakPath = finalPath + ".bak";
            // Drop any stale bak from a prior failed save.
            if (File.Exists(bakPath))
            {
                try { File.Delete(bakPath); }
                catch (Exception ex)
                {
                    Debug.unityLogger.LogWarning(TAG, $"Couldn't delete stale '{bakPath}': {ex.Message}");
                }
            }

            File.Move(finalPath, bakPath);
            try
            {
                File.Move(tempPath, finalPath);
                // Promote complete. Drop the bak.
                try { File.Delete(bakPath); } catch { /* not fatal */ }
            }
            catch
            {
                // Restore: best-effort move .bak back to final so the
                // previous save isn't lost.
                if (!File.Exists(finalPath) && File.Exists(bakPath))
                {
                    try { File.Move(bakPath, finalPath); }
                    catch (Exception restoreEx)
                    {
                        Debug.unityLogger.LogError(TAG,
                            $"Restore from bak failed: {restoreEx.Message}. Manual recovery: {bakPath}");
                    }
                }
                throw;
            }
        }

        static void EnsureDirectory()
        {
            string dir = SaveDirectory;
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                Debug.unityLogger.LogError(TAG, $"Could not create save directory '{dir}': {ex.Message}");
            }
        }
    }
}
