using UnityEngine;
using UnityEngine.AI;

namespace AlpacasOnFire.Map
{
    /// <summary>
    /// NavMesh 的共用查詢。**只做查詢，不做移動** —— 這個專案不用 NavMeshAgent。
    ///
    /// NavMeshAgent 會直接寫 transform，跟 Fusion 的重模擬打架；裸 CharacterController
    /// 與 NetworkTransform 已經在這個坑裡踩過兩次（症狀：幾秒後開始抖動、瞬間大步移動）。
    /// 所以 NavMesh 只拿來算路徑與落點，算出來的方向照舊餵進 NetworkCharacterController。
    ///
    /// **沒有 NavMesh 的場景（Stall_Test）全部退回原本的行為**：
    /// HasNavMesh 為 false 時，所有呼叫端都跳過 NavMesh 的判斷。
    /// </summary>
    public static class NavUtil
    {
        /// <summary>
        /// 烤 NavMesh 用的 agent 半徑。羊駝半徑 0.35，大動物是兩倍 ——
        /// 只烤一份給最大的那隻，大動物過得去的地方小的一定過得去。
        /// </summary>
        public const float AgentRadius = 0.7f;
        public const float AgentHeight = 2.0f;
        public const float AgentClimb  = 0.45f;   // 跨得上廣場地面、路緣這種小高差
        public const float AgentSlope  = 40f;

        private static bool _hasSettings;
        private static NavMeshBuildSettings _settings;

        /// <summary>
        /// 這一局用的 agent 設定（執行期建立，半徑 0.7，不改 ProjectSettings）。
        ///
        /// **烤與查詢用的是同一個設定物件**：建立一次就一直沿用，不再用 ID 回頭查 ——
        /// 烤用一個 agent type、查詢用另一個的話，什麼都查不到（道路抽樣 0/20 就是這個症狀之一）。
        /// </summary>
        public static NavMeshBuildSettings Settings
        {
            get
            {
                if (_hasSettings) return _settings;
                _settings = NavMesh.CreateSettings();
                _settings.agentRadius = AgentRadius;
                _settings.agentHeight = AgentHeight;
                _settings.agentClimb = AgentClimb;
                _settings.agentSlope = AgentSlope;
                _hasSettings = true;
                return _settings;
            }
        }

        public static int AgentTypeId => Settings.agentTypeID;

        public static NavMeshQueryFilter Filter => new NavMeshQueryFilter
        {
            agentTypeID = AgentTypeId,
            areaMask = NavMesh.AllAreas,
        };

        /// <summary>這一局有沒有烤好的 NavMesh（RandomMapBuilder 烤完會設成 true）。</summary>
        public static bool HasNavMesh { get; internal set; }

        /// <summary>關掉 domain reload 時，靜態欄位會留到下一次 Play —— 每次進 Play 先清掉。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _hasSettings = false;
            HasNavMesh = false;
        }

        /// <summary>
        /// 把一個想去的點落到最近的 NavMesh 上。沒有 NavMesh 時原樣回傳 true。
        /// radius 不要太大：取樣點在地面高度，半徑小於建築高度就不會落到屋頂上。
        /// </summary>
        public static bool SnapToNavMesh(Vector3 wish, float radius, out Vector3 result, float maxRise = 1.5f)
        {
            result = wish;
            if (!HasNavMesh) return true;

            if (!NavMesh.SamplePosition(wish, out var hit, radius, Filter)) return false;

            // 落點比想去的點高太多就不算（避免落到任何高處的小平台上）
            if (hit.position.y - wish.y > maxRise) return false;

            result = hit.position;
            return true;
        }

        /// <summary>這一點 tolerance 公尺內有沒有 NavMesh。沒有 NavMesh 的場景一律回 true。</summary>
        public static bool IsOnNavMesh(Vector3 point, float tolerance)
        {
            if (!HasNavMesh) return true;
            return NavMesh.SamplePosition(point, out _, tolerance, Filter);
        }

        /// <summary>
        /// 直線移動的防撞：往前看 lookAhead 公尺，前面是牆就改成**沿著牆面滑過去**。
        ///
        /// 用在所有「不走路徑」的直線移動：羊逃跑（規格要求背對玩家直線跑）、
        /// 大動物逃跑（方向取樣挑完之後是直線）、路徑算不出來時的直線備援。
        /// 不改方向怎麼選，只改「撞到牆之後怎麼辦」—— 原本是正面頂著牆一直推，
        /// 現在是順著牆滑開。大動物被逼到角落時（兩面都是牆）還是會卡在角落，那是刻意的。
        ///
        /// 沒有 NavMesh 的場景原樣回傳。
        /// </summary>
        public static Vector3 SlideAlongWalls(Vector3 position, Vector3 dir, float lookAhead = 1.5f)
        {
            dir.y = 0f;
            if (!HasNavMesh || dir.sqrMagnitude < 0.0001f) return dir;
            dir.Normalize();

            // 起點先落到 NavMesh 上，不然 Raycast 會在起點就「撞到」
            if (!NavMesh.SamplePosition(position, out var start, 1.5f, Filter)) return dir;
            if (!NavMesh.Raycast(start.position, start.position + dir * lookAhead, out var hit, Filter)) return dir;

            // 撞到了：把方向投影到牆面上（去掉往牆裡推的那一份）
            var normal = hit.normal; normal.y = 0f;
            if (normal.sqrMagnitude < 0.0001f) return dir;
            normal.Normalize();

            var slide = dir - Vector3.Dot(dir, normal) * normal;
            if (slide.sqrMagnitude < 0.04f)
            {
                // 幾乎正面撞上：投影後剩不多，順著牆隨便挑一邊走（固定挑一邊，不要左右抖）
                slide = Vector3.Cross(Vector3.up, normal);
            }
            return slide.normalized;
        }

        /// <summary>
        /// 這一點周圍夠不夠開闊：以 spacing 公尺為間距往 directions 個方向取樣，
        /// 至少 required 個方向在 NavMesh 上。
        /// </summary>
        public static bool IsOpenAround(Vector3 center, float spacing = 5f, int directions = 8, int required = 5)
        {
            if (!HasNavMesh) return true;
            int ok = 0;
            for (int i = 0; i < directions; i++)
            {
                var dir = Quaternion.Euler(0f, 360f / directions * i, 0f) * Vector3.forward;
                if (NavMesh.SamplePosition(center + dir * spacing, out _, 1f, Filter)) ok++;
            }
            return ok >= required;
        }
    }
}
