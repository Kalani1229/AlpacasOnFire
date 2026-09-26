using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Prank;
using UnityEngine;

namespace AlpacasOnFire.Player
{
    /// <summary>
    /// 被卡車炸到時的 active ragdoll。**純本機視覺。**
    ///
    /// ---
    ///
    /// ### 為什麼一個 [Networked] 欄位都沒有
    ///
    /// Unity 的 PhysX 跨機器不是決定性的，而 Fusion 會重模擬過去的 tick。
    /// 十幾個關節每重跑一次就多累積一點誤差，幾秒之內各端就會各自飛走。
    /// 所以這支不碰任何網路回呼、不新增任何 [Networked]；各端自己演自己的，
    /// 姿勢細節不同沒關係。**倒地與站起的時間點仍然一致** ——
    /// 那是讀 <see cref="StaggerStatus.Staggered"/> 來的，而那個是同步的。
    ///
    /// ### 位置的權威仍然是 NetworkCharacterController
    ///
    /// 膠囊照舊由 NCC 控制。**骨頭從不推膠囊**，方向永遠是膠囊把骨頭拉回來。
    ///
    /// ### 倒地期間骨架會暫時脫離角色
    ///
    /// 骨頭如果一直掛在角色底下，角色每動一下（擊退、**滑鼠轉朝向**）都會把
    /// 整副骨架硬拖著走 —— Unity 會把父物件的移動當成瞬移同步給子物件的剛體。
    /// 倒在地上時晃一下滑鼠，整隻羊駝就被甩一圈，這就是第一版「摔太猛」的一半原因。
    ///
    /// 所以倒地的那一刻把 Armature 拆到場景根下，骨頭真正活在世界空間裡；
    /// 靠「栓繩」和「扶正」兩股力跟著膠囊走。站起來時再裝回去。
    ///
    /// ### 目標姿勢是一個靜態快照
    ///
    /// v1 不做第二套骨架。完全癱軟時目標姿勢不重要；恢復時朝一個固定站姿施力，
    /// 讀起來就是「掙扎著想站直」。
    /// </summary>
    [DisallowMultipleComponent]
    public class RagdollRig : MonoBehaviour
    {
        /// <summary>
        /// Ragdoll 骨頭專用的 layer 名稱。
        /// 骨頭只跟地面與牆壁碰撞，不跟玩家、物品、互動探測碰撞。
        /// </summary>
        public const string LayerName = "Ragdoll";

        [Header("Ragdoll")]
        [Tooltip("骨架設定。留空的話整支靜默 —— 膠囊佔位版本沒有骨架，不該吼錯誤。")]
        [SerializeField] private RagdollProfile _profile;

        [Tooltip("模型上的 Animator（在 Visual 子物件）。倒地時會被關掉。")]
        [SerializeField] private Animator _animator;

        [Tooltip("骨架的根，通常是 Visual/Armature。留空會自己找。")]
        [SerializeField] private Transform _skeletonRoot;

        private class Joint
        {
            public Transform bone;
            public Rigidbody body;
            public ConfigurableJoint joint;
            public Collider collider;
            public RagdollBone setup;

            /// <summary>站立姿勢快照：ragdoll 期間所有關節都朝這個固定姿勢施力。</summary>
            public Quaternion standPose;

            /// <summary>建關節當下的 localRotation，SetTargetRotationLocal 要用它當基準。</summary>
            public Quaternion jointStartPose;

            /// <summary>混回動畫時的起點（ragdoll 結束那一刻的姿勢）。</summary>
            public Quaternion blendFrom;
        }

        private readonly List<Joint> _joints = new();
        private readonly List<Collider> _allColliders = new();
        private SkinnedMeshRenderer[] _skins;

        private Transform _rootBone;
        private Rigidbody _rootBody;
        private Collider _rootCollider;
        private Quaternion _rootStandPose;
        private Vector3 _rootStandLocalPos;

        // 混回動畫時根骨的起點。**用世界座標記**：Armature 裝回去的那一刻
        // 根骨的 local 值會整個換掉，只有世界座標是連續的。
        private Vector3 _rootBlendFromPos;
        private Quaternion _rootBlendFromRot;

        // Armature 原本掛在哪、原本的 local 姿勢。拆下來之後要照這個裝回去。
        private Transform _armatureParent;
        private Vector3 _armatureLocalPos;
        private Quaternion _armatureLocalRot;
        private Vector3 _armatureLocalScale;
        private bool _detached;

        private StaggerStatus _stagger;

        private bool _built;
        private bool _active;
        private float _elapsed;
        private float _total;
        private float _blendBack;

        /// <summary>骨架建起來了沒。false 時這支完全不作用，倒地退回舊的整隻傾倒。</summary>
        public bool Ready => _built && _joints.Count > 0;

        /// <summary>現在正在演 ragdoll（含混回動畫的尾巴）。</summary>
        public bool Playing => _active || _blendBack > 0f;

