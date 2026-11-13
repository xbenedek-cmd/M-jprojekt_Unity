using System.Collections.Generic;
using UnityEngine;

public enum DistrictWard
{
    Market,
    Temple,
    Guild,
    Slums,
    Military,
    Commons
}

[DisallowMultipleComponent]
public class DistrictOutfit : MonoBehaviour
{
    [Header("District")]
    public DistrictWard district = DistrictWard.Commons;

    [Header("Renderers")]
    public Renderer[] renderers;

    [Header("Palette")]
    public Color primary = new Color(0.72f, 0.72f, 0.72f);
    public Color secondary = new Color(0.20f, 0.22f, 0.28f);
    public Color accent = new Color(0.90f, 0.72f, 0.28f);

    void Reset()
    {
        CacheRenderers();
        ApplyDistrictColors();
    }

    void Awake()
    {
        CacheRenderers();
        ApplyDistrictColors();
    }

    [ContextMenu("Auto discover renderers")]
    public void CacheRenderers()
    {
        var list = new List<Renderer>(GetComponentsInChildren<Renderer>(true));
        renderers = list.ToArray();
    }

    public void ApplyDistrictColors()
    {
        if (renderers == null || renderers.Length == 0)
            CacheRenderers();

        if (renderers == null || renderers.Length == 0)
            return;

        var palette = GetPalette(district);

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;

            Material material = renderer.material;
            if (material == null) continue;

            // Keep a visible district tint while preserving the base material detail.
            material.color = palette.primary;
            renderer.material = material;
        }
    }

    public static OutfitPalette GetPalette(DistrictWard district)
    {
        switch (district)
        {
            case DistrictWard.Market:
                return new OutfitPalette(new Color(0.88f, 0.70f, 0.35f), new Color(0.25f, 0.18f, 0.13f), new Color(0.95f, 0.90f, 0.72f));
            case DistrictWard.Temple:
                return new OutfitPalette(new Color(0.72f, 0.82f, 0.90f), new Color(0.18f, 0.22f, 0.32f), new Color(0.94f, 0.88f, 0.70f));
            case DistrictWard.Guild:
                return new OutfitPalette(new Color(0.46f, 0.58f, 0.67f), new Color(0.12f, 0.14f, 0.16f), new Color(0.84f, 0.62f, 0.32f));
            case DistrictWard.Slums:
                return new OutfitPalette(new Color(0.53f, 0.41f, 0.28f), new Color(0.17f, 0.13f, 0.11f), new Color(0.78f, 0.79f, 0.35f));
            case DistrictWard.Military:
                return new OutfitPalette(new Color(0.31f, 0.38f, 0.45f), new Color(0.12f, 0.13f, 0.17f), new Color(0.82f, 0.83f, 0.84f));
            default:
                return new OutfitPalette(new Color(0.58f, 0.62f, 0.65f), new Color(0.20f, 0.22f, 0.25f), new Color(0.72f, 0.76f, 0.70f));
        }
    }

    public struct OutfitPalette
    {
        public Color primary;
        public Color secondary;
        public Color accent;

        public OutfitPalette(Color p, Color s, Color a)
        {
            primary = p;
            secondary = s;
            accent = a;
        }
    }
}
