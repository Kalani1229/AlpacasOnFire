using UnityEngine;
using System;

/// <author> Copilot (k: i didn't review the code, it works, so it's fine) </author>

namespace AlpacasOnFire.Map
{
    [Serializable]
    public class RoadPrefabSet
    {
        public GameObject horizontal;
        public GameObject vertical;
        public GameObject corner;
        public GameObject tJunction;
        public GameObject crossroad;
        public GameObject deadEnd;
        public GameObject isolated;
    }
}