        /// <summary>
        /// 失控結束後「混回站姿」要多久。PlayerController 拿這個延長鎖操作的時間 ——
        /// 讀的是 profile 的設定值（兩端一樣），不是本機演到哪，所以不會讓模擬依賴畫面。
        /// </summary>
        public float BlendBackSeconds => Ready && _profile != null ? Mathf.Max(0f, _profile.blendBackTime) : 0f;

        // ================================================================ 建置

        private void Awake()
        {
            _stagger = GetComponent<StaggerStatus>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>(true);

            if (_profile == null)
            {
                Debug.LogWarning($"[Ragdoll] {name} 沒有接上 RagdollProfile —— 倒地會退回整隻傾倒。" +
                                 "先跑「Ragdoll > 1. 掃描羊駝骨架」，再跑「1. 建置佔位資產」。");
            }
            else if (_profile.unitsVersion < RagdollProfile.CurrentUnitsVersion)
            {
                Debug.LogWarning("[Ragdoll] RagdollProfile 是舊的單位版本（骨頭本地單位，會被放大 50 倍）。" +
                                 "請重跑「Ragdoll > 1. 掃描羊駝骨架」，它會自動換成公尺。");
            }

            Build();
        }

        /// <summary>
        /// 把骨架建起來，然後立刻收成待機狀態。
        ///
        /// **一定要在 Awake 做** —— 站立姿勢的快照必須是還沒被任何動畫動過的綁定姿勢。
        /// Animator 跑起來之後再抓，抓到的會是「Idle 第 n 幀」，恢復時的站姿會一次比一次歪。
        /// </summary>
        private void Build()
        {
            if (_built) return;
            if (_profile == null || _profile.bones == null || _profile.bones.Length == 0) return;

            if (_skeletonRoot == null) _skeletonRoot = FindSkeletonRoot();
            if (_skeletonRoot == null) return;

            _rootBone = FindBone(_skeletonRoot, _profile.rootBoneName);
            if (_rootBone == null)
            {
                Debug.LogWarning($"[Ragdoll] 找不到根骨 {_profile.rootBoneName}，ragdoll 不會啟用。");
                return;
            }

            int layer = LayerMask.NameToLayer(LayerName);
            if (layer < 0)
            {
                Debug.LogWarning($"[Ragdoll] 沒有 {LayerName} layer —— 請重跑「1. 建置佔位資產」。");
                layer = gameObject.layer;
            }

            _armatureParent = _skeletonRoot.parent;
            _armatureLocalPos = _skeletonRoot.localPosition;
            _armatureLocalRot = _skeletonRoot.localRotation;
            _armatureLocalScale = _skeletonRoot.localScale;

            _rootStandPose = _rootBone.localRotation;
            _rootStandLocalPos = _rootBone.localPosition;

            _rootBody = EnsureBody(_rootBone, _profile.rootMass);
            _rootCollider = EnsureCollider(_rootBone, _profile.rootRadius, _profile.rootLength, layer);
            _allColliders.Add(_rootCollider);

            foreach (var setup in _profile.bones)
            {
                if (setup == null || !setup.enabled || string.IsNullOrEmpty(setup.boneName)) continue;

                var bone = FindBone(_skeletonRoot, setup.boneName);
                if (bone == null)
                {
                    Debug.LogWarning($"[Ragdoll] 找不到骨頭 {setup.boneName}，跳過。");
                    continue;
                }

                var body = EnsureBody(bone, setup.mass);
                var col = EnsureCollider(bone, setup.radius, setup.length, layer);
                _allColliders.Add(col);

                // 連到**最近的 ragdoll 祖先**，不是直接的父骨頭。
                // 跳過的骨頭（chest.L/R、hip.L/R）就這樣被當成剛體的一部分帶著走。
                var parentBody = FindParentBody(bone);
                if (parentBody == null) continue;

                _joints.Add(new Joint
                {
                    bone = bone,
                    body = body,
                    joint = CreateJoint(body, parentBody, setup),
                    collider = col,
                    setup = setup,
                    standPose = bone.localRotation,
                    jointStartPose = bone.localRotation,
                });
            }

            var visualRoot = _animator != null ? _animator.transform : transform;
            _skins = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            _built = true;
            SetPhysicsActive(false);

            Debug.Log($"[Ragdoll] {name} 骨架已建立：{_joints.Count} 個關節（根骨 {_rootBone.name}，" +
                      $"骨頭縮放 {_rootBone.lossyScale.x:0.##}）");
        }

        private Transform FindSkeletonRoot()
        {
            var scope = _animator != null ? _animator.transform : transform;
            var armature = FindBone(scope, "Armature");
            return armature != null ? armature : scope;
        }

