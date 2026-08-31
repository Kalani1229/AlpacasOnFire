using AlpacasOnFire.Core;
using Fusion;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 佈局裡的一台裝備：裝備種類 + 格子座標 + 朝向。
    ///
    /// 全部是整數，所以放進 NetworkArray 同步時不會有浮點誤差，
    /// 每個用戶端推算出來的世界座標一定一致。
    ///
    /// 這份結構同時是「手提箱記住的佈局」的最小單位 ——
    /// 收攤時把場上的裝備寫回一份這種陣列，下次開箱再照著彈出來。
    /// </summary>
    public struct StallSlotRecord : INetworkStruct
    {
        public int DeviceTypeRaw;
        public int CellX;
        public int CellZ;
        public int Facing;     // 0~3，見 StallFacing

        public bool IsValid => DeviceTypeRaw != 0;
        public LevelElementType DeviceType => (LevelElementType)DeviceTypeRaw;

        public static StallSlotRecord Create(LevelElementType type, int cellX, int cellZ, int facing)
        {
            return new StallSlotRecord
            {
                DeviceTypeRaw = (int)type,
                CellX = cellX,
                CellZ = cellZ,
                Facing = StallGrid.NormalizeFacing(facing),
            };
        }

        public override string ToString()
            => $"{DeviceType}@({CellX},{CellZ}) 朝{StallGrid.FacingName(Facing)}";
    }
}
