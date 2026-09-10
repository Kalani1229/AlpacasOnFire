using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.Networking;
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
    public class PlayerController : NetworkBehaviour, IInteractable, IGarmentHost
    {
        public static PlayerController Local { get; private set; }

        [Header("Anchors")]
        [SerializeField] private Transform _handAnchor;
        [SerializeField] private Transform _headAnchor;
        [SerializeField] private Transform _catchAnchor;
        [SerializeField] private Transform _garmentAnchor;

        [Header("Visual")]
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private Renderer _garmentRenderer;
        [SerializeField] private Renderer _fleeceIndicator;

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
        [Networked] private NetworkButtons PreviousButtons { get; set; }

        private NetworkCharacterController _ncc;
        private PlayerCarry _carry;
        private Renderer[] _fadeRenderers;
        private Material[] _fadeOriginals;
        private bool _bodyFaded;
        private PlayerInteractor _interactor;
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
            CacheFadeRenderers();
            _interactor = new PlayerInteractor(this);
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

                if (HasStateAuthority)
                {
                    var ctx = _interactor.BuildContext();

                    if (pressed.IsSet(GameButton.ThrowCatch))
                        _carry.HandleThrow(in ctx);

                    if (pressed.IsSet(GameButton.Interact))
                    {
                        // 接住優先於情境互動：有東西滯空在接得到的範圍內時，
                        // Space／左鍵一律先算接住；沒接到才跑正常的互動。
                        if (!_carry.TryManualCatch())
                            _interactor.TryInteract();
                    }

                    // v6：E 拿出／收起隨身剃毛器
                    if (pressed.IsSet(GameButton.DefaultTool))
                        _carry.ToggleDefaultTool();

                    // v6：右鍵的次要互動（手提箱選色／切色）。
                    // 手上拿著 IHoldTool 時 FindSecondaryTarget 會直接回 null，
                    // 所以拿著噴槍時右鍵永遠是噴漆，不會被準心前方的東西搶走。
                    if (pressed.IsSet(GameButton.UseTool))
                        _interactor.TrySecondaryInteract();

                    _carry.TickTool(in ctx, input.Buttons.IsSet(GameButton.UseTool), Runner.DeltaTime);
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

        private void Move(Vector2 moveInput)
        {
            if (_ncc == null) return;

            // 手上有東西時走慢一點。maxSpeed 不是網路狀態，但它是依 HasItem（是網路狀態）
            // 推導出來的，所以重模擬時兩端算出來的值一致。
            _ncc.maxSpeed = GameTuning.MoveSpeed *
                            (_carry != null && _carry.HasItem ? GameTuning.CarrySlowFactor : 1f);

            // 移動方向永遠相對角色目前朝向
            var wish = transform.rotation * new Vector3(moveInput.x, 0f, moveInput.y);
            wish = ClampToStallZone(wish);
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

        /// <summary>被隊友剃毛。只在 StateAuthority 呼叫。</summary>
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
        }

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


        // ---------------- IInteractable（別的玩家對你做事） ----------------

        public Transform InteractionAnchor => GarmentAnchor;
        public int InteractionPriority => 2;   // 隊友優先於背後的機台

        public bool CanInteract(in InteractionContext ctx)
        {
            if (ctx.Player == this) return false;

            if (ctx.Held is IGarmentHostUser user)
                return user.TryUseOnHost(this, in ctx, false, out _);

            return ctx.IsEmptyHanded && HasGarmentNet;   // 空手 -> 幫隊友脫下衣服
        }

        public string GetPrompt(in InteractionContext ctx)
        {
            if (ctx.Held is IGarmentHostUser user && user.TryUseOnHost(this, in ctx, false, out var prompt))
                return prompt;
            if (ctx.IsEmptyHanded && HasGarmentNet)
                return $"[Space] 脫下 {WornGarment.Describe()}";
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
            }
        }
    }
}