        private static Transform FindBone(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            if (root.name == name) return root;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static Rigidbody EnsureBody(Transform bone, float mass)
        {
            var rb = bone.GetComponent<Rigidbody>();
            if (rb == null) rb = bone.gameObject.AddComponent<Rigidbody>();

            rb.mass = Mathf.Max(0.05f, mass);
            rb.useGravity = true;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;   // 倒地時才開，理由見 SetPhysicsActive

            // 骨頭小、又會被甩得很快，離散碰撞會直接穿過地板
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // 關節鏈越長，預設的 6 次迭代越不夠，會看到四肢一直在細微抖動
            rb.solverIterations = 12;
            rb.solverVelocityIterations = 4;
            return rb;
        }

        /// <summary>
        /// 骨頭上的膠囊。**profile 裡填的是世界公尺，這裡負責除掉骨頭的縮放。**
        ///
        /// MD_Alpaca 的骨頭實際縮放是 50（Armature 100 × Visual 0.5）。
        /// 第一版直接把 profile 的值當本地單位用，0.06 的半徑就變成 3 公尺 ——
        /// 十幾顆巨大的膠囊疊在一起，一啟動就互相推擠爆開。
        ///
        /// 長度填 0 時自動量：取「最順著骨頭方向（+Y）」的那個子骨頭的距離。
        /// 沒有子骨頭（腿的末節、尾巴）就退而用自己到父骨頭的距離當估計。
        /// </summary>
        private static Collider EnsureCollider(Transform bone, float radiusMeters, float lengthMeters, int layer)
        {
            var col = bone.GetComponent<CapsuleCollider>();
            if (col == null) col = bone.gameObject.AddComponent<CapsuleCollider>();

            float scale = Mathf.Max(0.0001f, Mathf.Abs(bone.lossyScale.y));

            if (lengthMeters <= 0f) lengthMeters = AutoLengthMeters(bone);
            radiusMeters = Mathf.Max(0.01f, radiusMeters);
            lengthMeters = Mathf.Max(radiusMeters * 2f, lengthMeters);

            // Blender 匯出的骨頭一律沿本地 +Y 長出去（子骨頭的 localPosition 幾乎都是 (0, y, 0)）
            col.direction = 1;
            col.radius = radiusMeters / scale;
            col.height = lengthMeters / scale;

            // 骨頭原點在頭端（關節處），骨身往 +Y 長；center 放原點的話膠囊一半會戳進父骨頭
            col.center = new Vector3(0f, col.height * 0.5f, 0f);

            bone.gameObject.layer = layer;
            return col;
        }

        private static float AutoLengthMeters(Transform bone)
        {
            Transform best = null;
            float bestY = 0f;
            for (int i = 0; i < bone.childCount; i++)
            {
                var c = bone.GetChild(i);
                if (c.localPosition.y > bestY) { bestY = c.localPosition.y; best = c; }
            }

            if (best != null) return Vector3.Distance(bone.position, best.position);
            if (bone.parent != null) return Vector3.Distance(bone.position, bone.parent.position);
            return 0.2f;
        }

        private Rigidbody FindParentBody(Transform bone)
        {
            var p = bone.parent;
            while (p != null)
            {
                var rb = p.GetComponent<Rigidbody>();
                if (rb != null) return rb;
                if (p == _skeletonRoot) break;
                p = p.parent;
            }
            return _rootBody;
        }

        /// <summary>
        /// 關節設定。**這裡是最容易寫錯的地方**：
        ///  - `rotationDriveMode = Slerp`：力道來自 `slerpDrive`。用線性 drive 關節完全沒力。
        ///  - `slerpDrive.positionSpring`：名字叫 position 但它是**角度**驅動的勁度。
        ///  - 三軸 Motion 全部 Locked：骨頭不該被拉開，只該轉。
        ///  - `configuredInWorldSpace = false`：目標姿勢是 local 的。
        /// </summary>
        private static ConfigurableJoint CreateJoint(Rigidbody body, Rigidbody parent, RagdollBone setup)
        {
            var j = body.GetComponent<ConfigurableJoint>();
            if (j == null) j = body.gameObject.AddComponent<ConfigurableJoint>();

            j.connectedBody = parent;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = Vector3.zero;
            j.connectedAnchor = parent.transform.InverseTransformPoint(body.transform.position);

            j.axis = setup.jointAxis.sqrMagnitude > 0.0001f ? setup.jointAxis.normalized : Vector3.right;
            j.secondaryAxis = Vector3.up;

            j.xMotion = ConfigurableJointMotion.Locked;
            j.yMotion = ConfigurableJointMotion.Locked;
            j.zMotion = ConfigurableJointMotion.Locked;

            j.angularXMotion = ConfigurableJointMotion.Limited;
            j.angularYMotion = ConfigurableJointMotion.Limited;
            j.angularZMotion = ConfigurableJointMotion.Limited;

            j.lowAngularXLimit = new SoftJointLimit { limit = setup.limitLow };
            j.highAngularXLimit = new SoftJointLimit { limit = setup.limitHigh };
            j.angularYLimit = new SoftJointLimit { limit = setup.swingLimit };
            j.angularZLimit = new SoftJointLimit { limit = setup.swingLimit };

            j.configuredInWorldSpace = false;
            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.slerpDrive = new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = 0f };

            j.enablePreprocessing = false;
            return j;
        }

        // ================================================================ 每幀

