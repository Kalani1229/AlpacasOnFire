using AlpacasOnFire.Core;
using AlpacasOnFire.Items;
using AlpacasOnFire.Stall;
using UnityEngine;

namespace AlpacasOnFire.Player
{
    /// <summary>
    /// 第三人稱 spring arm。只存在於本機玩家，不同步。
    ///
    /// 共用朝向模型：yaw 直接取自 LocalInputProvider（角色也是用同一個值旋轉），
    /// 所以鏡頭與角色永遠一致；pitch 只影響鏡頭。
    ///
    /// 攝影機碰撞：從角色胸口往理想鏡頭位置做 SphereCast，撞到東西就把鏡頭拉到撞點前方，
    /// 拉近很快、放遠很慢，避免鏡頭在牆邊抖動。
    /// </summary>
    public class PlayerCameraRig : MonoBehaviour
    {
        public static PlayerCameraRig Instance { get; private set; }

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

        private void LateUpdate()
        {
            if (_target == null) return;

            var input = LocalInputProvider.Instance;
            float yaw = input != null ? input.Yaw : _target.eulerAngles.y;
            float pitch = input != null ? input.Pitch : GameTuning.CameraDefaultPitch;

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

            transform.position = origin + back * _currentDistance;
            transform.rotation = rot;

            UpdateBodyFade();
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
