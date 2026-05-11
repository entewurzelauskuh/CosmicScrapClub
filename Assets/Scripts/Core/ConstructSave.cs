using System;
using UnityEngine;

namespace CubeFly.Core
{
    // Persistent on-disk format for a saved construct. Designed for
    // Unity's JsonUtility — every member must be a [Serializable]
    // primitive / Unity type, and dictionaries are not supported.
    //
    // Schema rules (v1):
    //   • shape and material are stored by NAME, not by registry index,
    //     so reordering ShapeRegistry / MaterialRegistry doesn't break
    //     existing saves.
    //   • totalMass / totalHealthPoints are denormalised — derived from
    //     `placements` but cached so the slot-picker UI can show them
    //     without instantiating GameObjects. Treat the placement list
    //     as authoritative on load and recompute these.
    //   • Unknown JSON fields are tolerated by JsonUtility (ignored).
    //   • A version > knownVersion is treated as "save from a newer
    //     build" and refused.

    [Serializable]
    public class ConstructSave
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string slotName = string.Empty;
        public long createdUtcTicks;
        public long modifiedUtcTicks;
        public float totalMass;
        public float totalHealthPoints;

        public PlacementRecord[] placements = Array.Empty<PlacementRecord>();
    }

    [Serializable]
    public struct PlacementRecord
    {
        public Vector3Int cell;
        public string shape;
        public string material;
        public Vector3 rotEuler;
    }

    // Slim view of a slot used by the hangar-select UI. Built once per
    // slot when the picker scene loads, never written back to disk.
    public readonly struct SaveSlotInfo
    {
        public readonly int Slot;
        public readonly bool IsEmpty;
        public readonly string Name;
        public readonly int CubeCount;
        public readonly float TotalMass;
        public readonly float TotalHealthPoints;
        public readonly DateTime ModifiedUtc;

        public SaveSlotInfo(int slot, bool isEmpty, string name, int cubeCount,
            float totalMass, float totalHealthPoints, DateTime modifiedUtc)
        {
            Slot = slot;
            IsEmpty = isEmpty;
            Name = name;
            CubeCount = cubeCount;
            TotalMass = totalMass;
            TotalHealthPoints = totalHealthPoints;
            ModifiedUtc = modifiedUtc;
        }

        public static SaveSlotInfo Empty(int slot)
            => new SaveSlotInfo(slot, true, $"Slot {slot + 1}", 0, 0f, 0f, default);

        public static SaveSlotInfo From(int slot, ConstructSave save)
        {
            return new SaveSlotInfo(
                slot,
                false,
                string.IsNullOrEmpty(save.slotName) ? $"Slot {slot + 1}" : save.slotName,
                save.placements != null ? save.placements.Length : 0,
                save.totalMass,
                save.totalHealthPoints,
                SafeDateTimeFromTicks(save.modifiedUtcTicks));
        }

        // DateTime's constructor throws ArgumentOutOfRangeException for
        // ticks outside [DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks].
        // A corrupt save file with a junk integer in modifiedUtcTicks
        // would crash the hangar picker on load — clamp to the valid
        // range first and fall back to `default` when the value is out
        // of range. Belt-and-braces try/catch covers any platform
        // weirdness around boundary values.
        static DateTime SafeDateTimeFromTicks(long ticks)
        {
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                return default;
            try { return new DateTime(ticks, DateTimeKind.Utc); }
            catch (ArgumentOutOfRangeException) { return default; }
        }
    }
}