        /// <summary>
        /// **為什麼是 LateUpdate 而不是 Render()。**
        ///
        /// 規格「全部放在 Render()」真正要擋的是 `FixedUpdateNetwork()` ——
        /// 不要進網路 tick、不要被重模擬跑到。這一點這裡完全遵守。
        ///
        /// 但混回動畫那一段**必須跑在 Animator 之後**：Animator 每幀會把骨頭整個寫掉，
        /// 在它之前寫的值當場就被蓋掉。Unity 保證 Animator 在 LateUpdate 之前，Render() 不保證。
        /// </summary>
        private void LateUpdate()
        {
            if (!Ready) return;

            bool down = _stagger != null && _stagger.Runner != null && _stagger.Staggered;

            if (down && !_active) Begin();
            else if (!down && _active) End();

            if (_active)
            {
                _elapsed += Time.deltaTime;
                DriveJoints();
            }
            else if (_blendBack > 0f)
            {
                BlendBack();
            }
        }

        /// <summary>
        /// 施力（栓繩、扶正骨盆）跑在物理步上。AddForce 是累加到下一個物理步的，
        /// 放在 LateUpdate 的話力道會跟 FPS 有關。
        /// 這**仍然是 Unity 的物理步，不是 Fusion 的網路 tick**。
        /// </summary>
        private void FixedUpdate()
        {
            if (!Ready || !_active) return;

            CancelGravity();

            // 站回階段：骨盆改由程式直接帶到站姿，保證最後是正的
            float u = StandProgress01();
            if (u >= 0f)
            {
                DriveHipsKinematic(u);
                return;
            }

            DriveHips();
            LeashHips();
        }

        /// <summary>
        /// **站姿目標在每次倒地的那一刻，從動畫當下的姿勢抓。**
        ///
        /// 第一版是在 Awake 抓 FBX 的綁定姿勢，但 Blender 匯出的四足骨架，
        /// 綁定姿勢跟動畫裡「站著」的姿勢差很多（Idle 裡 spine.001 相對 Armature
        /// 轉了約 172°）。扶正本身沒有錯，是精準地扶到一個錯的目標 ——
        /// 所以站起來之後身體是歪的，甚至整個水平。
        ///
        /// 改成倒下那一刻抓：這時候 Animator 剛寫完這一幀（我們在 LateUpdate），
        /// 骨頭就是「羊駝站著」的樣子。而且這正好是混回動畫的終點，站回與混回會自然接上。
        ///
        /// **jointStartPose 不動。** 它是建關節當下的姿勢，SetTargetRotationLocal 的基準，
        /// 必須跟 PhysX 記住的那個零點一致。
        /// </summary>
        private void CaptureStandPose()
        {
            _armatureLocalPos = _skeletonRoot.localPosition;
            _armatureLocalRot = _skeletonRoot.localRotation;
            _armatureLocalScale = _skeletonRoot.localScale;

            _rootStandPose = _rootBone.localRotation;
            _rootStandLocalPos = _rootBone.localPosition;

            foreach (var j in _joints)
                if (j.bone != null) j.standPose = j.bone.localRotation;

            _hasStandPose = true;
        }

        private bool _hasStandPose;

        /// <summary>站回階段的進度 0~1；還沒到站回階段回 -1。</summary>
        private float StandProgress01()
        {
            float limp = Mathf.Min(_profile.limpSeconds, _total * _profile.limpMaxShare);
            float after = _total - limp;
            if (after <= 0.0001f) return _elapsed > limp ? 1f : -1f;

            float standStart = limp + after * _profile.struggleShare;
            if (_elapsed < standStart) return -1f;

            float standLen = _total - standStart;
            return standLen > 0.0001f ? Mathf.Clamp01((_elapsed - standStart) / standLen) : 1f;
        }

        /// <summary>
        /// **站回階段，骨盆不再靠彈簧拉，改成直接帶到站姿。**
        ///
        /// 彈簧是「施力」，拉不拉得到位要看跟四肢、地面摩擦的拔河結果。
        /// 站回階段通常只有零點幾秒，拉不到位就會帶著歪掉的姿勢進入混回動畫。
        ///
        /// 這裡把骨盆切成 kinematic，從它目前躺著的姿勢平滑帶到站姿
        /// （MovePosition / MoveRotation，物理會正確推動掛在上面的關節）。
        /// 四肢仍然是物理的，靠關節彈簧跟上來 —— 看起來是「整隻撐起來」，
        /// 而骨盆的方向在階段結束時**保證**是正的。
        ///
        /// 這是純視覺的 ragdoll，骨盆被程式接管沒有任何判定上的後果。
        /// </summary>
        private void DriveHipsKinematic(float u)
        {
            if (_rootBody == null) return;

            if (!_standTakeover)
            {
                _standTakeover = true;
                _standFromPos = _rootBody.position;
                _standFromRot = _rootBody.rotation;
                _rootBody.isKinematic = true;
            }

            HipsStandTarget(out var targetPos, out var targetRot);

            float e = u * u * (3f - 2f * u);   // smoothstep：起身與站定都不會有速度突變
            _rootBody.MovePosition(Vector3.Lerp(_standFromPos, targetPos, e));
            _rootBody.MoveRotation(Quaternion.Slerp(_standFromRot, targetRot, e));
        }

