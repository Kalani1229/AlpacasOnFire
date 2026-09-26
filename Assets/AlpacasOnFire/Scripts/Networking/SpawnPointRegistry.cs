using System.Collections.Generic;
using UnityEngine;

namespace AlpacasOnFire.Networking
{
    /// <summary>
    /// 場景中的玩家出生點。由關卡編輯器放置 LevelElementType.PlayerSpawn 產生。
    /// </summary>
    public class SpawnPointRegistry : MonoBehaviour
    {
        private static readonly List<SpawnPointRegistry> Points = new();
        private static int _cursor;

        private void OnEnable()
        {
            if (!Points.Contains(this)) Points.Add(this);
        }

        private void OnDisable()
        {
            Points.Remove(this);
        }

        private static List<Vector3> _override;
        private static int _overrideCursor;

        /// <summary>
        /// 用程式指定的出生點取代場景裡的 PlayerSpawn 標記（地圖 B：程序生成的城市，
        /// 場景裡寫死的座標可能在建築裡）。傳 null 取消覆寫。
        /// </summary>
        public static void SetOverride(List<Vector3> points)
        {
            _override = points != null && points.Count > 0 ? new List<Vector3>(points) : null;
            _overrideCursor = 0;
        }

        /// <summary>輪流取用出生點；沒有任何出生點時回傳原點附近的預設位置。</summary>
        public static (Vector3 pos, Quaternion rot) Next()
        {
            if (_override != null)
            {
                var spot = _override[_overrideCursor % _override.Count];
                _overrideCursor++;
                return (spot + Vector3.up * 0.2f, Quaternion.identity);
            }

            if (Points.Count == 0)
                return (new Vector3(0f, 1f, 0f), Quaternion.identity);

            var p = Points[_cursor % Points.Count];
            _cursor++;
            // 多人時稍微錯開，避免疊在一起
            var offset = Quaternion.Euler(0f, _cursor * 90f, 0f) * Vector3.forward * 0.6f;
            return (p.transform.position + offset + Vector3.up * 0.2f, p.transform.rotation);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.45f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.9f, transform.forward * 1.2f);
        }
    }
}
