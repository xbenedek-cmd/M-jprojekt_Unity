using UnityEngine;

/// <summary>
/// Modely zjednodušené v Blendri (hráč, drevorubači) majú kostru pod uzlom "Armature" (mierka 1, kosti v metroch), kým klipy z Mixama
/// hľadajú "mixamorig:Hips/..." priamo pod koreňom modelu. Hips sa presunie pod koreň (svetová poloha ostáva, koža sedí) a obal sa zmaže.
/// Avatar z FBX by po presune nesedel na hierarchiu a Animator by kostiam vnútil východiskovú pózu, preto sa odpojí; klipy sa viažu podľa ciest.
/// </summary>
public static class RigFixup
{
    public static void FlattenArmatureWrapper(Transform root)
    {
        Transform armature = root.Find("Armature");
        if (armature == null) return;
        Transform hips = armature.Find("mixamorig:Hips");
        if (hips == null) return;

        hips.SetParent(root, true);
        Object.DestroyImmediate(armature.gameObject);

        Animator existing = root.GetComponent<Animator>();
        if (existing != null) existing.avatar = null;
    }
}