        private bool _standTakeover;
        private Vector3 _standFromPos;
        private Quaternion _standFromRot;

        /// <summary>
        /// 恢復時把重力逐步抵銷掉。**這是 active ragdoll 站得起來的關鍵。**
        ///
        /// 扶正的力只推在骨盆上，但頭、胸、四條腿的重量全部經由關節掛在骨盆上往下扯
        /// （骨盆 4 公斤、其餘加起來將近 10 公斤）。不抵銷的話，骨盆的彈簧要跟整隻羊駝的
        /// 體重拔河，力道拉滿也只會半蹲在地上。
        ///
        /// 抵銷量跟階段同步：癱軟時 0（照常摔）、站回時到 antiGravity（預設全部抵銷），
        /// 這時候姿勢完全由關節彈簧與骨盆扶正決定，收斂得乾淨。
        /// 每根骨頭各自抵銷，用 Acceleration 模式 —— 跟質量無關，調質量不會影響站不站得起來。
        /// </summary>
        private void CancelGravity()
        {
            float s = SpringScale01() * _profile.antiGravity;
            if (s <= 0f) return;

            var up = -Physics.gravity * s;
            if (_rootBody != null && !_rootBody.isKinematic) _rootBody.AddForce(up, ForceMode.Acceleration);
            foreach (var j in _joints)
                if (j.body != null && !j.body.isKinematic) j.body.AddForce(up, ForceMode.Acceleration);
        }

        private void Begin()
        {
            // 混回動畫還沒跑完就又被炸（連續兩次）：這時候骨頭是半 ragdoll 半動畫的姿勢，
            // 不能拿來當站姿目標 —— 沿用上一次抓到的就好，那個是乾淨的
            bool poseIsClean = _blendBack <= 0f || !_hasStandPose;

            _active = true;
            _elapsed = 0f;
            _blendBack = 0f;

            // 讀剩餘時間而不是常數 —— 連續被炸兩次時 StaggerStatus 會取比較長的那個
            float left = _stagger != null && _stagger.Runner != null
                ? (_stagger.StaggerTimer.RemainingTime(_stagger.Runner) ?? 0f)
                : GameTuning.TruckStaggerSeconds;
            _total = Mathf.Max(0.2f, left);

            // Animator 一定要關，不然它每幀把骨頭寫回動畫姿勢，跟關節搶同一批 Transform
            if (_animator != null) _animator.enabled = false;

            // 每次倒地都照 profile 重新套一次質量／尺寸／角度上限，
            // 這樣 Play 中在 Inspector 改的值下一次倒地就看得到，不用重開
            ApplySetup();

            // 講一聲這一次怎麼分配時間 —— 「看起來沒反應」時，先看有沒有這行、
            // 以及癱軟到底有多短
            float limp = Mathf.Min(_profile.limpSeconds, _total * _profile.limpMaxShare);
            float after = Mathf.Max(0f, _total - limp);
            Debug.Log($"[Ragdoll] {name} 倒地 {_total:0.00} 秒：癱軟 {limp:0.00} / " +
                      $"掙扎 {after * _profile.struggleShare:0.00} / 站回 {after * (1f - _profile.struggleShare):0.00}");

            if (poseIsClean) CaptureStandPose();
            _standTakeover = false;

            Detach();
            SetPhysicsActive(true);

            // 跟著擊退一起飛出去。骨架已經脫離角色了，不給初速的話羊駝會原地倒下、
            // 膠囊自己滑走，然後骨頭才被栓繩拖過去 —— 看起來像是「倒了之後被人拖走」。
            // CurrentKnockback 是 [Networked] 推導出來的，每一端讀到的都一樣。
            var kick = _stagger != null ? _stagger.CurrentKnockback : Vector3.zero;
            kick += Vector3.up * (kick.sqrMagnitude > 0.01f ? _profile.kickUp : 0f);   // 被炸的才往上彈一點
            SetAllVelocities(kick);
        }

        /// <summary>
        /// 把 profile 目前的質量、碰撞體尺寸、角度上限重新套到骨頭上。
        ///
        /// **不重設 jointAxis。** 改 axis 會讓 PhysX 以「當下的姿勢」重新定義關節的零點，
        /// 而那時候骨頭是動畫姿勢不是綁定姿勢 —— SetTargetRotationLocal 的基準就對不上了，
        /// 恢復時的站姿會整個歪掉。改軸要重新 Play。
        /// </summary>
        private void ApplySetup()
        {
            if (_rootBody != null) _rootBody.mass = Mathf.Max(0.05f, _profile.rootMass);
            if (_rootBone != null)
                EnsureCollider(_rootBone, _profile.rootRadius, _profile.rootLength, _rootBone.gameObject.layer);

            foreach (var j in _joints)
            {
                var s = j.setup;
                if (s == null || j.bone == null) continue;

                if (j.body != null) j.body.mass = Mathf.Max(0.05f, s.mass);
                EnsureCollider(j.bone, s.radius, s.length, j.bone.gameObject.layer);

                if (j.joint == null) continue;
                j.joint.lowAngularXLimit = new SoftJointLimit { limit = s.limitLow };
                j.joint.highAngularXLimit = new SoftJointLimit { limit = s.limitHigh };
                j.joint.angularYLimit = new SoftJointLimit { limit = s.swingLimit };
                j.joint.angularZLimit = new SoftJointLimit { limit = s.swingLimit };
            }
        }

