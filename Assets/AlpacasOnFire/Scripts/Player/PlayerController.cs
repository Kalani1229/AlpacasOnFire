using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.Networking;
using AlpacasOnFire.Prank;
using AlpacasOnFire.Stall;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Player
{
    /// <summary>
    /// 羊駝玩家。共用朝向模型：
    ///  - 滑鼠水平位移 -> Yaw -> 同時是角色朝向與鏡頭 yaw
    ///  - 滑鼠垂直位移 -> Pitch -> 只影響鏡頭與互動射線，角色不會傾斜
    ///  - WASD 永遠相對角色目前朝向
    /// 互動／工具的判定方向就是 AimDirection，不另外維護一組瞄準方向。
    /// </summary>
    [RequireComponent(typeof(NetworkCharacterController))]
    [RequireComponent(typeof(PlayerCarry))]
    public class PlayerController : NetworkBehaviour, IInteractable, IGarmentHost, IStaggerable
    {
        public static PlayerController Local { get; private set; }

        /// <summary>
        /// 場上所有的羊駝。跟 WoolNpc.All、MaterialCrate.All 同一個慣例。
        ///
        /// 目前的用途：顧客系統要判斷「白毛有沒有來源」。白毛只能從剃隊友來，
        /// 而不能剃自己，所以場上少於兩隻羊駝時白色訂單是無解的。
        /// </summary>
        public static readonly List<PlayerController> All = new();

        [Header("Anchors")]
        [SerializeField] private Transform _handAnchor;
        [SerializeField] private Transform _headAnchor;
        [SerializeField] private Transform _catchAnchor;
        [SerializeField] private Transform _garmentAnchor;

        [Header("Visual")]
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private Renderer _garmentRenderer;
        [SerializeField] private Renderer _fleeceIndicator;
        [Tooltip("剃毛器。平常收在毛裡看不見，剃毛的瞬間才伸出來。由建置工具自動接上。")]
        [SerializeField] private GameObject _shearsVisual;

        [Header("Fade")]
        [Tooltip("擋住畫布時改用的半透明材質。由建置工具自動接上。")]
        [SerializeField] private Material _fadeMaterial;

        [Header("Camera Rig")]
        [SerializeField] private GameObject _cameraRigPrefab;

        // ---- 網路狀態 ----
        [Networked] public float Yaw { get; set; }
        [Networked] public float Pitch { get; set; }
        [Networked] public int ColorIndex { get; set; }
        [Networked] public int Fleece { get; set; }
        [Networked] public NetworkBool HasGarmentNet { get; set; }
        [Networked] public GarmentSpec WornGarment { get; set; }
        [Networked] private TickTimer FleeceTimer { get; set; }

        /// <summary>剃毛器伸出來的殘餘時間。走 [Networked] 才能讓所有人都看到這個動作。</summary>
        [Networked] private TickTimer ShearVisualTimer { get; set; }

        /// <summary>吐口水的冷卻。</summary>
        [Networked] private TickTimer SpitTimer { get; set; }

        [Networked] private NetworkButtons PreviousButtons { get; set; }

        private NetworkCharacterController _ncc;
        private PlayerCarry _carry;
        private Renderer[] _fadeRenderers;
        private Material[] _fadeOriginals;
        private bool _bodyFaded;
        private PlayerInteractor _interactor;
        private PlayerStallAgent _stallAgent;
        private StaggerStatus _stagger;
        private MaterialPropertyBlock _mpb;
        private PlayerCameraRig _rig;

        public Transform HandAnchor   => _handAnchor != null ? _handAnchor : transform;
        public Transform HeadAnchor   => _headAnchor != null ? _headAnchor : transform;
        public Transform CatchAnchor  => _catchAnchor != null ? _catchAnchor : transform;
        public PlayerCarry Carry      => _carry;
        public PlayerInteractor Interactor => _interactor;

        /// <summary>互動與工具的判定方向 = 角色朝向 = 鏡頭朝向。</summary>
        public Vector3 AimDirection => Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward;

        // ---------------- 生命週期 ----------------

        public override void Spawned()
        {
            _ncc = GetComponent<NetworkCharacterController>();
            ConfigureController();
            _carry = GetComponent<PlayerCarry>();
            _stallAgent = GetComponent<PlayerStallAgent>();   // 舊 prefab 上可能沒有，允許 null
            _stagger = GetComponent<StaggerStatus>();         // 同上
            CacheFadeRenderers();
            _interactor = new PlayerInteractor(this);
            if (!All.Contains(this)) All.Add(this);
            gameObject.name = $"Alpaca_P{ColorIndex + 1}";

            if (HasStateAuthority)
            {
                Fleece = GameTuning.FleeceMax;
                FleeceTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.FleeceRegenSeconds);
            }

            ApplyBodyColor();

            if (HasInputAuthority)
            {
                Local = this;
                SetupLocalRig();
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            if (Local == this) Local = null;
            if (_rig != null) Destroy(_rig.gameObject);
        }

        private void SetupLocalRig()
        {
            if (LocalInputProvider.Instance == null)
            {
                var inputGo = new GameObject("[LocalInput]");
                inputGo.AddComponent<LocalInputProvider>();
            }
            LocalInputProvider.Instance.SetCursorLocked(true);

            _rig = PlayerCameraRig.Instance;
            if (_rig == null)
            {
                GameObject go = _cameraRigPrefab != null
                    ? Instantiate(_cameraRigPrefab)
                    : new GameObject("[PlayerCameraRig]");
                _rig = go.GetComponent<PlayerCameraRig>();
                if (_rig == null) _rig = go.AddComponent<PlayerCameraRig>();
            }
            _rig.SetTarget(transform);

            // 場景裡若有預設的 MainCamera，關掉避免兩台相機打架
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (_rig.Camera != null && cam != _rig.Camera && cam.CompareTag("MainCamera"))
                    cam.gameObject.SetActive(false);
            }
        }

        // ---------------- 模擬 ----------------

        public override void FixedUpdateNetwork()
        {
            if (GetInput(out NetInput input))
            {
                Yaw = input.Yaw;
                Pitch = Mathf.Clamp(input.Pitch, GameTuning.CameraPitchMin, GameTuning.CameraPitchMax);
                transform.rotation = Quaternion.Euler(0f, Yaw, 0f);

                Move(input.Move);

                var pressed = input.Buttons.GetPressed(PreviousButtons);
                PreviousButtons = input.Buttons;

                // 失控中不能做任何事：不能互動、不能丟、不能用工具。
                // 移動已經在 Move() 裡被吃掉了，這裡擋的是動作。
                if (HasStateAuthority && !IsStaggered)
                {
                    var ctx = _interactor.BuildContext();

                    // ---- 蓄力鍵：Q，拿著卡車時左鍵也算 ----
                    //
                    // 同一個鍵兩個動作，靠「按多久」分開：點按放下、長按丟出。
                    // 放開的那一刻才決定是哪一個（見 ReleaseThrowCharge）。
                    //
                    // **邊緣是從「合成的按住狀態」推導的，不是用 GetPressed/GetReleased。**
                    // 兩個鍵都能蓄力的話，用 Q 開始、用左鍵放開（或反過來）都要成立；
                    // 逐鍵判邊緣的話那種混按會漏掉放開、卡車就永遠舉在手上。
                    bool chargeOnPrimary = _carry.Held != null && _carry.Held.ChargesOnPrimary;

                    // 開始一定要是**新按下**的那一刻。用「按著」當條件的話，
                    // 按著左鍵撿起卡車會在下一個 tick 直接開始蓄力 ——
                    // 玩家只是想撿起來，手指還沒放開就已經在舉了。
                    bool chargeStart = pressed.IsSet(GameButton.ThrowCatch)
                                    || (chargeOnPrimary && pressed.IsSet(GameButton.Interact));

                    bool chargeHeld = input.Buttons.IsSet(GameButton.ThrowCatch)
                                   || (chargeOnPrimary && input.Buttons.IsSet(GameButton.Interact));

                    if (!_carry.ThrowCharging && chargeStart) _carry.BeginThrowCharge();

                    if (_carry.ThrowCharging)
                    {
                        if (chargeHeld) _carry.TickThrowCharge(Runner.DeltaTime);
                        else _carry.ReleaseThrowCharge(in ctx);
                    }

                    // ---- 左鍵／Space：對前面的目標做事 ----
                    //
                    // 手上的東西把左鍵拿去蓄力的時候（卡車），這裡要整個讓開 ——
                    // 不然按一下會同時開始蓄力又把腳邊的東西撿起來。
                    if (!chargeOnPrimary && pressed.IsSet(GameButton.Interact))
                        HandlePrimaryPress(in ctx);

                    // ---- E：吐口水 ----
                    // 羊駝自帶的能力，不佔手、跟手上拿什麼無關。
                    // 借用 GameButton.DefaultTool 這個列舉值（原本是「拿出隨身剃毛器」，
                    // 剃毛改成內建之後就空著了）—— NetInput 是連線架構的一部分，
                    // 不動它的列舉，只換綁定的意義。
                    if (pressed.IsSet(GameButton.DefaultTool))
                        TrySpit(in ctx);

                    // ---- 右鍵：只做設定類動作 ----
                    //
                    // 手提箱選材料、放置模式取消。營業中基本上用不到。
                    // **道具使用已經搬回左鍵**，右鍵不再有任何即時動作。
                    //
                    // 放置預覽中右鍵整個歸「取消放置」（PlayerStallAgent 自己在本機讀），
                    // 次要互動要讓開，不然會同時取消又打開選色面板。
                    bool useToolHeld = input.Buttons.IsSet(GameButton.UseTool);

                    if (pressed.IsSet(GameButton.UseTool) && !IsPlacingDevice)
                        _interactor.TrySecondaryInteract();

                    // 噴槍的按住塗抹仍然在右鍵（它是「持續使用」不是「出手」）
                    _carry.TickTool(in ctx, useToolHeld, Runner.DeltaTime);
                }
            }
            else if (HasStateAuthority)
            {
                // 沒有輸入來源（例如玩家斷線）也要保持在地面上。
                // 純代理端（proxy）不要自己跑移動，交給 NetworkCharacterController 的狀態同步。
                Move(Vector2.zero);
            }

            if (HasStateAuthority)
            {
                TickFleece();
                CheckFellOutOfWorld();
            }
        }

        /// <summary>放置預覽中（手上舉著一台機台等著放下）。</summary>
        private bool IsPlacingDevice =>
            _stallAgent != null && _stallAgent.Object != null && _stallAgent.Object.IsValid
            && _stallAgent.HasPending;

        /// <summary>
        /// 左鍵／Space：**對前面的目標做事。** 依序判斷，第一個成立的就執行。
        ///
        ///   1. 有東西朝你飛來            -> 接住（最優先）
        ///   2. 手上拿著道具              -> 使用（大蔥揮擊）
        ///   3. 互動範圍內找得到有效目標  -> 互動（撿起／放入／取出／敲鈴／交貨／剃毛）
        ///   4. 其他                      -> 不做事
        ///
        /// **左鍵完全不丟東西。** 丟出與放下都歸 Q：
        /// 之前左鍵在「面前空無一物」時會改成丟出，結果是同一個鍵在看不見的狀態
        /// 之間切換意義 —— 想撿東西卻把手上的毛扔掉、拿著大蔥想打人卻先把大蔥丟出去。
        /// 現在左鍵只有一個意思：對著前面那個東西動手。
        ///
        /// 第 2 順位要壓在互動前面：拿著大蔥面對地上的羊毛時，左鍵應該是揮大蔥，
        /// 不是「放下大蔥去撿羊毛」。手上拿著武器就是要打人。
        ///
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        private void HandlePrimaryPress(in InteractionContext ctx)
        {
            // 1. 接住優先於一切：有東西滯空在接得到的範圍內時，一律先算接住
            if (_carry.TryManualCatch()) return;

            // 2. 手上的道具優先於情境互動
            if (_carry.TryPrank(in ctx)) return;

            // 3. 正常的情境互動
            if (_interactor.TryInteract()) return;

            // 4. 面前有東西但現在不能用 -> 什麼都不做，給一聲拒絕音，
            //    讓玩家知道按鍵有進去、是狀態不對（機台滿了、成品沒人拿、還沒開張）。
            //
            //    **面前空無一物就完全不出聲。** 對著空氣按左鍵是最常見的誤按，
            //    每次都叫會很吵，而且那時候本來就沒有任何「做不到」的事情要回報。
            if (_interactor.HasAnyTargetInRange())
                GameAudio.PlayAt(SfxId.PlaceRejected, transform.position);
        }

        /// <summary>
        /// 掉出世界的保險。正常情況不會觸發；如果一直觸發，代表關卡沒有地板
        /// （多半是「建置全部場景」時 GameCatalog 不完整，[Level] 是空的）。
        /// </summary>
        private void CheckFellOutOfWorld()
        {
            if (transform.position.y > -30f) return;

            var (pos, rot) = Networking.SpawnPointRegistry.Next();
            // 一定要用 NCC 的 Teleport，直接寫 transform 會讓 CharacterController 的
            // 內部快取位置對不上，之後就會開始抖動與瞬移。
            _ncc?.Teleport(pos, rot);
            if (_ncc != null) _ncc.Velocity = Vector3.zero;

            if (!_warnedNoGround)
            {
                _warnedNoGround = true;
                Debug.LogError("[羊駝很忙] 角色掉出世界並被拉回出生點。這個關卡場景裡沒有地板 —— " +
                               "請執行選單「羊駝很忙 / 4. 檢查設置」，再重跑「1. 建置佔位資產」與「3. 建置全部場景」。");
            }
        }

        private bool _warnedNoGround;

        /// <summary>
        /// 把 GameTuning 的數值餵給 Fusion 的 NetworkCharacterController。
        ///
        /// rotationSpeed 一定要是 0：NCC 預設會把角色轉向「移動方向」，那會直接破壞
        /// 共用朝向模型（朝向必須完全由滑鼠的 Yaw 決定）。設 0 之後它內部的 Slerp
        /// 係數為 0，等於不動旋轉，我們自己在 FixedUpdateNetwork 裡設 transform.rotation。
        /// </summary>
        private void ConfigureController()
        {
            if (_ncc == null) return;
            _ncc.gravity       = -GameTuning.Gravity;
            _ncc.acceleration  = GameTuning.MoveAcceleration;
            _ncc.braking       = GameTuning.MoveBraking;
            _ncc.maxSpeed      = GameTuning.MoveSpeed;
            _ncc.rotationSpeed = 0f;
        }

        /// <summary>
        /// 失控中：控制權被拿走，但**視覺完全保留**。
        /// 黑畫面會讓被害者連笑話都看不到，那就不好笑了，只是煩。
        /// </summary>
        public bool IsStaggered => _stagger != null && _stagger.Staggered;

        private void Move(Vector2 moveInput)
        {
            if (_ncc == null) return;

            // 手上有東西時走慢一點。maxSpeed 不是網路狀態，但它是依 HasItem（是網路狀態）
            // 推導出來的，所以重模擬時兩端算出來的值一致。
            _ncc.maxSpeed = GameTuning.MoveSpeed *
                            (_carry != null && _carry.HasItem ? GameTuning.CarrySlowFactor : 1f);

            // 失控中把輸入整個丟掉。注意**不是直接 return** ——
            // 還是要呼叫 Move()，重力與擊退才會繼續作用，不然人會定在半空中。
            if (IsStaggered) moveInput = Vector2.zero;

            // 移動方向永遠相對角色目前朝向
            var wish = transform.rotation * new Vector3(moveInput.x, 0f, moveInput.y);
            wish = ClampToStallZone(wish);

            // 擊退疊在移動之上。用 maxSpeed 放大而不是直接寫 Velocity ——
            // 寫 Velocity 會跟 NCC 內部用位移反推速度的做法打架（見 SuppressStepUpLaunch）。
            var knock = _stagger != null ? _stagger.CurrentKnockback : Vector3.zero;
            if (knock.sqrMagnitude > 0.0001f)
            {
                _ncc.maxSpeed = Mathf.Max(_ncc.maxSpeed, knock.magnitude);
                wish = (wish + knock.normalized * 1.2f).normalized;
            }

            _ncc.Move(wish);

            SuppressStepUpLaunch();
        }

        /// <summary>
        /// 擺攤期間把玩家關在「擺攤區域」（襯布 + 邊界）裡面。
        ///
        /// 做法是**把往外的移動分量砍掉**，而不是事後把位置拉回來 ——
        /// 拉位置會跟 NetworkCharacterController 的內部快取打架、造成抖動，
        /// 砍分量則會自然變成沿著邊界滑動，手感也比較好。
        ///
        /// 判定在襯布的本地座標做，所以襯布轉過角度也成立。
        /// </summary>
        private Vector3 ClampToStallZone(Vector3 wish)
        {
            var stall = StallManager.Instance;
            if (stall == null || stall.Object == null || !stall.MatDeployed) return wish;

            var rot = Quaternion.Euler(0f, stall.MatYaw, 0f);
            var invRot = Quaternion.Inverse(rot);

            var local = invRot * (transform.position - stall.MatCenter);
            var localWish = invRot * wish;
            float half = stall.MatSize * 0.5f + GameTuning.StallZoneMargin;

            if (local.x >  half && localWish.x > 0f) localWish.x = 0f;
            if (local.x < -half && localWish.x < 0f) localWish.x = 0f;
            if (local.z >  half && localWish.z > 0f) localWish.z = 0f;
            if (local.z < -half && localWish.z < 0f) localWish.z = 0f;

            return rot * localWish;
        }

        /// <summary>
        /// 修正「踩到矮台階會飛起來」。
        ///
        /// 原因在 Fusion 的 NetworkCharacterController.Move() 最後兩行：
        ///
        ///     _controller.Move(moveVelocity * deltaTime);
        ///     Data.Velocity = (transform.position - previousPos) * Runner.TickRate;
        ///
        /// 它是用「實際位移」反推速度。CharacterController 踩上矮台階時，Unity 會在
        /// **一個 tick 之內**把膠囊往上瞬移一整個 stepOffset（本專案 0.35 公尺），
        /// 於是速度被算成 0.35 x 60 = 21 m/s 向上。
        ///
        /// 而 NCC 下一個 tick 的接地保護只歸零「負的」y：
        ///
        ///     if (Data.Grounded &amp;&amp; moveVelocity.y &lt; 0) moveVelocity.y = 0f;
        ///
        /// 那個正的 y 完整活下來，重力要好幾秒才拉得回來 —— 角色就飛出去了。
        /// 走上斜坡也是同一個機制。
        ///
        /// 修法：每次 Move 之後把向上的殘留速度歸零。本專案沒有跳躍
        /// （NCC 的 Jump() 從來沒有被呼叫過），所以「向上的速度」一律是這個 bug 的產物，
        /// 直接砍掉不會有副作用；往下的速度（重力、墜落）完全不動。
        ///
        /// 為什麼不直接改 Fusion 的檔案：那是 SDK 原始碼，下次更新 Fusion 就會被覆蓋掉。
        ///
        /// **如果之後要加跳躍**，這裡要改成只在 `_ncc.Grounded` 為 true 時歸零，
        /// 否則跳到一半會被砍掉。
        /// </summary>
        private void SuppressStepUpLaunch()
        {
            var v = _ncc.Velocity;
            if (v.y <= 0f) return;

            v.y = 0f;
            _ncc.Velocity = v;
        }

        private void TickFleece()
        {
            if (Fleece >= GameTuning.FleeceMax) return;
            if (!FleeceTimer.ExpiredOrNotRunning(Runner)) return;

            Fleece = Mathf.Min(GameTuning.FleeceMax, Fleece + 1);
            FleeceTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.FleeceRegenSeconds);
        }

        /// <summary>冷卻好了沒。HUD 要用。</summary>
        public bool SpitReady => SpitTimer.ExpiredOrNotRunning(Runner);

        /// <summary>冷卻的剩餘比例 0~1（1 = 剛吐完）。</summary>
        public float SpitCooldown01
        {
            get
            {
                float left = SpitTimer.RemainingTime(Runner) ?? 0f;
                return Mathf.Clamp01(left / Mathf.Max(0.01f, GameTuning.SpitCooldownSeconds));
            }
        }

        /// <summary>
        /// 吐口水。**羊駝自帶的能力，不是道具** ——
        /// 不用撿、不用拿、不佔手，手上抱著羊毛也吐得出來。
        /// 這也是它跟大蔥、卡車最大的差別：那兩個要騰出手，這個不用。
        ///
        /// 綁 E 而不是右鍵：右鍵已經給了手持惡搞道具與次要互動，
        /// 而自帶能力本來就該有自己的鍵位，不該跟「手上拿什麼」有關。
        ///
        /// **噴出去的是一顆看得見的投射物**，不是立即命中。所以它需要瞄準：
        /// 飛行要時間、會往下掉、會落空。這才配得上它零成本、無限量的定位。
        ///
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        public void TrySpit(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;
            if (IsStaggered) return;
            if (!SpitTimer.ExpiredOrNotRunning(Runner)) return;

            // 冷卻先算 —— 噴空氣也要算，不然玩家會用亂噴來確認前面有沒有人
            SpitTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.SpitCooldownSeconds);
            GameAudio.PlayAt(SfxId.Spray, transform.position);

            var prefab = GameCatalog.Instance != null ? GameCatalog.Instance.GetItem(ItemKind.Spit) : null;
            if (prefab == null)
            {
                Debug.LogError("[惡搞] GameCatalog 裡沒有口水投射物的 prefab。" +
                               "請執行選單「羊駝很忙 / 1. 建置佔位資產」。");
                return;
            }

            // 從嘴巴的高度噴出去，不是從腳底
            var origin = HeadAnchor.position + AimDirection * 0.5f;
            var dir = AimDirection;

            Runner.Spawn(prefab, origin, Quaternion.LookRotation(dir, Vector3.up), null,
                         (r, o) => o.GetComponent<SpitProjectile>()?.Launch(this, dir));
        }

        /// <summary>
        /// 伸出剃毛器。**這是剃的那一方呼叫的**（不是被剃的那一方），
        /// 所以要傳 ctx.Player 而不是 this。
        ///
        /// 剃毛器不再是一件要撿、要拿、要收的道具 —— 它平常收在毛裡看不見，
        /// 按下左鍵的瞬間伸出來、一下子又縮回去。玩家永遠不必管它在哪。
        ///
        /// 只在 StateAuthority 呼叫；TickTimer 是 [Networked]，所有端都會看到。
        /// </summary>
        public void TriggerShearVisual()
        {
            if (!HasStateAuthority) return;
            ShearVisualTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.ShearVisualSeconds);
        }

        /// <summary>
        /// 被隊友剃毛。只在 StateAuthority 呼叫。
        ///
        /// **白毛（羊駝毛）不走共同背包，也不走素材箱。** 它是現場產出的：
        /// 剃下來就掉在腳邊，撿起來直接送去織布機。
        ///
        /// 為什麼不進背包／不給它一個素材箱：素材箱是「開張前備料、開張時鎖定」的東西，
        /// 而互相剃毛是**營業中持續在做的事**。把白毛塞進那套流程，營業中剃到的毛
        /// 會卡在背包裡進不了箱子，整條產線等於不存在。留在場上當物品反而最直接 ——
        /// 剃、撿、織，三步都在同一個地方發生。
        ///
        /// 也因為這樣，白毛不佔手提箱那三個顏色格。三格全部留給野外採集的顏色。
        ///
        /// Classic（Stall_Test）走的是完全相同的一條路，行為跟以前一模一樣。
        /// </summary>
        public bool Shear(PlayerController by)
        {
            if (!HasStateAuthority || Fleece <= 0) return false;

            Fleece--;
            FleeceTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.FleeceRegenSeconds);

            for (int i = 0; i < GameTuning.WoolPerShear; i++)
            {
                var offset = Random.insideUnitCircle * 0.4f;
                var pos = transform.position + Vector3.up * 0.6f + new Vector3(offset.x, 0f, offset.y);
                ItemFactory.Spawn(Runner, ItemKind.Wool, default, pos);
            }

            GameAudio.PlayAt(SfxId.Shear, transform.position);
            return true;
        }

        // ---------------- 外觀 ----------------

        public override void Render()
        {
            ApplyBodyColor();

            if (_garmentRenderer != null)
            {
                _garmentRenderer.enabled = HasGarmentNet;
                if (HasGarmentNet) Tint(_garmentRenderer, PlaceholderPalette.Dye(WornGarment.Color));
            }

            if (_fleeceIndicator != null)
            {
                float t = GameTuning.FleeceMax <= 0 ? 0f : (float)Fleece / GameTuning.FleeceMax;
                _fleeceIndicator.enabled = Fleece > 0;
                Tint(_fleeceIndicator, Color.Lerp(new Color(0.6f, 0.55f, 0.5f), PlaceholderPalette.Wool, t));
            }

            // 剃毛器：只有剃的那一瞬間看得見
            if (_shearsVisual != null)
            {
                bool show = Object != null && Object.IsValid
                            && !ShearVisualTimer.ExpiredOrNotRunning(Runner);
                if (_shearsVisual.activeSelf != show) _shearsVisual.SetActive(show);
            }

            RenderStagger();
        }

        /// <summary>
        /// 倒地的表現：整隻羊駝翻倒。
        ///
        /// 只轉 **視覺**、不動 transform 的 rotation —— 角色的朝向是共用朝向模型的一部分
        /// （Yaw 直接決定鏡頭），轉了會讓被害者的畫面天旋地轉，那就從好笑變成想吐。
        /// 所以倒的是身體，鏡頭照常。
        /// </summary>
        private void RenderStagger()
        {
            if (_bodyRenderer == null) return;

            var body = _bodyRenderer.transform;
            bool down = IsStaggered;

            // 倒下快、爬起來慢一點 —— 笑點在爬起來的過程
            float target = down ? 82f : 0f;
            float speed = down ? 900f : 260f;
            _bodyTilt = Mathf.MoveTowards(_bodyTilt, target, speed * Time.deltaTime);

            if (Mathf.Abs(_bodyTilt) < 0.01f && !down)
            {
                if (_bodyTiltApplied) { body.localRotation = _bodyBaseRotation; _bodyTiltApplied = false; }
                return;
            }

            if (!_bodyTiltApplied)
            {
                _bodyBaseRotation = body.localRotation;
                _bodyTiltApplied = true;
            }
            body.localRotation = _bodyBaseRotation * Quaternion.Euler(_bodyTilt, 0f, 0f);
        }

        private float _bodyTilt;
        private Quaternion _bodyBaseRotation = Quaternion.identity;
        private bool _bodyTiltApplied;

        private void ApplyBodyColor()
        {
            if (_bodyRenderer == null) return;
            Tint(_bodyRenderer, PlaceholderPalette.PlayerColor(ColorIndex));
        }

        /// <summary>
        /// 第三人稱下自己的身體會擋住準心瞄的東西（尤其是塗衣服的時候）。
        /// 這裡把整隻羊駝換成半透明材質，讓玩家看得到畫布。只對本機玩家做。
        /// </summary>
        public void SetBodyFaded(bool faded)
        {
            if (_bodyFaded == faded) return;
            if (_fadeMaterial == null || _fadeRenderers == null) return;

            _bodyFaded = faded;
            for (int i = 0; i < _fadeRenderers.Length; i++)
            {
                if (_fadeRenderers[i] == null) continue;
                _fadeRenderers[i].sharedMaterial = faded ? _fadeMaterial : _fadeOriginals[i];
            }
        }

        public bool IsBodyFaded => _bodyFaded;

        private void CacheFadeRenderers()
        {
            _fadeRenderers = GetComponentsInChildren<Renderer>(true);
            _fadeOriginals = new Material[_fadeRenderers.Length];
            for (int i = 0; i < _fadeRenderers.Length; i++)
                _fadeOriginals[i] = _fadeRenderers[i] != null ? _fadeRenderers[i].sharedMaterial : null;
        }

        private void Tint(Renderer r, Color c)
        {
            // 淡出時連 MaterialPropertyBlock 的顏色也要降 alpha，
            // 不然會蓋掉半透明材質原本的透明度
            c.a = _bodyFaded ? GameTuning.LocalPlayerFadeAlpha : 1f;
            if (r == null) return;
            _mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            r.SetPropertyBlock(_mpb);
        }

        // ---------------- IGarmentHost（隊友可以幫你穿衣服、噴漆） ----------------

        public Transform GarmentAnchor => _garmentAnchor != null ? _garmentAnchor : transform;
        public bool HasGarment => HasGarmentNet;
        public GarmentSpec Garment => WornGarment;

        public bool TryWear(GarmentSpec spec)
        {
            if (!HasStateAuthority || HasGarmentNet) return false;
            HasGarmentNet = true;
            WornGarment = spec;
            return true;
        }

        public bool TryTakeOff(out GarmentSpec spec)
        {
            spec = WornGarment;
            if (!HasStateAuthority || !HasGarmentNet) return false;
            HasGarmentNet = false;
            WornGarment = default;
            return true;
        }


        // ---------------- IStaggerable（被惡搞） ----------------

        public Transform StaggerAnchor => GarmentAnchor;
        public bool CanBeStaggered => Object != null && Object.IsValid;
        public string StaggerDisplayName => "隊友";

        public void ApplyBlind(float seconds)
        {
            if (!HasStateAuthority || _stagger == null) return;
            _stagger.Blind(seconds);
        }

        public void ApplyKnockback(Vector3 direction, float speed, float seconds)
        {
            if (!HasStateAuthority || _stagger == null) return;
            _stagger.Knockback(direction, speed, seconds);
        }

        /// <summary>
        /// 被打倒。身上的毛一次全部掉在地上 —— 這就是惡搞隊友的「回收價值」：
        /// 毛沒有消失，只是散了一地，有人得去撿。代價小、可回收、而且好笑。
        /// </summary>
        public void ApplyStagger(float seconds, bool dropWool)
        {
            if (!HasStateAuthority || _stagger == null) return;

            _stagger.Stagger(seconds);

            // 手上的東西也會脫手 —— 被卡車砸中還能死抓著羊毛不放很怪
            if (_carry != null && _carry.HasItem) _carry.Drop();

            if (dropWool) ScatterFleece();

            GameAudio.PlayAt(SfxId.Shear, transform.position);
        }

        /// <summary>身上的毛一次全部掉在腳邊。只在 StateAuthority 呼叫。</summary>
        private void ScatterFleece()
        {
            int count = Fleece;
            if (count <= 0) return;

            Fleece = 0;
            FleeceTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.FleeceRegenSeconds);

            for (int i = 0; i < count; i++)
            {
                var offset = Random.insideUnitCircle * GameTuning.KnockdownWoolSpread;
                var pos = transform.position + Vector3.up * 0.6f + new Vector3(offset.x, 0f, offset.y);
                ItemFactory.Spawn(Runner, ItemKind.Wool, default, pos);
            }
        }

        // ---------------- IInteractable（別的玩家對你做事） ----------------

        public Transform InteractionAnchor => GarmentAnchor;
        public int InteractionPriority => 2;   // 隊友優先於背後的機台

        /// <summary>
        /// 對隊友按左鍵的優先順序：手上工具的用途 -> 脫下衣服 -> 剃毛。
        ///
        /// 脫衣服排在剃毛前面，是因為它的條件嚴格得多（空手 + 對方身上真的有衣服），
        /// 而剃毛幾乎永遠成立。反過來排的話，隊友只要還有毛就永遠脫不下衣服。
        /// </summary>
        public bool CanInteract(in InteractionContext ctx)
        {
            if (ctx.Player == this) return false;

            if (ctx.Held is IGarmentHostUser user && user.TryUseOnHost(this, in ctx, false, out _))
                return true;

            if (ctx.IsEmptyHanded && HasGarmentNet) return true;   // 空手 -> 幫隊友脫下衣服

            return Fleece > 0;                                     // 其餘 -> 剃毛
        }

        public string GetPrompt(in InteractionContext ctx)
        {
            if (ctx.Player == this) return null;

            if (ctx.Held is IGarmentHostUser user && user.TryUseOnHost(this, in ctx, false, out var prompt))
                return prompt;
            if (ctx.IsEmptyHanded && HasGarmentNet)
                return $"[左鍵] 脫下 {WornGarment.Describe()}";
            if (Fleece > 0)
                return $"[左鍵] 剃毛（{Fleece}/{GameTuning.FleeceMax}）";
            return null;
        }

        public void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;

            if (ctx.Held is IGarmentHostUser user && user.TryUseOnHost(this, in ctx, true, out _))
                return;

            if (ctx.IsEmptyHanded && HasGarmentNet && TryTakeOff(out var spec))
            {
                ItemFactory.SpawnIntoHands(Runner, ItemKind.Garment, spec, ctx.Player);
                GameAudio.PlayAt(SfxId.DressOff, transform.position);
                return;
            }

            // 剃毛不需要拿著剃毛器 —— 按下去的瞬間自己伸出來
            if (Fleece > 0)
            {
                ctx.Player.TriggerShearVisual();
                Shear(ctx.Player);
            }
        }
    }
}
