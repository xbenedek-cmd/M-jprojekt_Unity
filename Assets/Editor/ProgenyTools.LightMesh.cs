using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Odľahčenie ťažkých sietí bez Blenderu: Unity (Mesh LOD) zjednoduší kópiu siete, z vybranej úrovne sa
/// poskladá nová menšia sieť a zapíše sa do LightMeshTable. Pôvodné súbory modelov sa nemenia;
/// kostra, váhy kostí, UV a materiály ostávajú rovnaké, nič sa neprepeká.
/// </summary>
public static class ProgenyLightMesh
{
    const string OutputFolder = "Assets/Models/LightMeshes";
    const string TablePath = "Assets/Resources/" + LightMeshTable.ResourceName + ".asset";

    // model, cieľový počet trojuholníkov
    static readonly (string path, int triangles)[] Targets =
    {
        ("Assets/Models/Player/Hrac1.1/Hrac1.1.fbx", 90000),
        ("Assets/Models/Player/Sword/Meč.glb", 14000),
        ("Assets/Resources/Characters/Drevorubac1.fbx", 130000),
        ("Assets/Resources/Characters/Drevorubac2.fbx", 130000),
        ("Assets/Models/House/dom cyberpunk 1.fbx", 70000),
        ("Assets/Models/House/dom cyberpunk 2.fbx", 70000),
        // Jedlo je vo svete vo veľkom počte kusov a je malé
        ("Assets/Resources/Items/Chlieb.glb", 14000),
        ("Assets/Resources/Items/zelenina.glb", 14000)
    };