        private void End()
        {
            _active = false;
            _blendBack = _profile.blendBackTime;

            // 起點記在「裝回去之前」：根骨用世界座標，關節用 local（它們相對父骨頭，裝回去不受影響）
            _rootBlendFromPos = _rootBone.position;
            _rootBlendFromRot = _rootBone.rotation;
            foreach (var j in _joints) j.blendFrom = j.bone.localRotation;

            SetPhysicsActive(false);
            Reattach();

            // 裝回去的那一刻 Armature 會跳到膠囊的位置，根骨跟著跳。
            // 立刻把根骨放回剛剛的世界位置，混回動畫才是從這裡平滑開始，沒有一幀的閃現。
            _rootBone.SetPositionAndRotation(_rootBlendFromPos, _rootBlendFromRot);
        }

        // ================================================================ 脫離 / 裝回

        /// <summary>
        /// 把 Armature 拆到場景根下。骨頭從此活在世界空間，不會被角色的移動或轉向拖著走。
        ///
        /// 只拆 Armature（純骨架），蒙皮的網格留在原地 —— SkinnedMeshRenderer
        /// 是照骨頭的世界位置變形的，骨頭在哪網格就畫在哪，不需要跟著搬。
        /// </summary>
        private void Detach()
        {
            if (_detached || !CanDetach()) return;

            _skeletonRoot.SetParent(null, true);
            _detached = true;

            // 骨頭離開原本的位置之後，網格的包圍盒要每幀重算，
            // 不然 Unity 用舊的包圍盒判斷「不在鏡頭裡」，整隻羊駝會突然消失
            if (_skins != null)
                foreach (var s in _skins) if (s != null) s.updateWhenOffscreen = true;
        }

        private void Reattach()
        {
            if (!_detached) return;

            // Animator 是照路徑（Armature/spine.001/...）綁骨頭的，
            // **一定要在重新啟用 Animator 之前裝回去**，不然它綁不到骨頭。
            _skeletonRoot.SetParent(_armatureParent, false);
            _skeletonRoot.localPosition = _armatureLocalPos;
            _skeletonRoot.localRotation = _armatureLocalRot;
            _skeletonRoot.localScale = _armatureLocalScale;
            _detached = false;

            if (_skins != null)
                foreach (var s in _skins) if (s != null) s.updateWhenOffscreen = false;
        }

        private bool CanDetach()
        {
            // 找不到真正的 Armature 時 _skeletonRoot 會退回 Visual 本身 ——
            // 那不能拆，拆了連網格一起走
            if (_skeletonRoot == null || _armatureParent == null) return false;
            if (_skeletonRoot == transform) return false;
            if (_animator != null && _skeletonRoot == _animator.transform) return false;
            return true;
        }

        /// <summary>玩家在倒地中被移除（斷線、換場景）時，拆出去的骨架要一起清掉。</summary>
        private void OnDestroy()
        {
            if (_detached && _skeletonRoot != null) Destroy(_skeletonRoot.gameObject);
        }

        // ================================================================ 物理開關

        /// <summary>
        /// 待機／倒地的切換。
        ///
        /// 待機時全部 kinematic、碰撞體全關：骨頭完全交給 Animator。
        /// 碰撞體要關是因為射線查詢不看碰撞矩陣 —— 鏡頭的 spring arm、放置預覽、
        /// 互動探測都會掃到藏在身體裡的膠囊（鏡頭被拉到貼在羊駝身上就是這個）。
        ///
        /// 插值只在倒地時開：待機時骨頭是 Animator 在寫，插值會把動畫蓋得慢半拍。
        /// </summary>
        private void SetPhysicsActive(bool on)
        {
            var interp = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;

            foreach (var c in _allColliders) if (c != null) c.enabled = on;
            if (on) IgnoreSelfCollisions();

            SetBodyActive(_rootBody, on, interp);
            foreach (var j in _joints) SetBodyActive(j.body, on, interp);
        }

        /// <summary>
        /// **同一隻羊駝的骨頭彼此不碰撞。**
        ///
        /// 綁定姿勢下相鄰的膠囊本來就疊在一起（關節處、腿根貼著軀幹），
        /// 開著碰撞的話倒地第一幀物理會拚命把它們推開 —— 整隻炸開。
        /// 不同玩家的骨頭仍然會互相碰（Ragdoll 層對自己是開著的）。
        ///
        /// **每次倒地都重新設一次**，不是只在 Build 設：碰撞體被停用再啟用時，
        /// Unity 可能會把忽略配對清掉，而這些碰撞體每次站起來都會被關掉。
        /// 13 顆 = 78 對，成本可以忽略。
        /// </summary>
        private void IgnoreSelfCollisions()
        {
            for (int a = 0; a < _allColliders.Count; a++)
                for (int b = a + 1; b < _allColliders.Count; b++)
                    if (_allColliders[a] != null && _allColliders[b] != null)
                        Physics.IgnoreCollision(_allColliders[a], _allColliders[b], true);
        }

