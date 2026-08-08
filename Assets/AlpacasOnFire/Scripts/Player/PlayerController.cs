using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.Networking;
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

        [Header("Camera Rig")]
        [SerializeField] private GameObject _cameraRigPrefab;

        // ---- 網路狀態 ----
        [Networked] public float Yaw { get; set; }
        [Networked] public float Pitch { get; set; }
        [Networked] public int ColorIndex { get; set; }
        [Networked] public int Fleece { get; set; }
        [Networked] public NetworkBool HasGarmentNet { get; set; }
        [Networked] public GarmentSpec WornGarment { get; set; }
        [Networked] public float PaintProgress { get; set; }
        [Networked] public int PaintColorRaw { get; set; }
        [Networked] private TickTimer FleeceTimer { get; set; }
        [Networked] private NetworkButtons PreviousButtons { get; set; }

        private NetworkCharacterController _ncc;
        private PlayerCarry _carry;
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
                        _carry.HandleThrowCatch(in ctx);

                    if (pressed.IsSet(GameButton.Interact))
                        _interactor.TryInteract();

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
            _ncc.Move(wish);
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

        private void Tint(Renderer r, Color c)
        {
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
            PaintProgress = 0f;
            PaintColorRaw = (int)DyeColorType.White;
            return true;
        }

        public bool TryTakeOff(out GarmentSpec spec)
        {
            spec = WornGarment;
            if (!HasStateAuthority || !HasGarmentNet) return false;
            HasGarmentNet = false;
            WornGarment = default;
            PaintProgress = 0f;
            return true;
        }

        public void AddPaint(DyeColorType color, float amount)
        {
            if (!HasStateAuthority || !HasGarmentNet) return;

            if (PaintColorRaw != (int)color)
            {
                PaintColorRaw = (int)color;
                PaintProgress = 0f;
            }

            PaintProgress += amount;
            if (PaintProgress < GameTuning.SprayPaintRequired) return;

            PaintProgress = 0f;
            var spec = WornGarment;
            spec.Color = color;
            WornGarment = spec;
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
