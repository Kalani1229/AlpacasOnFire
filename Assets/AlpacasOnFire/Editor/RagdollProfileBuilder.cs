using System.Collections.Generic;
using AlpacasOnFire.Player;
using UnityEditor;
using UnityEngine;

namespace AlpacasOnFire.EditorTools
{
    /// <summary>
    /// 掃描 `MD_Alpaca.fbx` 的骨架，產生（或更新）`RagdollProfile` 資產。
    ///
    /// **這支只負責「列出骨頭」，不負責「調好」。**
    /// 質量、碰撞體尺寸、角度上限、spring 倍率一定要看著模型調 ——
    /// 程式看不到骨頭有多長、往哪個方向長，猜出來的值一定是錯的。
    /// 這裡給的是一組能跑得起來的起點，不是最終值。
    ///
    /// **重跑不會蓋掉你調過的數值。** 已經在 profile 裡的骨頭一律原樣保留，
    /// 只補上新出現的骨頭。這是這個資產存在的全部理由 ——
    /// 玩家 prefab 每次建置都會重建，只有它留得住。
    /// </summary>
    public static class RagdollProfileBuilder
    {
        private const string ModelPath = "Assets/AlpacasOnFire/Models/MD_Alpaca.fbx";
        private const string ProfilePath = "Assets/AlpacasOnFire/Resources/RagdollProfile.asset";

        /// <summary>
        /// 預設要上關節的 12 根骨頭（根骨 spine.001 不算在內，它不上關節）。
        ///
        /// 對照 MD_Alpaca 的骨架：
        /// <code>
        /// Armature
        /// └─ spine.001                    ← 根骨／骨盆，栓繩的對象
        ///    ├─ spine.002                 ← 胸
        ///    │  ├─ chest.L ─ frontLeg.L ─ frontLeg.L.001
        ///    │  └─ chest.R ─ frontLeg.R ─ frontLeg.R.001
        ///    ├─ hip.L ─ backLeg.L ─ backLeg.L.001
        ///    ├─ hip.R ─ backLeg.R ─ backLeg.R.001
        ///    ├─ headBase                  ← 脖子（這副骨架沒有獨立的 neck）
        ///    │  ├─ face
        ///    │  └─ headTop                ← 頭
        ///    │     ├─ ears.L ─ ears.L.001
        ///    │     └─ ears.R ─ ears.R.001
        ///    └─ tail
        /// </code>
        ///
        /// **chest.L/R 與 hip.L/R 刻意跳過**：它們是肩／髖的球窩，
        /// 上了之後關節數多五成、更難調，而羊駝體型下那兩處的活動範圍本來就小。
        /// 跳過的骨頭仍然跟著動，只是被當成剛體的一部分帶著走 ——
        /// 腿的關節會直接連到最近的 ragdoll 祖先（spine.002 或 spine.001）。
        ///
        /// **ears / face 也跳過**：耳朵甩動是這個模型最好笑的部分，但它們很細、
        /// 質量很小，是最容易抖爆或穿模的。等主骨架調穩了再補。
        /// </summary>
        /// <summary>
        /// **全部是世界公尺。** 長度 0 = 自動（量到下一節骨頭）。
        ///
        /// 參考尺寸（從動畫檔讀出的骨頭間距 × 骨頭縮放 50）：
        /// 腿每節約 0.45 m、脖子約 0.75 m、胸到肩約 0.7 m。
        /// 半徑是目測羊駝的粗細給的起點。
        /// </summary>
        private static readonly RagdollBone[] Defaults =
        {
            // ---- 軀幹：重、硬，它決定整體的姿態 ----
            Bone("spine.002", mass: 2.5f, r: 0.20f, len: 0f,    low: -25f, high: 25f, swing: 20f, spring: 1.2f),

            // ---- 脖子與頭：軟一點，甩動的幅度大一些比較好笑 ----
            Bone("headBase",  mass: 1.2f, r: 0.08f, len: 0f,    low: -45f, high: 45f, swing: 35f, spring: 0.8f),
            Bone("headTop",   mass: 1.0f, r: 0.11f, len: 0.30f, low: -35f, high: 35f, swing: 25f, spring: 0.8f),

            // ---- 尾巴：最軟，純粹是裝飾。沒有子骨頭量不到長度，直接給 ----
            Bone("tail",      mass: 0.3f, r: 0.05f, len: 0.20f, low: -60f, high: 60f, swing: 45f, spring: 0.4f),

            // ---- 四條腿，各兩節。上節活動範圍大、下節（膝／踝）只往一個方向彎 ----
            Bone("frontLeg.L",     mass: 0.8f, r: 0.07f,  len: 0f, low: -60f, high: 60f, swing: 20f, spring: 1f),
            Bone("frontLeg.L.001", mass: 0.5f, r: 0.055f, len: 0f, low: -80f, high:  5f, swing: 10f, spring: 1f),
            Bone("frontLeg.R",     mass: 0.8f, r: 0.07f,  len: 0f, low: -60f, high: 60f, swing: 20f, spring: 1f),
            Bone("frontLeg.R.001", mass: 0.5f, r: 0.055f, len: 0f, low: -80f, high:  5f, swing: 10f, spring: 1f),
            Bone("backLeg.L",      mass: 0.9f, r: 0.07f,  len: 0f, low: -60f, high: 60f, swing: 20f, spring: 1f),
            Bone("backLeg.L.001",  mass: 0.5f, r: 0.055f, len: 0f, low:  -5f, high: 80f, swing: 10f, spring: 1f),
            Bone("backLeg.R",      mass: 0.9f, r: 0.07f,  len: 0f, low: -60f, high: 60f, swing: 20f, spring: 1f),
            Bone("backLeg.R.001",  mass: 0.5f, r: 0.055f, len: 0f, low:  -5f, high: 80f, swing: 10f, spring: 1f),
        };

