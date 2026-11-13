using System;
using System.Collections.Generic;
using UnityEngine;

public enum EquipSlot { None, Head, Top, Bottom, Boots }

/// <summary>
/// Oblečenie hráča: 4 časti (hlava, vrch, spodok, topánky). Oblečené kúsky dávajú bonusy cez rovnaké štáty ako strom schopností (SkillTree.Sum),
/// Filtračná maska na hlave sa pri pobyte v popolovej búrke opotrebúva a po čase sa rozpadne. Stav sa ukladá do PlayerPrefs ako inventár.
/// </summary>
public static class Equipment
{
    public const int SlotCount = 4;
    private const string SaveKey = "POA.Equip";

    private static readonly string[] ids = new string[SlotCount];
    private static readonly float[] wear = new float[SlotCount];     // zostávajúca výdrž; 0 pri predmetoch bez opotrebenia
    private static readonly Dictionary<string, float> spareWear = new Dictionary<string, float>();   // výdrž vyzutých predmetov (nedá sa „obnoviť“ prezlečením)
    private static bool loaded;

    [Serializable]
    class SaveData { public string[] ids; public float[] wear; public string[] spareIds; public float[] spareWear; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        Array.Clear(ids, 0, ids.Length);
        Array.Clear(wear, 0, wear.Length);
        spareWear.Clear();
        loaded = false;
    }

    static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        if (!PlayerPrefs.HasKey(SaveKey)) return;

        SaveData d = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey));
        if (d == null || d.ids == null) return;
        for (int i = 0; i < SlotCount && i < d.ids.Length; i++)
        {
            ItemDef def = ItemCatalog.Get(d.ids[i]);
            if (def == null || (int)def.slot != i + 1) continue;
            ids[i] = d.ids[i];
            wear[i] = d.wear != null && i < d.wear.Length ? d.wear[i] : def.durability;
        }
        if (d.spareIds != null && d.spareWear != null)
            for (int i = 0; i < d.spareIds.Length && i < d.spareWear.Length; i++) spareWear[d.spareIds[i]] = d.spareWear[i];
    }

    static void Save()
    {
        var d = new SaveData { ids = (string[])ids.Clone(), wear = (float[])wear.Clone() };
        d.spareIds = new string[spareWear.Count];
        d.spareWear = new float[spareWear.Count];
        int n = 0;
        foreach (KeyValuePair<string, float> kv in spareWear) { d.spareIds[n] = kv.Key; d.spareWear[n++] = kv.Value; }
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(d));
    }

    /// <summary>Nová hra: všetko oblečenie sa vyzlečie a zmaže.</summary>
    public static void Clear()
    {
        Array.Clear(ids, 0, ids.Length);
        Array.Clear(wear, 0, wear.Length);
        spareWear.Clear();
        loaded = true;
        PlayerPrefs.DeleteKey(SaveKey);
    }

    public static string IdIn(EquipSlot slot) { EnsureLoaded(); return slot == EquipSlot.None ? null : ids[(int)slot - 1]; }
    public static ItemDef DefIn(EquipSlot slot) => ItemCatalog.Get(IdIn(slot));
    public static bool IsWearing(string itemId) { EnsureLoaded(); foreach (string id in ids) if (id == itemId) return true; return false; }

    /// <summary>Zostávajúca výdrž v slote (0–1), alebo 1 pri predmetoch bez opotrebenia.</summary>
    public static float Condition01(EquipSlot slot)
    {
        ItemDef def = DefIn(slot);
        if (def == null || def.durability <= 0f) return 1f;
        return Mathf.Clamp01(wear[(int)slot - 1] / def.durability);
    }

    /// <summary>Oblečie predmet a vráti id predmetu, ktorý bol na tej časti predtým (alebo null).</summary>
    public static string Equip(string itemId)
    {
        EnsureLoaded();
        ItemDef def = ItemCatalog.Get(itemId);
        if (def == null || def.slot == EquipSlot.None) return null;

        int i = (int)def.slot - 1;
        string previous = ids[i];
        ItemDef previousDef = ItemCatalog.Get(previous);
        if (previousDef != null && previousDef.durability > 0f) spareWear[previous] = wear[i];

        ids[i] = itemId;
        wear[i] = def.durability > 0f && spareWear.TryGetValue(itemId, out float w) ? w : def.durability;
        spareWear.Remove(itemId);
        Save();
        return previous;
    }

    /// <summary>Vyzuje časť a vráti id predmetu (alebo null, ak bola prázdna).</summary>
    public static string Unequip(EquipSlot slot)
    {
        EnsureLoaded();
        int i = (int)slot - 1;
        string id = ids[i];
        if (id == null) return null;

        ItemDef def = ItemCatalog.Get(id);
        if (def != null && def.durability > 0f) spareWear[id] = wear[i];
        ids[i] = null;
        wear[i] = 0f;
        Save();
        return id;
    }

    /// <summary>Opotrebenie pri používaní (maska v búrke). Pri nule sa predmet rozpadne; vráti true, ak sa rozpadol.</summary>
    public static bool Wear(EquipSlot slot, float amount)
    {
        EnsureLoaded();
        int i = (int)slot - 1;
        ItemDef def = ItemCatalog.Get(ids[i]);
        if (def == null || def.durability <= 0f) return false;

        wear[i] -= amount;
        if (wear[i] > 0f) return false;

        GameHUD.Message($"{def.displayName} sa rozpadla.", 4f);
        ids[i] = null;
        wear[i] = 0f;
        Save();
        return true;
    }

    /// <summary>Súčet bonusov daného štátu zo všetkých oblečených kúskov.</summary>
    public static float Bonus(SkillStat stat)
    {
        EnsureLoaded();
        float sum = 0f;
        for (int i = 0; i < SlotCount; i++)
        {
            ItemDef def = ItemCatalog.Get(ids[i]);
            if (def == null || def.equipEffects == null) continue;
            foreach (SkillTree.Effect e in def.equipEffects) if (e.stat == stat) sum += e.amount;
        }
        return sum;
    }

    public static string SlotName(EquipSlot slot)
    {
        switch (slot)
        {
            case EquipSlot.Head: return "Hlava";
            case EquipSlot.Top: return "Vrch";
            case EquipSlot.Bottom: return "Spodok";
            case EquipSlot.Boots: return "Topánky";
            default: return "";
        }
    }
}
