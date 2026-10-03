using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Networking;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 夜晚的床（債主 P2，取代原本的發光柱子 NightMarker）。
    ///
    /// **每個玩家自己躺下**：互動床 -> 自己的 PlayerController.IsAsleep = true；
    /// 所有人都睡了，StallManager 才呼叫 EndNight()。起床是躺著時再按一次左鍵（PlayerController 處理）。
    ///
    /// 位置固定在**第一個出生點** —— 出生點就是之後的「家」。城市是程序生成的，
    /// 出生點由 GameLauncher 落位到第一個廣場，床跟著它走。
    ///
    /// 跟之前的柱子一樣是**本機物件**：每一端依同步的狀態自己生出來，
    /// 互動只會在狀態權威那一份上被呼叫（ctx.Player 就是按的人），不需要 RPC、不需要 prefab。
    /// 碰撞體是 trigger —— 只給準心偵測用，不擋路（床就擺在出生點，擋路的話會卡人）。
    ///
    /// 大額日的夜晚，大哥（<see cref="CreditorStandIn"/>）站在床前，
    /// 還沒跟他說過話之前床拒絕互動。
    /// </summary>
    public class Bed : MonoBehaviour, IInteractable
    {
        private static readonly Vector3 BedSize = new Vector3(1.1f, 0.45f, 2.1f);

        private static Bed _instance;
        private static bool _warnedNoSpawn;

        /// <summary>床頭朝向（床尾對著廣場中心，大哥站在床尾外側）。</summary>
        public Vector3 Front { get; private set; } = Vector3.forward;

        /// <summary>每幀由 StallManager.Render() 呼叫：夜晚（與收工後的結算畫面）存在，其他時候收掉。</summary>
        public static void Sync(StallManager stall)
        {
            bool want = stall != null && stall.RunMode
                        && (stall.IsNight || stall.State == StallState.Settling);

            if (!want)
            {
                DestroyInstance();
                return;
            }

            if (_instance == null) _instance = Create(stall);

            // 大哥：只在大額日的夜晚
            CreditorStandIn.Sync(stall, _instance);
        }

        public static void DestroyInstance()
        {
            CreditorStandIn.DestroyInstance();
            if (_instance != null) Destroy(_instance.gameObject);
            _instance = null;
        }

        // ---------------------------------------------------------------- 位置

        /// <summary>
        /// 「家」的位置與朝向（床擺在這裡，賭場櫃檯也借這個位置擺在床旁邊）。
        /// front = 從家看向第一個廣場中心的方向。每一端算出來都一樣（出生點與城市都是確定的）。
        /// </summary>
        public static void HomeFrame(StallManager stall, out Vector3 position, out Vector3 front)
            => Place(stall, out position, out front);

        /// <summary>
        /// 第一個出生點。拿不到的話退回舊的算法（襯布邊上／第一個廣場），並警告一次。
        /// 朝向：床尾對著第一個廣場中心（大家從那邊走過來）；沒有城市就朝 +Z。
        /// </summary>
        private static void Place(StallManager stall, out Vector3 position, out Vector3 front)
        {
            var map = FindAnyObjectByType<Map.RandomMapBuilder>();
            Vector3? plaza = map != null && map.PlazaCenters.Count > 0 ? map.PlazaCenters[0] : null;

            if (SpawnPointRegistry.TryGetFirst(out position))
            {
                // 出生點
            }
            else
            {
                if (!_warnedNoSpawn)
                {
                    _warnedNoSpawn = true;
                    Debug.LogWarning("[床] 拿不到出生點，改用舊的位置算法（襯布邊上或第一個廣場旁邊）。");
                }
                position = FallbackPosition(stall, map);
            }

            front = Vector3.forward;
            if (plaza.HasValue)
            {
                var d = plaza.Value - position; d.y = 0f;
                if (d.sqrMagnitude > 0.01f) front = d.normalized;
            }
        }

        /// <summary>原本 NightMarker.PositionFor() 的算法。</summary>
        private static Vector3 FallbackPosition(StallManager stall, Map.RandomMapBuilder map)
        {
            if (stall.MatDeployed)
                return StallGeometry.BellRestPosition(stall.MatCenter, stall.MatYaw);

            if (map != null && map.PlazaCenters.Count > 0)
            {
                var wish = map.PlazaCenters[0] + Vector3.back * 4f;
                return Map.NavUtil.SnapToNavMesh(wish, 3f, out var p) ? p : map.PlazaCenters[0];
            }
            return new Vector3(0f, 0f, -4f);
        }

        // ---------------------------------------------------------------- 外觀

        private static Bed Create(StallManager stall)
        {
            Place(stall, out var pos, out var front);

            var root = new GameObject("[Bed]");
            root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-front, Vector3.up));

            // 床身、被子、枕頭：佔位幾何。床頭在 local +Z（背對廣場），床尾對著廣場
            Box(root.transform, "Frame", new Vector3(0f, BedSize.y * 0.5f, 0f), BedSize,
                new Color(0.45f, 0.30f, 0.18f));
            Box(root.transform, "Blanket", new Vector3(0f, BedSize.y + 0.04f, -0.2f),
                new Vector3(BedSize.x * 0.96f, 0.08f, BedSize.z * 0.7f), new Color(0.32f, 0.45f, 0.78f));
            Box(root.transform, "Pillow", new Vector3(0f, BedSize.y + 0.08f, BedSize.z * 0.5f - 0.3f),
                new Vector3(BedSize.x * 0.75f, 0.14f, 0.38f), new Color(0.95f, 0.94f, 0.9f));

            // 互動用的 trigger（不擋路）
            var col = root.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, 0.6f, 0f);
            col.size = new Vector3(BedSize.x + 0.6f, 1.2f, BedSize.z + 0.6f);

            // 夜裡整個畫面都暗了：床上方一盞暖色小燈，回家找得到
            var lightGo = new GameObject("NightLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.2f, 0.4f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 7f;
            light.intensity = 2.2f;
            light.color = new Color(1f, 0.82f, 0.55f);
            light.shadows = LightShadows.None;

            var bed = root.AddComponent<Bed>();
            bed.Front = front;
            return bed;
        }

        private static void Box(Transform parent, string name, Vector3 localPos, Vector3 size, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;

            var r = go.GetComponent<Renderer>();
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            r.SetPropertyBlock(mpb);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---------------------------------------------------------------- IInteractable

        public Transform InteractionAnchor => transform;

        /// <summary>跟開張鈴同級。</summary>
        public int InteractionPriority => 3;

        public bool CanInteract(in InteractionContext ctx)
        {
            var stall = StallManager.Instance;
            // 被擋住也回 true，提示要說得出「他擋在前面」
            return ctx.Player != null && stall != null && stall.IsNight && !ctx.Player.IsAsleep;
        }

        public string GetPrompt(in InteractionContext ctx)
        {
            if (!CanInteract(in ctx)) return null;
            var stall = StallManager.Instance;
            if (!stall.BedUnlocked) return "他擋在前面，先跟他說話";
            return "[左鍵] 上床睡覺";
        }

        /// <summary>只會在狀態權威那一份上被呼叫。每個玩家都能躺，躺的是按的那個人。</summary>
        public void Interact(in InteractionContext ctx)
        {
            var stall = StallManager.Instance;
            if (stall == null || !stall.HasStateAuthority || !stall.IsNight) return;
            if (ctx.Player == null || ctx.Player.Object == null || ctx.Player.IsAsleep) return;

            if (!stall.BedUnlocked)
            {
                stall.NoticeTo(ctx.Player.Object.InputAuthority, "他擋在前面，先跟他說話");
                return;
            }

            ctx.Player.IsAsleep = true;
            GameAudio.PlayAt(SfxId.Pickup, transform.position);
        }
    }

    /// <summary>
    /// 大哥（佔位）。**只是一個會擋路的方塊** —— 形象、物種、個性、台詞都還沒定，這裡一個都不做。
    ///
    /// 只在大額日的夜晚出現，站在床尾外側（床和走過來的玩家之間）。
    /// **不用碰撞體擋路**：他站在出生點旁邊，實體碰撞很容易把人卡在後面出不來。
    /// 「擋住」是規則層的：StallManager.CreditorCleared 為 false 時床拒絕互動。
    /// 跟他說過話之後他往旁邊讓開（還站在那裡，只是不再擋）。
    ///
    /// 本機物件，跟著同步的 State / TodayIsBalloon / CreditorCleared 生滅與移動。
    /// </summary>
    public class CreditorStandIn : MonoBehaviour, IInteractable
    {
        private const float Height = 2.4f;          // 比羊駝（1.8）高
        private const float FrontDistance = 2.0f;   // 離床中心多遠（床長 2.1，站在床尾外面一點）
        private const float StepAside = 2.2f;       // 讓開時往旁邊移多遠

        private static CreditorStandIn _instance;

        private Vector3 _blockPos;
        private Vector3 _asidePos;

        public static void Sync(StallManager stall, Bed bed)
        {
            bool want = stall.IsNight && stall.TodayIsBalloon && bed != null;
            if (!want)
            {
                DestroyInstance();
                return;
            }

            if (_instance == null) _instance = Create(bed);

            // 讓開：平滑地往旁邊走，不要瞬移（瞬移看起來像 bug）
            var goal = stall.CreditorCleared ? _instance._asidePos : _instance._blockPos;
            var t = _instance.transform;
            t.position = Vector3.MoveTowards(t.position, goal, 2.5f * Time.deltaTime);
        }

        public static void DestroyInstance()
        {
            if (_instance != null) Destroy(_instance.gameObject);
            _instance = null;
        }

        private static CreditorStandIn Create(Bed bed)
        {
            var front = bed.Front;
            var side = Vector3.Cross(Vector3.up, front).normalized;
            var block = bed.transform.position + front * FrontDistance;

            var root = new GameObject("[Creditor]");
            root.transform.SetPositionAndRotation(block, Quaternion.LookRotation(front, Vector3.up));

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.up * (Height * 0.5f);
            body.transform.localScale = new Vector3(1.1f, Height, 0.9f);

            var c = new Color(0.85f, 0.15f, 0.2f);   // 夜裡也看得出來的紅
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            body.GetComponent<Renderer>().SetPropertyBlock(mpb);

            // 互動用的 trigger（不擋路）
            var col = root.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = Vector3.up * (Height * 0.5f);
            col.size = new Vector3(1.6f, Height, 1.4f);

            var s = root.AddComponent<CreditorStandIn>();
            s._blockPos = block;
            s._asidePos = block + side * StepAside;
            return s;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---------------------------------------------------------------- IInteractable

        public Transform InteractionAnchor => transform;

        /// <summary>比床高一級 —— 他站在床前面，準心同時掃到兩個時要選到他。</summary>
        public int InteractionPriority => 4;

        public bool CanInteract(in InteractionContext ctx)
        {
            var stall = StallManager.Instance;
            return ctx.Player != null && !ctx.Player.IsAsleep
                   && stall != null && stall.IsNight && stall.TodayIsBalloon && !stall.CreditorCleared;
        }

        public string GetPrompt(in InteractionContext ctx)
            => CanInteract(in ctx) ? "[左鍵] 跟他說話" : null;

        /// <summary>只會在狀態權威那一份上被呼叫。任何一個玩家都能跟他說話，說完所有人的床都解鎖。</summary>
        public void Interact(in InteractionContext ctx)
        {
            var stall = StallManager.Instance;
            if (stall == null || !stall.HasStateAuthority) return;
            if (!CanInteract(in ctx)) return;
            stall.ClearCreditor();
        }
    }
}
