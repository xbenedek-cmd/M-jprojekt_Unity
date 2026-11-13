using System.Collections.Generic;
using UnityEngine;

public class StorageChest : MonoBehaviour
{
    [System.Serializable]
    public class MaterialEntry
    {
        public string id;
        public int amount;
    }

    [SerializeField] private List<MaterialEntry> materials = new List<MaterialEntry>();

    private readonly Dictionary<string, int> storage = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

    private void Awake()
    {
        RebuildDictionary();
    }

    public bool AddMaterial(string materialId, int amount)
    {
        if (string.IsNullOrWhiteSpace(materialId) || amount <= 0)
            return false;

        if (!storage.TryGetValue(materialId, out int current))
            current = 0;

        storage[materialId] = current + amount;
        SyncToList();
        return true;
    }

    public bool TryTakeMaterial(string materialId, int amount)
    {
        if (string.IsNullOrWhiteSpace(materialId) || amount <= 0)
            return false;

        if (!storage.TryGetValue(materialId, out int current) || current < amount)
            return false;

        int remaining = current - amount;
        if (remaining <= 0)
            storage.Remove(materialId);
        else
            storage[materialId] = remaining;

        SyncToList();
        return true;
    }

    public int GetAmount(string materialId)
    {
        if (string.IsNullOrWhiteSpace(materialId))
            return 0;

        return storage.TryGetValue(materialId, out int amount) ? amount : 0;
    }

    public Dictionary<string, int> Snapshot()
    {
        return new Dictionary<string, int>(storage, System.StringComparer.OrdinalIgnoreCase);
    }

    public List<MaterialEntry> GetEntries()
    {
        return new List<MaterialEntry>(materials);
    }

    [ContextMenu("Rebuild storage")]
    public void RebuildDictionary()
    {
        storage.Clear();

        for (int i = 0; i < materials.Count; i++)
        {
            var entry = materials[i];
            if (string.IsNullOrWhiteSpace(entry.id))
                continue;

            int amount = Mathf.Max(0, entry.amount);
            if (amount <= 0)
                continue;

            storage[entry.id] = amount;
        }

        SyncToList();
    }

    private void SyncToList()
    {
        materials.Clear();
        foreach (var pair in storage)
            materials.Add(new MaterialEntry { id = pair.Key, amount = pair.Value });
    }
}
