using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

// Finds all MeshFilter components in the scene, and creates a new giant Mesh out of all of them (except the ones with
// "EditorOnly" tag). This is similar to what "static batching" would do -- take input meshes, transform their
// vertices/normals into world space, add to the output mesh.
//
// One approach is using C# Jobs, Burst and the new 2020.1 MeshData API. Another implementation further below is
// using "traditional" Mesh API.
public class CreateSceneMesh : MonoBehaviour
{
    static ProfilerMarker smp1 = new ProfilerMarker("Find Meshes");
    static ProfilerMarker smp2 = new ProfilerMarker("Prepare");
    static ProfilerMarker smp3 = new ProfilerMarker("Create Mesh");
    static ProfilerMarker smp4 = new ProfilerMarker("Cleanup");
    
    // ----------------------------------------------------------------------------------------------------------------
    // New Unity 2020.1 MeshData API
    //
    // Took 0.06sec for 11466 objects, total 4676490 verts (MacBookPro 2018, 2.9GHz i9)
    // Profiler: 59ms (GC alloc 275KB):
    // - Create Mesh 47ms (mostly waiting for jobs)
    // - Prepare 8ms (89KB GC alloc)
    // - FindMeshes 2.1ms (180KB GC alloc)
    [MenuItem("Mesh API Test/Create Mesh From Scene - New API %G")]
    public static void CreateMesh_MeshDataApi()
    {
        var sw = Stopwatch.StartNew();
        
        // Find all MeshFilter objects in the scene
        smp1.Begin();
        var meshFilters = FindObjectsOfType<MeshFilter>();
        smp1.End();

        smp2.Begin();
        var jobs = new ProcessMeshDataJob();
        jobs.vertexStart = new NativeArray<int>(meshFilters.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        jobs.triStart = new NativeArray<int>(meshFilters.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        jobs.xform = new NativeArray<float4x4>(meshFilters.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var inputMeshes = new List<Mesh>(meshFilters.Length);
        
        int vertexStart = 0;
        int triStart = 0;
        int meshCount = 0;
        for (var i = 0; i < meshFilters.Length; ++i)
        {
            var mf = meshFilters[i];
            var go = mf.gameObject;
            if (go.CompareTag("EditorOnly"))
            {
                DestroyImmediate(go);
                continue;
            }

            var mesh = mf.sharedMesh;
            var vcount = mesh.vertexCount;
            var icount = (int)mesh.GetIndexCount(0);
            inputMeshes.Add(mesh);
            jobs.vertexStart[meshCount] = vertexStart;
            jobs.triStart[meshCount] = triStart;
            jobs.xform[meshCount] = go.transform.localToWorldMatrix;
            vertexStart += vcount;
            triStart += icount;
            ++meshCount;
        }
        smp2.End();

        jobs.meshData = Mesh.AcquireReadOnlyMeshData(inputMeshes);
        var outputMeshData = Mesh.AllocateWritableMeshData(1);
        
        jobs.outputMesh = outputMeshData[0];
        jobs.outputMesh.SetIndexBufferParams(triStart, IndexFormat.UInt32);
        jobs.outputMesh.SetVertexBufferParams(vertexStart,
            new VertexAttributeDescriptor(VertexAttribute.Position),
            new VertexAttributeDescriptor(VertexAttribute.Normal, stream:1));
        jobs.bounds = new NativeArray<float3x2>(meshCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        for (var i = 0; i < meshCount; ++i)
            jobs.bounds[i] = new float3x2(new float3(Mathf.Infinity), new float3(Mathf.NegativeInfinity));
        
        var handle = jobs.Schedule(meshCount, 4);

        smp3.Begin();
        var newMesh = new Mesh();
        newMesh.name = "CombinedMesh";
        var sm = new SubMeshDescriptor(0, triStart, MeshTopology.Triangles);
        sm.firstVertex = 0;
        sm.vertexCount = vertexStart;
        
        handle.Complete();
        
        var bounds = new float3x2(new float3(Mathf.Infinity), new float3(Mathf.NegativeInfinity));
        for (var i = 0; i < meshCount; ++i)
        {
            var b = jobs.bounds[i];
            bounds.c0 = math.min(bounds.c0, b.c0);
            bounds.c1 = math.max(bounds.c1, b.c1);
        }

        sm.bounds = new Bounds((bounds.c0+bounds.c1)*0.5f, bounds.c1-bounds.c0);
        jobs.outputMesh.subMeshCount = 1;
        jobs.outputMesh.SetSubMesh(0, sm, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
        Mesh.ApplyAndDisposeWritableMeshData(outputMeshData, new[]{newMesh}, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
        newMesh.bounds = sm.bounds;
        smp3.End();
        smp4.Begin();
        jobs.meshData.Dispose();
        jobs.bounds.Dispose();
        
        smp4.End();
        
        var newGo = new GameObject("CombinedMesh", typeof(MeshFilter), typeof(MeshRenderer));
        newGo.tag = "EditorOnly";
        var newMf = newGo.GetComponent<MeshFilter>();
        var newMr = newGo.GetComponent<MeshRenderer>();
        newMr.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/CreateMeshFromAllSceneMeshes/MaterialForNewlyCreatedMesh.mat");
        newMf.sharedMesh = newMesh;
        //newMesh.RecalculateNormals(); // faster to do normal xform in the job
        
        var dur = sw.ElapsedMilliseconds;
        Debug.Log($"Took {dur/1000.0:F2}sec for {meshCount} objects, total {vertexStart} verts");
        
        Selection.activeObject = newGo;
    }

    [BurstCompile]
    struct ProcessMeshDataJob : IJobParallelFor
    {
        [ReadOnly] public Mesh.MeshDataArray meshData;
        public Mesh.MeshData outputMesh;
        [DeallocateOnJobCompletion] public NativeArray<int> vertexStart;
        [DeallocateOnJobCompletion] public NativeArray<int> triStart;
        [DeallocateOnJobCompletion] public NativeArray<float4x4> xform;
        public NativeArray<float3x2> bounds;

        [NativeDisableContainerSafetyRestriction] public NativeArray<float3> tempVertices;
        [NativeDisableContainerSafetyRestriction] public NativeArray<float3> tempNormals;

        public void Execute(int index)
        {
            var data = meshData[index]; 
            var vCount = data.vertexCount;
            var mat = xform[index];
            var vStart = vertexStart[index];

            if (!tempVertices.IsCreated || tempVertices.Length < vCount)
            {
                if (tempVertices.IsCreated) tempVertices.Dispose();
                tempVertices = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            }
            if (!tempNormals.IsCreated || tempNormals.Length < vCount)
            {
                if (tempNormals.IsCreated) tempNormals.Dispose();
                tempNormals = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            }
            data.GetVertices(tempVertices.Reinterpret<Vector3>());
            data.GetNormals(tempNormals.Reinterpret<Vector3>());
            
            var outputVerts = outputMesh.GetVertexData<Vector3>();
            var outputNormals = outputMesh.GetVertexData<Vector3>(stream:1);

            var b = bounds[index];
            for (var i = 0; i < vCount; ++i)
            {
                var pos = tempVertices[i];
                pos = math.mul(mat, new float4(pos, 1)).xyz;
                outputVerts[i+vStart] = pos;
                var nor = tempNormals[i];
                nor = math.normalize(math.mul(mat, new float4(nor, 0)).xyz);
                outputNormals[i+vStart] = nor;
                b.c0 = math.min(b.c0, pos);
                b.c1 = math.max(b.c1, pos);
            }
            bounds[index] = b;

            var tStart = triStart[index];
            var tCount = data.GetSubMesh(0).indexCount;
            var outputTris = outputMesh.GetIndexData<int>();
            if (data.indexFormat == IndexFormat.UInt16)
            {
                var tris = data.GetIndexData<ushort>();
                for (var i = 0; i < tCount; ++i)
                {
                    int idx = tris[i];
                    outputTris[i + tStart] = vStart + idx;
                }
            }
            else
            {
                var tris = data.GetIndexData<int>();
                for (var i = 0; i < tCount; ++i)
                {
                    int idx = tris[i];
                    outputTris[i + tStart] = vStart + idx;
                }
            }
        }
    }
        
    // ----------------------------------------------------------------------------------------------------------------
    // "Traditional" Mesh API
    //
    // Took 0.76sec for 11467 objects, total 4676490 verts (MacBookPro 2018, 2.9GHz i9)
    // Profiler: 703ms (640MB GC alloc):
    // - Prepare 340ms (23k GC allocs total 640MB),
    // - Recalculate Normals 214ms,
    // - Create Mesh 142ms,
    // - FindMeshes 2ms (180KB GC alloc)
    [MenuItem("Mesh API Test/Create Mesh From Scene - Classic Api %J")]
    public static void CreateMesh_ClassicApi()
    {
        var sw = Stopwatch.StartNew();
        smp1.Begin();
        var meshFilters = FindObjectsOfType<MeshFilter>();
        smp1.End();

        smp2.Begin();
        List<Vector3> allVerts = new List<Vector3>();
        //List<Vector3> allNormals = new List<Vector3>(); // faster to do RecalculateNormals than doing it manually
        List<int> allIndices = new List<int>();
        foreach (var mf in meshFilters)
        {
            var go = mf.gameObject;
            if (go.CompareTag("EditorOnly"))
            {
                DestroyImmediate(go);
                continue;
            }

            var tr = go.transform;
            var mesh = mf.sharedMesh;
            var verts = mesh.vertices;
            //var normals = mesh.normals;
            var tris = mesh.triangles;
            for (var i = 0; i < verts.Length; ++i)
            {
                var pos = verts[i];
                pos = tr.TransformPoint(pos);
                verts[i] = pos;
                //var nor = normals[i];
                //nor = tr.TransformDirection(nor).normalized;
                //normals[i] = nor;
            }
            var baseIdx = allVerts.Count;
            for (var i = 0; i < tris.Length; ++i)
                tris[i] = tris[i] + baseIdx;
            allVerts.AddRange(verts);
            //allNormals.AddRange(normals);
            allIndices.AddRange(tris);
        }
        smp2.End();

        smp3.Begin();
        var newMesh = new Mesh();
        newMesh.name = "CombinedMesh";
        newMesh.indexFormat = IndexFormat.UInt32;
        newMesh.SetVertices(allVerts);
        //newMesh.SetNormals(allNormals);
        newMesh.SetTriangles(allIndices, 0);
        smp3.End();
        
        var newGo = new GameObject("CombinedMesh", typeof(MeshFilter), typeof(MeshRenderer));
        newGo.tag = "EditorOnly";
        var newMf = newGo.GetComponent<MeshFilter>();
        var newMr = newGo.GetComponent<MeshRenderer>();
        newMr.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/CreateMeshFromAllSceneMeshes/MaterialForNewlyCreatedMesh.mat");
        newMf.sharedMesh = newMesh;
        newMesh.RecalculateNormals();
        
        var dur = sw.ElapsedMilliseconds;
        Debug.Log($"Took {dur/1000.0:F2}sec for {meshFilters.Length} objects, total {allVerts.Count} verts");
        
        Selection.activeObject = newGo;
    }
}