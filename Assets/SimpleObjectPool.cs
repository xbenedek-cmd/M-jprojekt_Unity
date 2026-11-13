using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Jednoduchý zásobník objektov: Get() = SetActive(true) z voľných (alebo nový cez factory), Release() = SetActive(false) späť do zásobníka.
/// Odolný voči zničeným objektom (zmena scény, Nová hra): zničené kusy sa pri Get() preskočia.
/// </summary>
public sealed class SimpleObjectPool
{
    private readonly Stack<GameObject> free = new Stack<GameObject>();
    private readonly Func<GameObject> factory;
    private static Transform root;
    private static PoolTimer timer;

    public SimpleObjectPool(Func<GameObject> factory, int prewarm = 0)
    {
        this.factory = factory;
        for (int i = 0; i < prewarm; i++) Release(Create());
    }

    private static void EnsureRoot()
    {
        if (root != null) return;
        GameObject go = new GameObject("PooledObjects");
        root = go.transform;
        timer = go.AddComponent<PoolTimer>();
    }

    private GameObject Create()
    {
        EnsureRoot();
        GameObject go = factory();
        go.transform.SetParent(root, false);
        return go;
    }

    /// <summary>Aktívny objekt (nový alebo recyklovaný); pozíciu a vzhľad nastaví volajúci.</summary>
    public GameObject Get()
    {
        GameObject go = null;
        while (free.Count > 0 && go == null) go = free.Pop();   // zničené kusy (== null) sa zahodia
        if (go == null) go = Create();
        go.SetActive(true);
        return go;
    }

    public void Release(GameObject go)
    {
        if (go == null) return;
        EnsureRoot();
        go.SetActive(false);
        go.transform.SetParent(root, false);   // aj značky pripnuté na cieľ sa odpoja skôr, než cieľ zmizne
        free.Push(go);
    }

    /// <summary>Vráti objekt do zásobníka po uplynutí času (jeden spoločný časovač, žiadny Destroy(go, t)).</summary>
    public void ReleaseAfter(GameObject go, float seconds)
    {
        EnsureRoot();
        timer.Add(this, go, Time.time + seconds);
    }

    // Jediný Update pre všetky objekty s časovým vrátením
    private sealed class PoolTimer : MonoBehaviour
    {
        private struct Entry { public SimpleObjectPool pool; public GameObject go; public float at; }
        private readonly List<Entry> entries = new List<Entry>(64);

        public void Add(SimpleObjectPool pool, GameObject go, float at)
            => entries.Add(new Entry { pool = pool, go = go, at = at });

        void Update()
        {
            float now = Time.time;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry e = entries[i];
                if (e.go != null && now < e.at) continue;

                int last = entries.Count - 1;
                entries[i] = entries[last];
                entries.RemoveAt(last);
                if (e.go != null) e.pool.Release(e.go);
            }
        }
    }
}