        private static void SetBodyActive(Rigidbody rb, bool on, RigidbodyInterpolation interp)
        {
            if (rb == null) return;
            rb.isKinematic = !on;
            rb.interpolation = interp;
            if (!on) return;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        private void SetAllVelocities(Vector3 v)
        {
            if (_rootBody != null && !_rootBody.isKinematic) _rootBody.linearVelocity = v;
            foreach (var j in _joints)
                if (j.body != null && !j.body.isKinematic) j.body.linearVelocity = v;
        }

        // ================================================================ 三階段

        /// <summary>
        /// 這一刻的力道比例 0~1。**笑點全在這條曲線上。**
        /// <code>
        /// ① 癱軟   0             完全沒力，該怎麼摔就怎麼摔
        /// ② 掙扎   0 -> 60%      想撐起來但撐不直
        /// ③ 站回   60% -> 100%   收斂回站姿
        /// </code>
        /// 三段是從整段時間**按比例**切的，理由見 _profile.limpMaxShare。
        /// </summary>
        private float SpringScale01()
        {
            float limp = Mathf.Min(_profile.limpSeconds, _total * _profile.limpMaxShare);
            if (_elapsed <= limp) return 0f;

            float after = _total - limp;
            if (after <= 0.0001f) return 1f;

            float struggle = after * _profile.struggleShare;
            float t = _elapsed - limp;

            if (t <= struggle)
                return Mathf.Lerp(0f, _profile.struggleSpring, struggle > 0.0001f ? t / struggle : 1f);

            float standLen = after - struggle;
            float u = standLen > 0.0001f ? Mathf.Clamp01((t - struggle) / standLen) : 1f;

            // **站回是「一口氣撐起來」，不是慢慢爬。** 用 ease-out：一進站回階段力道就衝上去
            // （前 20% 的時間就到一半，40% 到八成），剩下的時間拿來收斂姿勢。
            // 線性的話掙扎力道設 0 時，站回前半段幾乎沒力，站回階段又只有 0.3 秒左右，
            // 結果就是「站不起來，最後被混回動畫瞬移扶正」。
            float eased = 1f - (1f - u) * (1f - u) * (1f - u);
            return Mathf.Lerp(_profile.struggleSpring, 1f, eased);
        }

        private void DriveJoints()
        {
            float scale = SpringScale01();

            foreach (var j in _joints)
            {
                if (j.joint == null) continue;

                float spring = _profile.maxSpring * scale * j.setup.springScale;
                float force = _profile.maxForce * scale * j.setup.springScale;

                j.joint.slerpDrive = new JointDrive
                {
                    positionSpring = spring,
                    positionDamper = spring * 0.1f,   // 跟著 spring 走，不然拉滿時會彈過頭
                    maximumForce = Mathf.Max(0f, force),
                };

                SetTargetRotationLocal(j.joint, j.standPose, j.jointStartPose);
            }
        }

        /// <summary>
        /// 把「想要的 local rotation」換算成 `ConfigurableJoint.targetRotation`。
        /// **不要自己推導** —— 它相對於建關節當下的姿勢、表達在 joint space、還要取反。
        /// 這是社群通用的 SetTargetRotationLocal。
        /// </summary>
        private static void SetTargetRotationLocal(ConfigurableJoint joint,
                                                   Quaternion targetLocalRotation,
                                                   Quaternion startLocalRotation)
        {
            var right = joint.axis;
            var forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            var up = Vector3.Cross(forward, right).normalized;

            var worldToJoint = Quaternion.LookRotation(forward, up);
            var jointToWorld = Quaternion.Inverse(worldToJoint);

            joint.targetRotation = jointToWorld * Quaternion.Inverse(targetLocalRotation)
                                 * startLocalRotation * worldToJoint;
        }

        // ================================================================ 骨盆

        /// <summary>
        /// 骨盆「站著的時候應該在哪、朝哪」。
        ///
        /// Armature 拆下來之後已經不在角色底下了，所以要用記下來的原始 local 姿勢，
        /// 從角色**目前**的位置與朝向重新算一次。角色被擊退、轉向了，目標就跟著走。
        /// </summary>
        private void HipsStandTarget(out Vector3 pos, out Quaternion rot)
        {
            var parent = _armatureParent != null ? _armatureParent : transform;
            var armatureToWorld = parent.localToWorldMatrix
                                * Matrix4x4.TRS(_armatureLocalPos, _armatureLocalRot, _armatureLocalScale);

            pos = armatureToWorld.MultiplyPoint3x4(_rootStandLocalPos);
            rot = parent.rotation * _armatureLocalRot * _rootStandPose;
        }

        /// <summary>
        /// **把骨盆扶正。第一版少了這個，所以「恢復怪怪的」。**
        ///
        /// 關節的 spring 只管「每一節相對上一節要怎麼擺」—— 它能把腿伸直，
        /// 但沒有任何東西讓整隻羊駝翻回正面。第一版的結果是：四肢伸直了，
        /// 身體還側躺在地上，然後最後 0.35 秒被混回動畫硬轉 90 度站起來。
        ///
        /// 這裡用一組跟階段同步的彈簧把骨盆往「站著的位置與朝向」拉：
        ///   癱軟時 0 —— 不出力，讓它摔
        ///   掙扎時 ~60% —— 撐得起來一點，但重力讓它下垂（g / k 的量），看起來就是撐不直
        ///   站回時 100% —— 收斂到站姿，混回動畫前幾乎已經是站好的
        ///
        /// 阻尼取臨界值（2√k），拉回去不會彈過頭再掉下來。
        /// 一律用 ForceMode.Acceleration：跟骨盆的質量無關，之後在 profile 裡調質量
        /// 不會連帶改變扶正的手感。
        /// </summary>
        private void DriveHips()
        {
            if (_rootBody == null || _rootBody.isKinematic) return;

            float s = SpringScale01();
            if (s <= 0f) return;

            HipsStandTarget(out var targetPos, out var targetRot);

            float k = _profile.hipsSpring * s;
            float d = 2f * Mathf.Sqrt(k);

            var acc = (targetPos - _rootBody.position) * k - _rootBody.linearVelocity * d;
            _rootBody.AddForce(acc, ForceMode.Acceleration);

            var delta = targetRot * Quaternion.Inverse(_rootBody.rotation);
            delta.ToAngleAxis(out float angle, out var axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x) || Mathf.Abs(angle) < 0.01f) return;

            var angAcc = axis.normalized * (angle * Mathf.Deg2Rad * k) - _rootBody.angularVelocity * d;
            _rootBody.AddTorque(angAcc, ForceMode.Acceleration);
        }

