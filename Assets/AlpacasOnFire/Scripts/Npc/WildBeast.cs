using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.Player;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Npc
{
    /// <summary>
    /// 大動物。體型是 WoolNpc 的兩倍，身上九份毛一次全掉，**一個人抓不到**。
    ///
    /// 核心規則只有一條：**牠永遠往「離最近的玩家最遠」的方向跑，而且比玩家快。**
    /// 難度曲線是這條規則自己長出來的，程式裡沒有任何難度設定：
    ///
    ///   一個人 -> 牠永遠背對你，6.5 對 5.0，追不上。抓不到是正確的
    ///   兩個人 -> 站兩側，牠往 A 跑就被 B 逼回來，可以慢慢夾
    ///   四個人 -> 切斷退路，很快逼到角落
    ///
    /// **這是獨立元件，不是 WoolNpc 的一種模式。** 兩者唯一的共同點是「身上有毛」，
    /// 行為、體型、收網方式全都不同，硬塞進同一支會讓兩邊都變難讀。
    ///
    /// 也**刻意不進 WoolNpc.All** —— 那份清單是 CustomerQueue 抽顧客用的，
    /// 大動物被抽去排隊買衣服會非常荒謬。
    ///
    /// 批 1 刻意**不實作 IStaggerable**：道具對牠完全無效。
    /// 先確認光用身體圍堵好不好玩 —— 好玩的話道具只會更好玩，不好玩的話道具也救不了。
    ///
    /// 移動用 NetworkCharacterController，跟玩家與 WoolNpc 一樣。
    /// 裸 CharacterController + NetworkTransform 撐不過 Fusion 的重模擬，
    /// 用戶端會在幾秒後開始抖動並瞬間大步移動 —— 這個坑已經踩過兩次。
    /// </summary>
    [RequireComponent(typeof(NetworkCharacterController))]
    public class WildBeast : NetworkBehaviour, IInteractable
    {
        /// <summary>場上所有的大動物。**跟 WoolNpc.All 分開**，理由見類別註解。</summary>
        public static readonly List<WildBeast> All = new();

        private enum BeastState : byte { Graze = 0, Alert = 1, Flee = 2, Return = 3 }

        [Header("Wild Beast")]
        [Tooltip("身上的毛是什麼顏色。最貴的顏色配最難抓的動物。")]
        [SerializeField] private DyeColorType _woolColor = DyeColorType.Red;
        [SerializeField] private Transform _interactionAnchor;
        [SerializeField] private Renderer _bodyRenderer;
        [Tooltip("身上的毛。有幾份就顯示幾撮，剃光全關。")]
        [SerializeField] private Renderer[] _fleeceTufts;

        [Networked] public int Fleece { get; set; }
        [Networked] public int StateRaw { get; set; }
        [Networked] public Vector3 HomePoint { get; set; }
        [Networked] public Vector3 TargetPoint { get; set; }

        /// <summary>目前跑的方向。每 0.25 秒重算一次，中間沿用。</summary>
        [Networked] public Vector3 MoveDirection { get; set; }

        /// <summary>至少要逃這麼久。沒有它的話你一退牠就停，圍堵會變成抖動。</summary>
        [Networked] private TickTimer FleeMinTimer { get; set; }

        /// <summary>下一次重算逃跑方向的時間。</summary>
        [Networked] private TickTimer RethinkTimer { get; set; }

        private NetworkCharacterController _ncc;
        private MaterialPropertyBlock _mpb;

        public DyeColorType WoolColor => _woolColor;
        public bool HasWool => Fleece > 0;
        private BeastState State => (BeastState)StateRaw;

        // ---------------- 生命週期 ----------------

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);

            _ncc = GetComponent<NetworkCharacterController>();
            ConfigureController();

            if (HasStateAuthority)
            {
                HomePoint = transform.position;
                Fleece = GameTuning.BeastFleece;
                EnterGraze();
                _needsRelocate = true;   // 第一個 tick 搬到離出生廣場最遠、夠開闊的地方
            }

            ApplyVisual(true);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        private void ConfigureController()
        {
            if (_ncc == null) return;
            _ncc.gravity       = -GameTuning.BeastGravity;
            _ncc.acceleration  = 18f;
            _ncc.braking       = 18f;
            _ncc.maxSpeed      = GameTuning.BeastGrazeSpeed;
            _ncc.rotationSpeed = 6f;   // 自然轉向移動方向，看起來像活的
        }

        /// <summary>由場景建置器指定顏色與家點。</summary>
        public void Configure(DyeColorType color, Vector3 home)
        {
            _woolColor = color;
            HomePoint = home;
        }

        // ---------------- 模擬 ----------------

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            if (_needsRelocate && Runner.IsForward)
            {
                _needsRelocate = false;
                RelocateToCity();
            }

            switch (State)
            {
                case BeastState.Alert:  TickAlert();  break;
                case BeastState.Flee:   TickFlee();   break;
                case BeastState.Return: TickReturn(); break;
                default:                TickGraze();  break;
            }
        }

        // ---------------- 各狀態 ----------------

        private void TickGraze()
        {
            _ncc.maxSpeed = GameTuning.BeastGrazeSpeed;

            // 有人靠得夠近就停下來盯著他
            if (NearestPlayer(out _, out float dist) && dist < GameTuning.BeastStareRadius)
            {
                EnterAlert();
                _ncc.Move(Vector3.zero);
                return;
            }

            var to = TargetPoint - transform.position;
            to.y = 0f;

            if (to.magnitude <= GameTuning.BeastArriveThreshold)
            {
                EnterGraze();          // 到了就換一個新的目標點
                _ncc.Move(Vector3.zero);
                return;
            }

            // 走太久還沒到、或 2 秒內幾乎沒動（卡在建築角）就換個目標，不要貼牆磨
            if (Runner.IsForward && (Runner.SimulationTime > _grazeDeadline || IsStuck()))
            {
                EnterGraze();
                _ncc.Move(Vector3.zero);
                return;
            }

            // 地圖 B：吃草沿路徑走（城市裡建築有碰撞體，直線走會卡牆）。
            // 算不出路就退回原本的直線 —— 規格明講不要讓 NPC 整個停住。
            _ncc.Move(FollowPath(TargetPoint, to));
        }

        /// <summary>
        /// 沿 NavMesh 路徑走向 target 的方向；算不出路或沒有 NavMesh 就是原本的直線。
        /// 路徑只在 forward tick 重算；路徑是過程不是狀態，存在普通欄位。
        /// **只用在吃草與回家** —— 逃跑維持方向取樣，那是這隻動物的靈魂。
        /// </summary>
        private Vector3 FollowPath(Vector3 target, Vector3 straight)
        {
            if (Map.NavUtil.HasNavMesh && Runner.IsForward)
                _path.Recalculate(transform.position, target, Runner.SimulationTime);

            var steer = _path.HasPath ? _path.Steer(transform.position, GameTuning.BeastArriveThreshold) : Vector3.zero;
            return steer != Vector3.zero ? steer : Map.NavUtil.SlideAlongWalls(transform.position, straight);
        }

        // 卡住偵測（只在狀態權威上跑，普通欄位）
        private Vector3 _stuckAnchor;
        private float _stuckSince = -1f;

        /// <summary>一直想走、但 2 秒內離上一次的位置不到 0.3 公尺 —— 卡住了。只在 forward tick 判斷。</summary>
        private bool IsStuck()
        {
            if (!Runner.IsForward) return false;
            float now = Runner.SimulationTime;
            var moved = transform.position - _stuckAnchor; moved.y = 0f;

            if (_stuckSince < 0f || moved.sqrMagnitude > 0.09f)
            {
                _stuckAnchor = transform.position;
                _stuckSince = now;
                return false;
            }
            if (now - _stuckSince < 2f) return false;

            _stuckSince = -1f;
            return true;
        }

        private readonly Npc.NavPathFollower _path = new();
        private float _grazeDeadline = float.PositiveInfinity;

        /// <summary>
        /// 盯著。**這一段是整批最重要的表現** —— 玩家看不到 10 公尺的圈，
        /// 所以牠必須自己把距離講出來：停下來、轉身正對著你、換色。
        ///
        /// 玩家靠這個學會「再一步就會跑」，而那個學習過程本身就是玩法。
        /// 所以不要用 UI 畫警戒圈 —— 畫了就不用學了。
        /// </summary>
        private void TickAlert()
        {
            _ncc.Move(Vector3.zero);

            if (!NearestPlayer(out var player, out float dist))
            {
                EnterGraze();
                return;
            }

            // 轉身面向他。只轉 yaw，不要讓牠仰頭或低頭。
            var look = player.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);

            if (dist < GameTuning.BeastAlertRadius) { EnterFlee(GameTuning.BeastFleeMinSeconds); return; }
            if (dist > GameTuning.BeastStareRadius) EnterGraze();
        }

        private void TickFlee()
        {
            _ncc.maxSpeed = GameTuning.BeastFleeSpeed;

            bool anyoneClose = NearestPlayer(out _, out float dist)
                            && dist < GameTuning.BeastStareRadius;

            // 沒人靠近而且最短逃跑時間到了 -> 收手
            if (!anyoneClose && FleeMinTimer.ExpiredOrNotRunning(Runner))
            {
                if (OutsideTerritory(transform.position)) EnterReturn();
                else EnterGraze();
                return;
            }

            // 方向每 0.25 秒重算一次。每個 tick 重算會讓牠原地抖。
            if (RethinkTimer.ExpiredOrNotRunning(Runner))
            {
                MoveDirection = PickFleeDirection();
                RethinkTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.BeastRethinkSeconds);
            }

            // 方向怎麼選不變（方向取樣）；只是撞到牆會順著牆滑開，不會頂著建築角卡住。
            // 被逼到角落（兩面都是牆）時滑也滑不出去，還是會在角落打轉 —— 那是刻意保留的
            _ncc.Move(Map.NavUtil.SlideAlongWalls(transform.position, MoveDirection));
        }

        /// <summary>走回家，路上不理會玩家 —— 不然牠會在領域邊界來回彈。</summary>
        private void TickReturn()
        {
            _ncc.maxSpeed = GameTuning.BeastGrazeSpeed;

            var to = HomePoint - transform.position;
            to.y = 0f;

            if (to.magnitude <= GameTuning.BeastArriveThreshold)
            {
                EnterGraze();
                _ncc.Move(Vector3.zero);
                return;
            }

            // 回家也沿路徑走；算不出路就直線
            _ncc.Move(FollowPath(HomePoint, to));
        }

        // ---------------- 逃跑方向 ----------------

        /// <summary>
        /// 繞一圈取樣，挑「離最近玩家最遠」的方向。
        ///
        /// **不要用「直接取反方向」。** 那個寫法在領域邊界與牆壁上會卡死：
        /// 玩家從牆的方向逼過來時，反方向就是牆，牠會貼著牆抖動直到被抓到 ——
        /// 那不是被圍住，那是 bug。
        ///
        /// 取樣的淘汰條件有三個，通過的才評分：
        ///   跑出領域、前方有障礙、前方沒有地面
        ///
        /// **全部被淘汰時保持目前方向**，不要改成隨機亂跑 ——
        /// 牠被逼到角落原地打轉正是這整個設計的高潮，那就是玩家靠近的窗口。
        /// 亂跑會讓那個瞬間消失。
        /// </summary>
        private Vector3 PickFleeDirection()
        {
            var origin = transform.position;
            float reach = GameTuning.BeastFleeSpeed * GameTuning.BeastLookaheadSeconds;

            Vector3 best = MoveDirection;
            float bestScore = float.MinValue;
            bool found = false;

            for (int i = 0; i < GameTuning.BeastDirectionSamples; i++)
            {
                float angle = 360f / GameTuning.BeastDirectionSamples * i;
                var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                var predicted = origin + dir * reach;

                if (OutsideTerritory(predicted)) continue;
                if (BlockedAhead(origin, dir)) continue;
                if (!HasGroundAt(predicted)) continue;
                // 地圖 B：預測點要在 NavMesh 上（容差 1 公尺）—— 不往建築裡鑽。
                // 只加這一條，方向取樣本身不變：被逼到角落原地打轉的那個瞬間完全保留。
                // 沒有 NavMesh 的場景 IsOnNavMesh 一律回 true，等於沒加。
                if (!Map.NavUtil.IsOnNavMesh(predicted, 1f)) continue;

                float score = ScoreAgainstPlayers(predicted);
                if (score <= bestScore) continue;

                bestScore = score;
                best = dir;
                found = true;
            }

            // 一個方向都沒過 -> 被封死了，保持原方向（原地打轉）
            return found ? best : MoveDirection;
        }

        /// <summary>
        /// 這個預測點有多安全 = 離最近玩家多遠，再加上一點「離第二近的玩家也要遠」。
        ///
        /// 第二項很重要：少了它，牠會為了躲開 A 而直直撞進 B 的懷裡，
        /// 兩個人隨便站都夾得到。加了之後必須真的站對位置。
        /// </summary>
        private float ScoreAgainstPlayers(Vector3 predicted)
        {
            float nearest = float.MaxValue;
            float second = float.MaxValue;

            for (int i = 0; i < PlayerController.All.Count; i++)
            {
                var p = PlayerController.All[i];
                if (p == null || p.Object == null || !p.Object.IsValid) continue;

                var to = p.transform.position - predicted;
                to.y = 0f;
                float d = to.magnitude;

                if (d < nearest) { second = nearest; nearest = d; }
                else if (d < second) { second = d; }
            }

            if (nearest == float.MaxValue) return 0f;          // 場上沒有玩家
            if (second == float.MaxValue) return nearest;      // 只有一個玩家

            return nearest + GameTuning.BeastSecondPlayerWeight * second;
        }

        private bool _needsRelocate;

        [Tooltip("勾起來：大動物生在玩家出生廣場附近（測試方便，14～30 公尺，在警戒範圍外）。\n" +
                 "取消：照規格生在離出生廣場最遠、周圍夠開闊的地方。")]
        [SerializeField] private bool _spawnNearPlayers = true;

        /// <summary>
        /// 搬到離玩家出生廣場最遠、而且周圍夠開闊的合法點，家點跟著搬。
        /// 開闊 = 以 5 公尺間距往 8 個方向取樣，至少 5 個在 NavMesh 上；
        /// 不然領域太窄，方向取樣會一直全部淘汰，牠會原地不動。
        /// **一定走 NCC.Teleport**。沒有城市（Stall_Test）就維持原位。
        /// </summary>
        private void RelocateToCity()
        {
            var map = FindAnyObjectByType<Map.RandomMapBuilder>();
            if (map == null || !Map.NavUtil.HasNavMesh || map.PlazaCenters.Count == 0) return;

            var spawn = map.PlazaCenters[0];
            bool found = _spawnNearPlayers
                // 測試用：放在出生廣場附近，但在警戒範圍（BeastStareRadius）外面，
                // 不然一出生就看到玩家、直接進入盯人／逃跑
                ? map.TryGetOpenRoadPointNear(spawn, GameTuning.BeastStareRadius + 2f, 30f, Random.Range, out var p)
                : map.TryGetFarOpenPoint(spawn, Random.Range, out p);
            if (!found) return;

            _ncc.Teleport(p + Vector3.up * 0.1f);
            HomePoint = p;
            EnterGraze();
        }

        private bool OutsideTerritory(Vector3 point)
        {
            var to = point - HomePoint;
            to.y = 0f;
            return to.magnitude > GameTuning.BeastTerritoryRadius;
        }

        private bool BlockedAhead(Vector3 origin, Vector3 dir)
        {
            var from = origin + Vector3.up * 0.8f;
            return Physics.Raycast(from, dir, GameTuning.BeastObstacleProbe,
                                   ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 預測點底下有沒有地面。
        ///
        /// 不在原始規格裡，是補的：現在的地圖只有 60x60 的地板，而領域半徑是 40 ——
        /// 領域比地板大，光靠領域檢查擋不住牠跑出邊緣摔下去。
        /// 有正式地形（四周有牆）之後這條就只是多一層保險。
        /// </summary>
        private static bool HasGroundAt(Vector3 point)
        {
            return Physics.Raycast(point + Vector3.up * 2f, Vector3.down,
                                   GameTuning.BeastGroundProbe + 2f,
                                   ~0, QueryTriggerInteraction.Ignore);
        }

        // ---------------- 狀態切換 ----------------

        private void EnterGraze()
        {
            StateRaw = (int)BeastState.Graze;

            // Random 只在狀態權威上跑，結果透過 [Networked] TargetPoint 同步出去，
            // 所以重模擬不會產生不同的目標點
            // 地圖 B：目標點先落到 NavMesh 上，多試幾次（城市建築密，隨機點常常在建築裡）。
            // 沒有 NavMesh 的場景 SnapToNavMesh 原樣回傳，行為跟原本一樣。
            // 有城市就挑領域內一格道路（道路彼此連通，路徑算得出來）；否則用原本的隨機點
            float grazeRadius = GameTuning.BeastTerritoryRadius * 0.5f;
            var map = Map.NavUtil.HasNavMesh ? FindAnyObjectByType<Map.RandomMapBuilder>() : null;
            if (map != null && map.TryGetRoadPoint(Random.Range, out var road, HomePoint, grazeRadius))
            {
                TargetPoint = road;
            }
            else
            {
                var offset = Random.insideUnitCircle * grazeRadius;
                var wish = HomePoint + new Vector3(offset.x, 0f, offset.y);
                TargetPoint = Map.NavUtil.SnapToNavMesh(wish, 4f, out var p) ? p : wish;
            }
            _path.Clear();
            _stuckSince = -1f;

            // 走路的時間上限：直線距離 ÷ 速度 × 3，至少 10 秒
            if (Runner != null)
            {
                var d = TargetPoint - transform.position; d.y = 0f;
                _grazeDeadline = Runner.SimulationTime
                               + Mathf.Max(10f, d.magnitude / Mathf.Max(0.1f, GameTuning.BeastGrazeSpeed) * 3f);
            }
        }

        private void EnterAlert() => StateRaw = (int)BeastState.Alert;

        private void EnterFlee(float minSeconds)
        {
            StateRaw = (int)BeastState.Flee;
            FleeMinTimer = TickTimer.CreateFromSeconds(Runner, minSeconds);
            RethinkTimer = default;                 // 立刻算一次方向，不要等 0.25 秒
            MoveDirection = PickFleeDirection();
        }

        private void EnterReturn() => StateRaw = (int)BeastState.Return;

        // ---------------- 找玩家 ----------------

        /// <summary>
        /// 最近的玩家。**是「最近的」不是「主機的」** ——
        /// 用戶端玩家靠近也必須觸發，不然連線時只有房主能嚇到牠。
        /// </summary>
        private bool NearestPlayer(out PlayerController nearest, out float distance)
        {
            nearest = null;
            distance = float.MaxValue;

            for (int i = 0; i < PlayerController.All.Count; i++)
            {
                var p = PlayerController.All[i];
                if (p == null || p.Object == null || !p.Object.IsValid) continue;

                var to = p.transform.position - transform.position;
                to.y = 0f;
                float d = to.magnitude;

                if (d >= distance) continue;
                distance = d;
                nearest = p;
            }
            return nearest != null;
        }

        // ---------------- IInteractable（剃毛）----------------

        public Transform InteractionAnchor =>
            _interactionAnchor != null ? _interactionAnchor : transform;

        /// <summary>比地面雜物高、比機台低，跟 WoolNpc 一樣。</summary>
        public int InteractionPriority => 1;

        public bool CanInteract(in InteractionContext ctx)
            => Fleece > 0 && ctx.HeldKind == ItemKind.Shears;

        public string GetPrompt(in InteractionContext ctx)
        {
            if (ctx.HeldKind != ItemKind.Shears) return null;
            return Fleece > 0
                ? $"[左鍵] 剃毛（{Fleece} 份一次全掉）"
                : "牠身上的毛剃光了";
        }

        /// <summary>
        /// 剃毛。批 1 沒有暈眩，所以收網方式就是「靠到 2.5 公尺內」——
        /// 那本身就是兩個人夾到底的證明。
        ///
        /// 毛**生成在地上**、不是直接進背包：九份散一地要一份一份撿，
        /// 撿的過程就是這場圍堵的戰利品展示。跟撞暈掉毛是同一個規則。
        /// </summary>
        public void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority || Fleece <= 0) return;

            int count = Fleece;
            Fleece = 0;

            var spec = GarmentSpec.Create(PatternType.None, _woolColor);
            for (int i = 0; i < count; i++)
            {
                // 繞著牠散開 1.5–2.5 公尺
                float angle = 360f / count * i + Random.Range(-12f, 12f);
                float radius = Random.Range(1.5f, 2.5f);
                var offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;

                ItemFactory.Spawn(Runner, ItemKind.Wool, spec,
                                  transform.position + offset + Vector3.up * 0.8f);
            }

            ctx.Player.TriggerShearVisual();
            GameAudio.PlayAt(SfxId.Shear, transform.position);

            // 剃完要真的跑掉，不是原地繼續被圍
            EnterFlee(GameTuning.BeastShearedFleeSeconds);
        }

        // ---------------- 外觀 ----------------

        public override void Render() => ApplyVisual(false);

        private int _renderedFleece = -1;
        private int _renderedState = -1;

        private void ApplyVisual(bool force)
        {
            if (!force && _renderedFleece == Fleece && _renderedState == StateRaw) return;
            _renderedFleece = Fleece;
            _renderedState = StateRaw;

            _mpb ??= new MaterialPropertyBlock();

            if (_bodyRenderer != null)
            {
                // Alert 換成明顯的警戒色 —— 這是玩家唯一能看到的「距離提示」
                var c = State == BeastState.Alert
                    ? new Color(1f, 0.55f, 0.15f)
                    : PlaceholderPalette.Dye(_woolColor);

                if (Fleece <= 0) c *= 0.45f;   // 剃光轉暗，遠遠就看得出沒毛了

                _bodyRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", c);
                _mpb.SetColor("_Color", c);
                _bodyRenderer.SetPropertyBlock(_mpb);
            }

            if (_fleeceTufts == null) return;
            for (int i = 0; i < _fleeceTufts.Length; i++)
            {
                if (_fleeceTufts[i] == null) continue;

                // 九份毛配幾撮視覺：等比例換算，不要一份一撮（會太多）
                bool on = _fleeceTufts.Length > 0
                       && i < Mathf.CeilToInt(_fleeceTufts.Length * (Fleece / (float)GameTuning.BeastFleece));
                if (_fleeceTufts[i].enabled != on) _fleeceTufts[i].enabled = on;
            }
        }
    }
}
