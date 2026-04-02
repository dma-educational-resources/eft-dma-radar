using eft_dma_radar.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Misc;
using eft_dma_radar.Tarkov.Unity;
using eft_dma_radar.Tarkov.Unity.Collections;

namespace eft_dma_radar.Tarkov.GameWorld
{
    public sealed class WorldInteractablesManager
    {
        private readonly ulong _localGameWorld;
        public readonly HashSet<Door> _Doors;

        public WorldInteractablesManager(ulong localGameWorld)
        {
            _localGameWorld = localGameWorld;
            _Doors = new();
            Init();
        }

        public void Init()
        {
            ulong world = 0;
            ulong interactableArrayPtr = 0;

            try
            {

                world = Memory.ReadPtr(_localGameWorld + Offsets.ClientLocalGameWorld.World, true);

                if (!world.IsValidVirtualAddress())
                    return;

                interactableArrayPtr = Memory.ReadPtr(world + 0x30, true);

                if (!interactableArrayPtr.IsValidVirtualAddress())
                    return;

                using var array = MemArray<ulong>.Get(interactableArrayPtr, true);
                foreach (var item in array)
                {
                    if (item == 0x0)
                        continue;
                    var itemName = ObjectClass.ReadName(item);
                    if (itemName == "Door")
                        _Doors.Add(new Door(item));
                }
            }
            catch { }
        }


        public void Refresh()
        {
            if (_Doors.Count == 0)
                Init();

            foreach (var door in _Doors)
            {
                door.Refresh();
            }
        }
    }
}
