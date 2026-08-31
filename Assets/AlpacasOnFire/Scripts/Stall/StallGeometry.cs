using AlpacasOnFire.Core;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 開箱時的場地檢測與襯布幾何。
    ///
    /// **地面檢測只在開箱時做一次。** 襯布是一個平面，裝備站在這個平面上，
    /// 所以個別裝備放置時不需要再各自檢測地面 —— 那是上一版的做法，
    /// 既浪費、又會讓「同一塊襯布上有些格子放得下有些放不下」這種難解釋的狀況出現。
    ///
    /// 這裡全部是 static 純函式，沒有隨機、沒有讀 Time，輸入相同結果就相同。
    /// </summary>
    public static class StallGeometry
    {
        /// <summary>地面檢測的取樣點：中心 + 四角（單位化，實際使用時乘上半徑）。</summary>
        private static readonly Vector2[] ProbeOffsets =
        {
            new Vector2( 0f,  0f),
            new Vector2(-1f, -1f),
            new Vector2( 1f, -1f),
            new Vector2( 1f,  1f),
            new Vector2(-1f,  1f),
        };

        private static readonly Collider[] ClearanceBuffer = new Collider[32];

        /// <summary>襯布的四個角（世界座標，順序：左後、右後、右前、左前）。</summary>
        public static void MatCorners(Vector3 center, float yaw, float size, Vector3[] outCorners)
        {
            if (outCorners == null || outCorners.Length < 4) return;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float h = size * 0.5f;
            outCorners[0] = center + rot * new Vector3(-h, 0f, -h);
            outCorners[1] = center + rot * new Vector3( h, 0f, -h);
            outCorners[2] = center + rot * new Vector3( h, 0f,  h);
            outCorners[3] = center + rot * new Vector3(-h, 0f,  h);
        }

        /// <summary>世界座標換算成襯布的本地座標（襯布中心為原點、對齊襯布朝向）。</summary>
        public static Vector3 WorldToMat(Vector3 world, Vector3 center, float yaw)
            => Quaternion.Euler(0f, -yaw, 0f) * (world - center);

        /// <summary>展開襯布的預定位置：玩家前方一段距離，朝向對齊玩家當下的 yaw。</summary>
        public static void PlannedMat(Vector3 playerPos, float playerYaw, out Vector3 center, out float yaw)
        {
            yaw = playerYaw;
            var forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float distance = GameTuning.StallMatDeployDistance + GameTuning.StallMatSize * 0.5f;
            center = playerPos + forward * distance;
            center.y = playerPos.y;
        }

        /// <summary>手提箱擺在襯布背緣外面，不佔用任何格子。</summary>
        public static Vector3 SuitcaseRestPosition(Vector3 matCenter, float matYaw)
        {
            float back = GameTuning.StallMatSize * 0.5f + GameTuning.StallSuitcaseBackOffset;
            return matCenter + Quaternion.Euler(0f, matYaw, 0f) * new Vector3(0f, 0f, -back);
        }

        /// <summary>
        /// 開張鈴放在襯布背緣、手提箱旁邊。位置固定 —— 它是控制而不是裝備，
        /// 固定在同一個地方才找得到，也才不會被玩家不小心搬走。
        /// </summary>
        public static Vector3 BellRestPosition(Vector3 matCenter, float matYaw)
        {
            float back = GameTuning.StallMatSize * 0.5f + GameTuning.StallSuitcaseBackOffset;
            return matCenter + Quaternion.Euler(0f, matYaw, 0f)
                             * new Vector3(GameTuning.StallBellSideOffset, 0f, -back);
        }

        /// <summary>從準心投影到襯布平面上的點。回傳 false 代表準心朝天上、打不到地面。</summary>
        public static bool ProjectAim(Vector3 origin, Vector3 direction, Vector3 matCenter, out Vector3 point)
        {
            point = default;
            var plane = new Plane(Vector3.up, matCenter);
            var ray = new Ray(origin, direction.normalized);
            if (!plane.Raycast(ray, out float enter)) return false;
            if (enter < 0f) return false;
            if (enter > GameTuning.StallPlaceMaxDistance) enter = GameTuning.StallPlaceMaxDistance;
            point = ray.GetPoint(enter);
            return true;
        }

        // ---------------- 開箱檢測 ----------------

        /// <summary>
        /// 開箱檢測：整塊襯布的地面夠不夠平，上方夠不夠淨空。
        /// 這是唯一一次地面檢測，通過之後整塊襯布就是一個平面。
        /// </summary>
        public static PlacementResult CheckDeployArea(Vector3 center, float yaw, float size,
                                                      out float groundY, out string detail)
        {
            var ground = CheckGround(center, yaw, size, out groundY, out detail);
            if (ground != PlacementResult.Ok) return ground;

            var surface = new Vector3(center.x, groundY, center.z);
            return CheckClearance(surface, yaw, size, out detail);
        }

        /// <summary>
        /// 四角 + 中心往下射線：全部要打得到、法線角度夠小、彼此高低差不能太大。
        /// detail 會寫出實際量到的數字，方便 BuildReport 記錄與除錯。
        /// </summary>
        public static PlacementResult CheckGround(Vector3 center, float yaw, float size,
                                                  out float groundY, out string detail)
        {
            groundY = center.y;
            detail = null;

            float inset = Mathf.Min(GameTuning.StallDeployProbeInset, size * 0.4f);
            float h = size * 0.5f - inset;
            var rot = Quaternion.Euler(0f, yaw, 0f);

            float minY = float.MaxValue, maxY = float.MinValue, maxAngle = 0f;

            for (int i = 0; i < ProbeOffsets.Length; i++)
            {
                var offset = new Vector3(ProbeOffsets[i].x * h, 0f, ProbeOffsets[i].y * h);
                var probe = center + rot * offset + Vector3.up * GameTuning.StallGroundProbeHeight;

                if (!Physics.Raycast(probe, Vector3.down, out var hit,
                                     GameTuning.StallGroundProbeLength, ~0, QueryTriggerInteraction.Ignore))
                {
                    detail = $"第 {i} 個取樣點下方沒有地面";
                    return PlacementResult.NoGround;
                }

                float angle = Vector3.Angle(hit.normal, Vector3.up);
                if (angle > maxAngle) maxAngle = angle;

                if (angle > GameTuning.StallMaxGroundAngle)
                {
                    detail = $"第 {i} 個取樣點的坡度 {angle:F1}°，超過容許的 {GameTuning.StallMaxGroundAngle:F0}°";
                    return PlacementResult.GroundTooSteep;
                }

                if (i == 0) groundY = hit.point.y;
                if (hit.point.y < minY) minY = hit.point.y;
                if (hit.point.y > maxY) maxY = hit.point.y;
            }

            float step = maxY - minY;
            if (step > GameTuning.StallMaxGroundStep)
            {
                detail = $"四角高低差 {step:F2} 公尺，超過容許的 {GameTuning.StallMaxGroundStep:F2} 公尺";
                return PlacementResult.GroundTooSteep;
            }

            detail = $"最大坡度 {maxAngle:F1}°、高低差 {step:F2} 公尺";
            return PlacementResult.Ok;
        }

        /// <summary>
        /// 襯布上方要淨空：撞到牆、機台、其他攤位就攤不開。
        /// 玩家自己與地上的物品不算障礙（trigger 也不算）。
        /// </summary>
        public static PlacementResult CheckClearance(Vector3 groundCenter, float yaw, float size,
                                                     out string detail)
        {
            detail = null;

            float half = size * 0.5f - GameTuning.StallClearanceInset;
            var extents = new Vector3(half, GameTuning.StallClearanceHeight * 0.5f, half);
            var boxCenter = groundCenter + Vector3.up * (GameTuning.StallClearanceHeight * 0.5f + 0.15f);
            var rot = Quaternion.Euler(0f, yaw, 0f);

            int count = Physics.OverlapBoxNonAlloc(boxCenter, extents, ClearanceBuffer, rot,
                                                   ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var col = ClearanceBuffer[i];
                if (col == null) continue;
                if (IsIgnoredForClearance(col)) continue;

                detail = $"被「{col.transform.root.name}」擋住";
                return PlacementResult.Obstructed;
            }

            detail = "上方淨空";
            return PlacementResult.Ok;
        }

        /// <summary>
        /// 哪些東西不算障礙物：地板（法線朝上的靜態面通常是地板，用 layer 判斷太脆弱，
        /// 改成只要是角色與可攜帶物品就跳過）、玩家、以及攤位自己的東西。
        /// </summary>
        private static bool IsIgnoredForClearance(Collider col)
        {
            if (col.GetComponentInParent<Player.PlayerController>() != null) return true;
            if (col.GetComponentInParent<Items.CarriableItem>() != null) return true;
            if (col.GetComponentInParent<DeployableDevice>() != null) return true;

            // 地板：碰撞體頂面低於檢測起點就當作是地，不算障礙
            if (col.bounds.max.y <= col.bounds.min.y + 0.001f) return true;
            return col.bounds.max.y < 0.35f;
        }

        /// <summary>把驗證結果翻成給玩家看的一句話。</summary>
        public static string Describe(PlacementResult result) => result switch
        {
            PlacementResult.Ok             => "可以放置",
            PlacementResult.OutsideMat     => "超出襯布範圍",
            PlacementResult.CellOccupied   => "這一格已經有東西了",
            PlacementResult.GroundTooSteep => "地面不夠平",
            PlacementResult.NoGround       => "下方沒有地面",
            PlacementResult.NotDeploying   => "營業中不能移動裝備",
            PlacementResult.NothingPending => "沒有拿著任何裝備",
            PlacementResult.Obstructed     => "空間不夠，這裡攤不開",
            _                              => "無法放置",
        };
    }
}