        /// <summary>
        /// 骨盆栓在膠囊上：超過 RagdollLeashRadius 就用力拉回。
        ///
        /// 在一個靠距離判定剃毛、交貨的遊戲裡，畫面上的羊駝不能跟碰撞位置分家。
        /// **單向：膠囊拉骨盆，骨盆從不推膠囊。**
        /// </summary>
        private void LeashHips()
        {
            if (_rootBody == null || _rootBody.isKinematic) return;

            var anchor = transform.position + Vector3.up * (GameTuning.AlpacaHeight * 0.5f);
            var offset = _rootBody.position - anchor;
            float dist = offset.magnitude;
            if (dist <= _profile.leashRadius) return;

            float over = dist - _profile.leashRadius;
            _rootBody.AddForce(-offset / dist * (over * 60f), ForceMode.Acceleration);
        }

        // ================================================================ 混回動畫

        /// <summary>
        /// Animator 先重新啟用，讓它把骨頭寫成動畫姿勢，再把 ragdoll 最後的姿勢往它上面混。
        ///
        /// 根骨在**世界座標**混（Armature 剛裝回去，local 值不連續），
        /// 其餘關節在 local 混（它們相對父骨頭，裝回去不受影響）。
        /// 兩者的目標都是「Animator 這一幀寫進去的值」，所以結束時是自然接上的。
        /// </summary>
        private void BlendBack()
        {
            if (_animator != null && !_animator.enabled) _animator.enabled = true;

            _blendBack -= Time.deltaTime;
            float t = 1f - Mathf.Clamp01(_blendBack / Mathf.Max(0.0001f, _profile.blendBackTime));
            t = t * t * (3f - 2f * t);   // smoothstep：頭尾都不會有速度突變

            if (_rootBone != null)
            {
                var animPos = _rootBone.position;
                var animRot = _rootBone.rotation;
                _rootBone.SetPositionAndRotation(Vector3.Lerp(_rootBlendFromPos, animPos, t),
                                                 Quaternion.Slerp(_rootBlendFromRot, animRot, t));
            }

            foreach (var j in _joints)
            {
                if (j.bone == null) continue;
                j.bone.localRotation = Quaternion.Slerp(j.blendFrom, j.bone.localRotation, t);
            }

            if (_blendBack <= 0f) _blendBack = 0f;
        }

        // ================================================================ 碰撞層

        /// <summary>
        /// 把 Ragdoll layer 跟除了 Default（地面與牆壁）以外的所有 layer 隔開。
        /// 在執行期設而不是改 ProjectSettings —— 那個矩陣不在建置流程裡，換台機器就沒了。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureCollisionMatrix()
        {
            int ragdoll = LayerMask.NameToLayer(LayerName);
            if (ragdoll < 0) return;

            for (int layer = 0; layer < 32; layer++)
            {
                bool keep = layer == 0 || layer == ragdoll;
                Physics.IgnoreLayerCollision(ragdoll, layer, !keep);
            }
        }
    }
}
