using AlpacasOnFire.Core;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 襯布的網格座標系統（PlateUp! 式）。
    ///
    /// 座標定義：格子編號是 0 到 StallGridCells-1 的整數，(0,0) 在襯布的「左後角」，
    /// x 往右增加、z 往前增加。**裝備的位置一律用這組整數同步**，世界座標一律由這裡推算——
    /// 整數不會有浮點誤差，所以每個用戶端算出來的位置一定是同一個點，
    /// 也不需要為裝備掛 NetworkTransform。
    ///
    /// 這裡全部是 static 純函式，本機預覽與狀態權威呼叫的是同一組，
    /// 所以玩家看到高亮的那一格就是真的會放進去的那一格。
    /// </summary>
    public static class StallGrid
    {
        public const int Cells = GameTuning.StallGridCells;
        public const int CellCount = Cells * Cells;

        public static float CellSize => GameTuning.StallCellSize;
        public static float MatSize => GameTuning.StallMatSize;

        // ---------------- 座標換算 ----------------

        /// <summary>格子中心在襯布本地座標中的位置（y = 0）。</summary>
        public static Vector3 CellToLocal(int cx, int cz)
        {
            float half = MatSize * 0.5f;
            return new Vector3(
                -half + (cx + 0.5f) * CellSize,
                0f,
                -half + (cz + 0.5f) * CellSize);
        }

        /// <summary>格子中心的世界座標。</summary>
        public static Vector3 CellToWorld(int cx, int cz, Vector3 matCenter, float matYaw)
            => matCenter + Quaternion.Euler(0f, matYaw, 0f) * CellToLocal(cx, cz);

        /// <summary>
        /// 世界座標落在哪一格。回傳 false 代表在襯布外面（此時 cx/cz 仍然是「最接近的那一格」，
        /// 沒有被夾住，可以用來判斷是超出哪一邊）。
        /// </summary>
        public static bool WorldToCell(Vector3 world, Vector3 matCenter, float matYaw,
                                       out int cx, out int cz)
        {
            var local = Quaternion.Euler(0f, -matYaw, 0f) * (world - matCenter);
            float half = MatSize * 0.5f;

            cx = Mathf.FloorToInt((local.x + half) / CellSize);
            cz = Mathf.FloorToInt((local.z + half) / CellSize);

            return InBounds(cx, cz);
        }

        public static bool InBounds(int cx, int cz)
            => cx >= 0 && cx < Cells && cz >= 0 && cz < Cells;

        /// <summary>把格子編號夾回襯布範圍內（幽靈模型在邊界外時仍然要有東西可顯示）。</summary>
        public static void Clamp(ref int cx, ref int cz)
        {
            cx = Mathf.Clamp(cx, 0, Cells - 1);
            cz = Mathf.Clamp(cz, 0, Cells - 1);
        }

        // ---------------- 朝向 ----------------

        public static float FacingToYaw(int facing) => NormalizeFacing(facing) * 90f;

        public static int NormalizeFacing(int facing)
        {
            int f = facing % 4;
            return f < 0 ? f + 4 : f;
        }

        public static Quaternion FacingToRotation(int facing, float matYaw)
            => Quaternion.Euler(0f, matYaw + FacingToYaw(facing), 0f);

        public static string FacingName(int facing) => NormalizeFacing(facing) switch
        {
            0 => "前",
            1 => "右",
            2 => "後",
            _ => "左",
        };

        // ---------------- 佔地 ----------------

        /// <summary>
        /// 一台裝備旋轉之後實際佔用幾格。footprint 是「朝向 0 時」的 (寬, 深)，
        /// 轉 90 度或 270 度時寬深互換。
        /// v1 全部都是 1x1，但整條路徑都照 w x d 算，之後要加 2x1 的交貨櫃台不用改邏輯。
        /// </summary>
        public static void RotatedFootprint(Vector2Int footprint, int facing, out int w, out int d)
        {
            int f = NormalizeFacing(facing);
            bool swap = f == 1 || f == 3;
            w = swap ? footprint.y : footprint.x;
            d = swap ? footprint.x : footprint.y;
        }

        /// <summary>
        /// 佔地的左後角格子。錨點格 (cx,cz) 是裝備的「中心格」，
        /// 寬深是偶數時往負方向多佔一格（這個規則只要前後一致就好）。
        /// </summary>
        public static void FootprintOrigin(int cx, int cz, int w, int d, out int ox, out int oz)
        {
            ox = cx - (w - 1) / 2;
            oz = cz - (d - 1) / 2;
        }

        /// <summary>裝備實際覆蓋的所有格子是否都在襯布內。</summary>
        public static bool FootprintInBounds(int cx, int cz, int w, int d)
        {
            FootprintOrigin(cx, cz, w, d, out int ox, out int oz);
            return ox >= 0 && oz >= 0 && ox + w <= Cells && oz + d <= Cells;
        }

        // ---------------- 佔用表 ----------------

        /// <summary>
        /// 格子佔用表。用一張表做重疊判定，不再用碰撞體互相檢查 ——
        /// 碰撞體的判定會受模型外框影響、而且兩端可能算出不同結果；
        /// 整數格子表是完全確定性的。
        /// </summary>
        public struct Occupancy
        {
            // 6x6 = 36 格，塞得進一個 ulong。格數若加大到超過 8x8 就要改成陣列。
            private ulong _bits;

            public void Clear() => _bits = 0UL;

            public bool IsOccupied(int cx, int cz)
            {
                if (!InBounds(cx, cz)) return true;   // 界外一律視為不可用
                return (_bits & (1UL << Index(cx, cz))) != 0UL;
            }

            public void Occupy(int cx, int cz)
            {
                if (!InBounds(cx, cz)) return;
                _bits |= 1UL << Index(cx, cz);
            }

            public void OccupyFootprint(int cx, int cz, int w, int d)
            {
                FootprintOrigin(cx, cz, w, d, out int ox, out int oz);
                for (int x = ox; x < ox + w; x++)
                    for (int z = oz; z < oz + d; z++)
                        Occupy(x, z);
            }

            /// <summary>這台裝備放得下去嗎（界內 + 每一格都空著）。</summary>
            public PlacementResult Check(int cx, int cz, int w, int d)
            {
                if (!FootprintInBounds(cx, cz, w, d)) return PlacementResult.OutsideMat;

                FootprintOrigin(cx, cz, w, d, out int ox, out int oz);
                for (int x = ox; x < ox + w; x++)
                    for (int z = oz; z < oz + d; z++)
                        if (IsOccupied(x, z)) return PlacementResult.CellOccupied;

                return PlacementResult.Ok;
            }

            /// <summary>找一個放得下的空格（開箱時佈局資料有衝突才會用到，當作保底）。</summary>
            public bool TryFindFree(int w, int d, out int cx, out int cz)
            {
                for (int z = 0; z < Cells; z++)
                {
                    for (int x = 0; x < Cells; x++)
                    {
                        if (Check(x, z, w, d) == PlacementResult.Ok)
                        {
                            cx = x; cz = z;
                            return true;
                        }
                    }
                }
                cx = 0; cz = 0;
                return false;
            }

            private static int Index(int cx, int cz) => cz * Cells + cx;
        }
    }
}
