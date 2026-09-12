using AlpacasOnFire.Core;
using AlpacasOnFire.Items;
using AlpacasOnFire.Stall;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlpacasOnFire.Player
{
    /// <summary>
    /// 攝影機。只存在於本機玩家，不同步。**Tab 切換第一／第三人稱。**
    ///
    /// 共用朝向模型：yaw 直接取自 LocalInputProvider（角色也是用同一個值旋轉），
    /// 所以鏡頭與角色永遠一致；pitch 只影響鏡頭。
    ///
    /// 第三人稱是 spring arm ——攝影機碰撞從角色胸口往理想鏡頭位置做 SphereCast，
    /// 撞到東西就把鏡頭拉到撞點前方，拉近很快、放遠很慢，避免鏡頭在牆邊抖動。
    ///
    /// 第一人稱就是把鏡頭放到 HeadAnchor 上，沒有碰撞、沒有平滑、沒有側位移。
    ///
    /// **玩法完全不用改。** 互動射線本來就是從 PlayerController.HeadAnchor 沿
    /// AimDirection（= pitch/yaw）發出去的，跟鏡頭在哪無關 ——
    /// 第三人稱時鏡頭在後面，判定卻一直是從頭上算的。所以切成第一人稱之後
    /// 準心指向哪裡、打得到什麼，跟原本一模一樣。
    /// </summary>
    public class PlayerCameraRig : MonoBehaviour
    {
        public static PlayerCameraRig Instance { get; private set; }

        /// <summary>
        /// 現在是不是第一人稱。**static 是刻意的** —— 它是玩家的偏好，
        /// 死掉重生、換場景都該記得，不該跟著某一個 rig 實例生滅。
        /// </summary>
        public static bool FirstPerson { get; private set; }

        private Transform _target;
        private Camera _camera;
        private float _currentDistance = GameTuning.CameraDistance;
        private Vector3 _pivotVelocity;
        private Vector3 _smoothedPivot;
        private bool _initialised;
        private LayerMask _collisionMask;

        public Camera Camera => _camera;

        private void Awake()
        {
            Instance = this;
            _camera = GetComponentInChildren<Camera>();
            if (_camera == null)
            {
                var go = new GameObject("PlayerCamera");
                go.transform.SetParent(transform, false);
                _camera = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            if (_camera != null) _camera.gameObject.tag = "MainCamera";
            // 只排除玩家與可攜帶物件所在的 layer（由場景建置工具設定），其餘都會擋鏡頭
            _collisionMask = ~LayerMask.GetMask("Ignore Raycast", "Player", "CarriedItem");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetTarget(Transform target)
        {
            _target = target;
            _initialised = false;
        }

        /// <summary>
        /// Tab 切換視角。
        ///
        /// 直接在這裡讀鍵盤，**不經過 NetInput** —— 視角是純本機的表現偏好，
        /// 別人不需要知道你在用第幾人稱，送上網路只是浪費頻寬。
        /// 這也讓 NetInput（連線架構的一部分）一行都不用動。
        ///
        /// 有 UI 面板開著時不吃 Tab：那時候 Tab 是介面的切換鍵，
        /// 而且玩家的手不在遊戲上。
        /// </summary>
        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.tabKey.wasPressedThisFrame) return;

            var input = LocalInputProvider.Instance;
            if (input != null && !input.LookEnabled) return;

            FirstPerson = !FirstPerson;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            var input = LocalInputProvider.Instance;
            float yaw = input != null ? input.Yaw : _target.eulerAngles.y;
            float pitch = input != null ? input.Pitch : GameTuning.CameraDefaultPitch;

            var rotNow = Quaternion.Euler(pitch, yaw, 0f);
            var localPlayer = PlayerController.Local;

            // 身體的顯示交給視角決定：第一人稱藏起來，第三人稱交還給淡出邏輯
            if (localPlayer != null && localPlayer.Object != null && localPlayer.Object.IsValid)
                localPlayer.SetBodyHidden(FirstPerson);

            var tilt = UpdateStaggerTilt(localPlayer);

            if (FirstPerson)
            {
                // 沒有平滑、沒有碰撞、沒有側位移 —— 鏡頭就是眼睛。
                // 平滑跟隨在第一人稱會變成暈車的來源（畫面追不上自己的腳步）。
                var head = localPlayer != null ? localPlayer.HeadAnchor : null;
                var eye = head != null
                    ? head.position
                    : _target.position + Vector3.up * GameTuning.EyeHeight;

                // 位置用**沒有傾斜**的 rotNow 算，傾斜只加在旋轉上
                transform.position = eye + rotNow * Vector3.forward * GameTuning.FirstPersonForward
                                         + ShakeOffset();
                transform.rotation = rotNow * tilt;

                _initialised = false;   // 切回第三人稱時重抓 pivot，不要從舊位置滑過去
                return;
            }

            var pivot = _target.position + Vector3.up * GameTuning.CameraHeight;
            if (!_initialised)
            {
                _smoothedPivot = pivot;
                _initialised = true;
            }
            else
            {
                _smoothedPivot = Vector3.SmoothDamp(_smoothedPivot, pivot, ref _pivotVelocity,
                                                    GameTuning.CameraFollowSmooth);
            }

            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var back = rot * Vector3.back;
            var side = rot * Vector3.right * GameTuning.CameraSideOffset;
            var origin = _smoothedPivot + side;

            // ---- 攝影機碰撞 ----
            float desired = GameTuning.CameraDistance;
            if (Physics.SphereCast(origin, GameTuning.CameraCollisionRadius, back, out var hit,
                                   desired, _collisionMask, QueryTriggerInteraction.Ignore))
            {
                desired = Mathf.Max(0.6f, hit.distance - GameTuning.CameraCollisionBuffer);
            }

            float speed = desired < _currentDistance ? GameTuning.CameraZoomInSpeed : GameTuning.CameraZoomOutSpeed;
            _currentDistance = Mathf.MoveTowards(_currentDistance, desired, speed * Time.deltaTime);

            transform.position = origin + back * _currentDistance + ShakeOffset();
            transform.rotation = rot * tilt;

            UpdateBodyFade();
        }

        /// <summary>
        /// 倒地時鏡頭跟著側翻。
        ///
        /// **只轉旋轉、不動位置。** 位置是用沒有傾斜的 rot 算出來的，
        /// 連位置一起繞的話第三人稱會把鏡頭整個甩到側邊 ——
        /// 那不是「我倒了」，是「有人把攝影機扔出去了」。
        ///
        /// 倒下快（5.5）、爬起來慢（1.6），跟身體的翻倒同一個節奏。
        /// 爬起來的那一秒鏡頭慢慢轉正，是這個效果最好笑的部分。
        ///
        /// 側翻方向固定往右。要跟著被推的方向倒的話，得在倒下的瞬間把
        /// 擊退方向鎖起來（KnockbackTimer 0.35 秒就到期，但倒地有 1.1 秒），
        /// 多一個狀態換一點變化，目前不值得。
        /// </summary>
        private Quaternion UpdateStaggerTilt(PlayerController player)
        {
            bool down = player != null && player.Object != null && player.Object.IsValid
                        && player.IsStaggered;

            float target = down ? 1f : 0f;
            float speed = down ? GameTuning.StaggerCameraFallSpeed : GameTuning.StaggerCameraRiseSpeed;
            _staggerTilt = Mathf.MoveTowards(_staggerTilt, target, speed * Time.deltaTime);

            if (_staggerTilt <= 0.0001f) return Quaternion.identity;

            return Quaternion.Euler(GameTuning.StaggerCameraPitch * _staggerTilt,
                                    0f,
                                    GameTuning.StaggerCameraRoll * _staggerTilt);
        }

        private float _staggerTilt;

        /// <summary>
        /// 被打到時螢幕震一下。
        ///
        /// 只加在**位置**上、不動旋轉 —— 轉鏡頭會讓準心跟著飄，被害者接下來
        /// 那幾秒會瞄不準東西，那是懲罰不是笑點。位移則純粹是「哇」一下。
        ///
        /// 用 PerlinNoise 而不是 Random：每一幀的值是連續的，看起來像震動，
        /// 用 Random 會變成高頻閃爍，很難看也容易讓人不舒服。
        /// </summary>
        private Vector3 ShakeOffset()
        {
            var player = PlayerController.Local;
            if (player == null || player.Object == null || !player.Object.IsValid) return Vector3.zero;

            var status = player.GetComponent<Prank.StaggerStatus>();
            if (status == null || !status.Shaking) return Vector3.zero;

            float t = Time.time * 38f;
            float amp = GameTuning.CameraShakeAmplitude;

            return new Vector3((Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f * amp,
                               (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f * amp,
                               0f);
        }

        /// <summary>互動／塗抹射線用的方向（＝畫面中心）。</summary>
        public Vector3 AimDirection => transform.forward;

        /// <summary>
        /// 拿著顏料、而且準心正對著畫布時，把自己的身體淡掉 ——
        /// 不然第三人稱下羊駝會擋在鏡頭和衣服中間，看不到自己塗到哪裡。
        ///
        /// 條件刻意收得很窄（要同時「拿著顏料」和「瞄到畫布」），
        /// 這樣平常走路不會一直閃。
        /// </summary>
        private void UpdateBodyFade()
        {
            var player = PlayerController.Local;
            if (player == null || player.Object == null) return;

            // 第一人稱時身體已經整個藏起來了，不需要也不該再淡它
            if (FirstPerson) { player.SetBodyFaded(false); return; }

            bool shouldFade = false;

            if (player.Carry != null && player.Carry.Held is DyeCanisterTool)
            {
                float range = GameTuning.PaintRange + GameTuning.CameraDistance;
                var hits = Physics.RaycastAll(transform.position, transform.forward,
                                              range, ~0, QueryTriggerInteraction.Collide);
                foreach (var h in hits)
                {
                    var surface = h.collider.GetComponentInParent<GarmentPaintSurface>();
                    if (surface == null || !surface.Active) continue;
                    shouldFade = true;
                    break;
                }
            }

            player.SetBodyFaded(shouldFade);
        }
    }
}