        private static RagdollBone Bone(string name, float mass, float r, float len,
                                        float low, float high, float swing, float spring)
            => new()
            {
                boneName = name,
                enabled = true,
                mass = mass,
                radius = r,
                length = len,
                jointAxis = Vector3.right,
                limitLow = low,
                limitHigh = high,
                swingLimit = swing,
                springScale = spring,
            };

        [MenuItem("羊駝很忙/Ragdoll/1. 掃描羊駝骨架（建立-更新 RagdollProfile）", priority = 70)]
        public static void ScanSkeleton()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[Ragdoll] 找不到 {ModelPath}。沒有美術模型就沒有骨架，" +
                               "ragdoll 不會啟用（膠囊版維持原本的整隻傾倒）。");
                return;
            }

            // 模型裡實際有哪些骨頭。用它來驗證預設清單，而不是反過來 ——
            // 美術改了骨頭名稱時要當場吼出來，不要等到執行期才「找不到骨頭」。
            var present = new HashSet<string>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                present.Add(t.name);

            var profile = AssetDatabase.LoadAssetAtPath<RagdollProfile>(ProfilePath);
            bool created = false;

            if (profile == null)
            {
                EnsureFolder("Assets/AlpacasOnFire/Resources");
                profile = ScriptableObject.CreateInstance<RagdollProfile>();
                profile.unitsVersion = RagdollProfile.CurrentUnitsVersion;
                AssetDatabase.CreateAsset(profile, ProfilePath);
                created = true;
            }
            else if (profile.unitsVersion < RagdollProfile.CurrentUnitsVersion)
            {
                // 舊版的尺寸是骨頭本地單位，實際會被放大 50 倍 —— 那些值沒有一個是對的，
                // 保留它們只會繼承錯誤。整份清空，底下照新的預設重填。
                // （「不蓋掉手調數值」的規則只適用於同一個單位版本之內。）
                Debug.LogWarning("[Ragdoll] RagdollProfile 是舊的單位版本（骨頭本地單位），" +
                                 "已整份重設成公尺版的預設值。");
                profile.bones = new RagdollBone[0];
                profile.rootMass = 4f;
                profile.rootRadius = 0.2f;
                profile.rootLength = 0f;
                profile.unitsVersion = RagdollProfile.CurrentUnitsVersion;
            }

            if (!present.Contains(profile.rootBoneName))
            {
                Debug.LogWarning($"[Ragdoll] 模型裡沒有根骨 {profile.rootBoneName} —— " +
                                 "骨架可能改過名字，請確認 RagdollProfile.rootBoneName。");
            }

            // **已經存在的骨頭原樣保留**，只補新的。這是不蓋掉手調數值的關鍵。
            var existing = new List<RagdollBone>(profile.bones ?? new RagdollBone[0]);
            var known = new HashSet<string>();
            foreach (var b in existing)
                if (b != null && !string.IsNullOrEmpty(b.boneName)) known.Add(b.boneName);

            int added = 0;
            var missing = new List<string>();

            foreach (var def in Defaults)
            {
                if (!present.Contains(def.boneName))
                {
                    missing.Add(def.boneName);
                    continue;
                }
                if (known.Contains(def.boneName)) continue;

                existing.Add(Bone(def.boneName, def.mass, def.radius, def.length,
                                  def.limitLow, def.limitHigh, def.swingLimit, def.springScale));
                added++;
            }

            profile.bones = existing.ToArray();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            string verb = created ? "已建立" : "已更新";
            Debug.Log($"[Ragdoll] {verb} {ProfilePath}：" +
                      $"共 {profile.bones.Length} 根，本次新增 {added} 根。\n" +
                      "接下來請在那個資產上調質量／碰撞體尺寸／角度上限，" +
                      "然後重跑「1. 建置佔位資產」讓玩家 prefab 接上它。");

            if (missing.Count > 0)
                Debug.LogWarning("[Ragdoll] 模型裡找不到這些預設骨頭（已跳過）：\n - " +
                                 string.Join("\n - ", missing));

            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        /// <summary>
        /// 把骨架階層印到 Console。調數值前先看一眼哪根骨頭在哪，比在 Hierarchy 裡展開快。
        /// </summary>
        [MenuItem("羊駝很忙/Ragdoll/2. 印出羊駝骨架階層（診斷用）", priority = 71)]
        public static void DumpSkeleton()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) { Debug.LogError($"[Ragdoll] 找不到 {ModelPath}。"); return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Ragdoll] {ModelPath} 的骨架：");
            Dump(model.transform, 0, sb);
            Debug.Log(sb.ToString());
        }

        private static void Dump(Transform t, int depth, System.Text.StringBuilder sb)
        {
            sb.Append(' ', depth * 2).Append("└ ").AppendLine(t.name);
            for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1, sb);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