    [MenuItem("Progeny/Hráč/Odľahčiť siete (hráč, meč, drevorubači, domy, jedlo) bez zmeny pôvodných modelov", false, 610)]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(OutputFolder)) AssetDatabase.CreateFolder("Assets/Models", "LightMeshes");

        LightMeshTable table = AssetDatabase.LoadAssetAtPath<LightMeshTable>(TablePath);
        if (table == null)
        {
            table = ScriptableObject.CreateInstance<LightMeshTable>();
            AssetDatabase.CreateAsset(table, TablePath);
        }

        var report = new System.Text.StringBuilder("Odľahčené siete:\n");
        foreach ((string path, int triangles) in Targets)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(o is Mesh source)) continue;
                long sourceTriangles = TriangleCount(source);
                if (sourceTriangles <= triangles * 1.5f) continue;   // už je dosť ľahká

                // Sieť, ktorá už cieľu zodpovedá, sa neprepočítava (zjednodušenie milióna trojuholníkov trvá desiatky sekúnd)
                Mesh existing = table.Get(source);
                if (existing != null)
                {
                    long have = TriangleCount(existing);
                    if (have <= triangles && have > triangles / 2.2f) continue;
                }

                Mesh reduced = Reduce(source, triangles);
                if (reduced == null)
                {
                    report.AppendLine($"  {path} / {source.name}: zjednodušenie zlyhalo, ostáva pôvodná sieť");
                    continue;
                }

                string file = System.IO.Path.GetFileNameWithoutExtension(path).Replace(' ', '_');
                string assetPath = $"{OutputFolder}/{file}_{Sanitize(source.name)}_lahka.asset";
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(reduced, assetPath);
                table.Set(source, reduced);
                report.AppendLine($"  {source.name}: {sourceTriangles:N0} -> {TriangleCount(reduced):N0} trojuholníkov, {source.vertexCount:N0} -> {reduced.vertexCount:N0} vrcholov");
            }
        }

        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
        Debug.Log(report.ToString());
    }

    static string Sanitize(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.Length > 24 ? sb.ToString(0, 24) : sb.ToString();
    }

    static long TriangleCount(Mesh mesh)
    {
        long indices = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) indices += mesh.GetIndexCount(i);
        return indices / 3;
    }

    /// <summary>Kópia siete zjednodušená približne na daný počet trojuholníkov, len s vrcholmi, ktoré naozaj používa.</summary>
    static Mesh Reduce(Mesh source, int targetTriangles)
    {
        Mesh work = Object.Instantiate(source);
        try
        {
            MeshLodUtility.GenerateMeshLods(work, -1);
            int levels = work.lodCount;
            if (levels <= 1) return null;

            // Najjemnejšia úroveň, ktorá sa ešte zmestí do cieľa
            int level = levels - 1;
            for (int l = 0; l < levels; l++)
            {
                long tris = 0;
                for (int s = 0; s < work.subMeshCount; s++) tris += work.GetLod(s, l).indexCount / 3;
                if (tris <= targetTriangles) { level = l; break; }
            }

            using (Mesh.MeshDataArray array = Mesh.AcquireReadOnlyMeshData(work))
            {
                Mesh.MeshData data = array[0];
                int vertexCount = data.vertexCount;
                bool wide = data.indexFormat == IndexFormat.UInt32;
                NativeArray<uint> indices32 = wide ? data.GetIndexData<uint>() : default;
                NativeArray<ushort> indices16 = wide ? default : data.GetIndexData<ushort>();

                // Prečíslovanie: starý vrchol -> nový (len použité vrcholy)
                var remap = new int[vertexCount];
                for (int i = 0; i < vertexCount; i++) remap[i] = -1;
                var used = new List<int>(targetTriangles * 2);
                var newIndices = new List<int>[work.subMeshCount];

                for (int s = 0; s < work.subMeshCount; s++)
                {
                    MeshLodRange range = work.GetLod(s, level);
                    int baseVertex = work.GetSubMesh(s).baseVertex;
                    var list = new List<int>((int)range.indexCount);
                    for (uint i = 0; i < range.indexCount; i++)
                    {
                        int at = (int)(range.indexStart + i);
                        int old = (int)(wide ? indices32[at] : indices16[at]) + baseVertex;
                        if (remap[old] < 0)
                        {
                            remap[old] = used.Count;
                            used.Add(old);
                        }
                        list.Add(remap[old]);
                    }
                    newIndices[s] = list;
                }

                int newVertexCount = used.Count;
                int newIndexCount = 0;
                foreach (List<int> list in newIndices) newIndexCount += list.Count;
                bool newWide = newVertexCount > 65535;

                Mesh.MeshDataArray writable = Mesh.AllocateWritableMeshData(1);
                Mesh.MeshData output = writable[0];
                output.SetVertexBufferParams(newVertexCount, work.GetVertexAttributes());
                output.SetIndexBufferParams(newIndexCount, newWide ? IndexFormat.UInt32 : IndexFormat.UInt16);

                // Vrcholy sa kopírujú po bajtoch celé (poloha, normála, UV, váhy kostí...), takže sa nič neprepočítava
                for (int stream = 0; stream < data.vertexBufferCount; stream++)
                {
                    int stride = data.GetVertexBufferStride(stream);
                    if (stride == 0) continue;
                    NativeArray<byte> from = data.GetVertexData<byte>(stream);
                    NativeArray<byte> to = output.GetVertexData<byte>(stream);
                    for (int v = 0; v < newVertexCount; v++)
                        NativeArray<byte>.Copy(from, used[v] * stride, to, v * stride, stride);
                }

                int cursor = 0;
                if (newWide)
                {
                    NativeArray<uint> target = output.GetIndexData<uint>();
                    foreach (List<int> list in newIndices)
                        for (int i = 0; i < list.Count; i++) target[cursor++] = (uint)list[i];
                }
                else
                {
                    NativeArray<ushort> target = output.GetIndexData<ushort>();
                    foreach (List<int> list in newIndices)
                        for (int i = 0; i < list.Count; i++) target[cursor++] = (ushort)list[i];
                }

                output.subMeshCount = work.subMeshCount;
                int start = 0;
                for (int s = 0; s < work.subMeshCount; s++)
                {
                    output.SetSubMesh(s, new SubMeshDescriptor(start, newIndices[s].Count, MeshTopology.Triangles));
                    start += newIndices[s].Count;
                }

                var reduced = new Mesh { name = source.name + "_lahka" };
                Mesh.ApplyAndDisposeWritableMeshData(writable, reduced);
                reduced.bindposes = source.bindposes;   // rovnaká kostra ako pôvodná sieť
                reduced.RecalculateBounds();
                reduced.UploadMeshData(false);
                return reduced;
            }
        }
        finally
        {
            Object.DestroyImmediate(work);
        }
    }
}
