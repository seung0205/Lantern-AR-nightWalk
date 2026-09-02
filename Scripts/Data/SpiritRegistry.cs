using System.Collections.Generic;
using UnityEngine;

namespace Locackthon.Data
{
    [CreateAssetMenu(menuName = "Locackthon/Spirit Registry", fileName = "SpiritRegistry")]
    public class SpiritRegistry : ScriptableObject
    {
        [Tooltip("The master list of every spirit in the game. Add an entry here for each Spirit asset that should appear in the Collection Book.")]
        [SerializeField] private List<Spirit> spirits = new List<Spirit>();

        public IReadOnlyList<Spirit> All => spirits;
        public int Count => spirits.Count;

        public bool Contains(Spirit s) => s != null && spirits.Contains(s);

        public Spirit FindByName(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return null;
            for (int i = 0; i < spirits.Count; i++)
            {
                if (spirits[i] != null && spirits[i].name == assetName) return spirits[i];
            }
            return null;
        }
    }
}
