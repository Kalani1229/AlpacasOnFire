using AlpacasOnFire.Core;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 擺攤的幾何運算與地面檢測。
    ///
    /// 這裡全部是 static 純函式，**沒有任何隨機、沒有讀取 Time.deltaTime**，
    /// 輸入相同就一定得到相同結果。這點很重要：本機的幽靈預覽與狀態權威的實際放置
    /// 走的是同一組函式、餵同一組 [Networked] 推導出來的參數（頭部位置與 AimDirection），
    /// 所以玩家看到的位置就是真的會放下去的位置，不需要額外同步一份「預覽座標」。
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

        /// <summary>點是否落在襯布範圍內（margin 讓機台中心離邊緣留一點距離）。</summary>
        public static bool InsideMat(Vector3 world, Vector3 center, float yaw, float size, float margin)
        {
            var local = WorldToMat(world, center, yaw);
            float limit = size * 0.5f - margin;
            return Mathf.Abs(local.x) <= limit && Mathf.Abs(local.z) <= limit;
        }

        /// <summary>
        /// 展開襯布的預定位置：玩家前方一段距離，朝向對齊玩家當下的 yaw。
        /// </summary>
        public static void PlannedMat(Vector3 playerPos, float playerYaw, out Vector3 center, out float yaw)
        {
            yaw = playerYaw;
            var forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float distance = GameTuning.StallMatDeployDistance + GameTuning.StallMatSize * 0.5f;
            center = playerPos + forward * distance;
            center.y = playerPos.y;
        }

        /// <summary>
        /// 從準心投影到襯布平面上的點。回傳 false 代表準心朝天上、根本打不到地面。
        /// </summary>
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

        /// <summary>
        /// 檢查一塊方形範圍下方的地面夠不夠平：四角 + 中心往下射線，
        /// 全部要打得到、法線角度夠小、而且彼此高低差不能太大。
        /// </summary>
        public static PlacementResult CheckGround(Vector3 center, float yaw, float size, out float groundY)
        {
            groundY = center.y;

            float inset = Mathf.Min(GameTuning.StallDeployProbeInset, size * 0.4f);
            float h = size * 0.5f - inset;
            var rot = Quaternion.Euler(0f, yaw, 0f);

            float minY = float.MaxValue, maxY = float.MinValue;

            // 中心 + 四角，用固定的單位偏移乘上半徑，避免每次呼叫都配置陣列
            for (int i = 0; i < ProbeOffsets.Length; i++)
            {
                var offset = new Vector3(ProbeOffsets[i].x * h, 0f, ProbeOffsets[i].y * h);
                var probe = center + rot * offset + Vector3.up * GameTuning.StallGroundProbeHeight;
                if (!Physics.Raycast(probe, Vector3.down, out var hit,
                                     GameTuning.StallGroundProbeLength, ~0, QueryTriggerInteraction.Ignore))
                    return PlacementResult.NoGround;

                if (Vector3.Angle(hit.normal, Vector3.up) > GameTuning.StallMaxGroundAngle)
                    return PlacementResult.GroundTooSteep;

                if (i == 0) groundY = hit.point.y;
                if (hit.point.y < minY) minY = hit.point.y;
                if (hit.point.y > maxY) maxY = hit.point.y;
            }

            if (maxY - minY > GameTuning.StallMaxGroundStep)
                return PlacementResult.GroundTooSteep;

            return PlacementResult.Ok;
        }

        /// <summary>把驗證結果翻成給玩家看的一句話。</summary>
        public static string Describe(PlacementResult result) => result switch
        {
            PlacementResult.Ok             => "可以放置",
            PlacementResult.OutsideMat     => "超出襯布範圍",
            PlacementResult.Overlapping    => "和其他機台太近",
            PlacementResult.GroundTooSteep => "地面不夠平",
            PlacementResult.NoGround       => "下方沒有地面",
            PlacementResult.NotDeploying   => "營業中不能移動機台",
            PlacementResult.NothingPending => "沒有選擇要放的機台",
            _                              => "無法放置",
        };
    }
}
