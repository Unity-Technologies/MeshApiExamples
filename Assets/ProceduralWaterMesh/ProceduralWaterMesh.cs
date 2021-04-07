using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

// Simple water wave procedural mesh based on https://www.konsfik.com/procedural-water-surface-made-in-unity3d/ - written by Kostas Sfikas, March 2017.
//
// Tests on 400x400 vertex mesh, 10 wave sources, on 2019 MacBookPro (Core i9 2.4GHz, 8c/16t):
// In editor, full frame time:
// - C# 1 thread: 154.9ms
// - Burst 1 thread: 40.2ms
// - Burst threads: 8.8ms
// - GPU compute: 8.3ms
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class ProceduralWaterMesh : MonoBehaviour
{
	public enum Mode
	{
		CPU,
		CPUBurst,
		CPUBurstThreaded,
		GPU
	}

	public Mode mode = Mode.CPUBurstThreaded;
	public float surfaceActualWidth = 10;
	public float surfaceActualLength = 10;
	public int surfaceWidthPoints = 100;
	public int surfaceLengthPoints = 100;
	public ComputeShader waveComputeShader;
	Transform[] m_WaveSources;
	Mesh m_Mesh;
	float m_LocalTime = 0.0f;

	NativeArray<Vector3> m_WaveSourcePositions;
	NativeArray<Vector3> m_Vertices;
	GraphicsBuffer m_GpuWaveSourcePositions;
	GraphicsBuffer m_GpuVertices;
	
	GUIContent[] m_UIOptions;

	void OnEnable()
	{
		m_Mesh = CreateMesh();
		m_WaveSources = transform.Cast<Transform>().Where(t => t.gameObject.activeInHierarchy).ToArray();
		m_WaveSourcePositions = new NativeArray<Vector3>(m_WaveSources.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
	}

	void OnDisable()
	{
		m_WaveSourcePositions.Dispose();
		m_Vertices.Dispose();
		CleanupComputeResources();
	}

	void CleanupComputeResources()
	{
		m_GpuWaveSourcePositions?.Dispose();
		m_GpuWaveSourcePositions = null;
		m_GpuVertices?.Dispose();
		m_GpuVertices = null;
	}

	public void Update()
	{
		m_LocalTime += Time.deltaTime * 2.0f;
		UpdateWaveSourcePositions();
		if (mode == Mode.GPU)
			UpdateWaveGpu();
		else
			UpdateWaveCpu();
	}

	void UpdateWaveCpu()
	{
		CleanupComputeResources();

		var job = new WaveJob { vertices = m_Vertices, waveSourcePositions = m_WaveSourcePositions, time = m_LocalTime };
		
		if (mode == Mode.CPU)
		{
			for (var i = 0; i < m_Vertices.Length; ++i)
				job.Execute(i);
		}
		else if (mode == Mode.CPUBurst)
		{
			job.Schedule(m_Vertices.Length, m_Vertices.Length).Complete();
		}
		else if (mode == Mode.CPUBurstThreaded)
		{
			job.Schedule(m_Vertices.Length, 16).Complete();
		}
		m_Mesh.SetVertices(m_Vertices);
		m_Mesh.RecalculateNormals();
	}

	void UpdateWaveGpu()
	{
		m_GpuWaveSourcePositions ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_WaveSources.Length, 12);
		m_GpuVertices ??= m_Mesh.GetVertexBuffer(0);
		
		m_GpuWaveSourcePositions.SetData(m_WaveSourcePositions);
		
		waveComputeShader.SetFloat("gTime", m_LocalTime);
		waveComputeShader.SetInt("gVertexCount", m_Mesh.vertexCount);
		waveComputeShader.SetInt("gWaveSourceCount", m_WaveSources.Length);
		waveComputeShader.SetInt("gVertexGridX", surfaceWidthPoints);
		waveComputeShader.SetInt("gVertexGridY", surfaceLengthPoints);
		// update vertex positions
		waveComputeShader.SetBuffer(0, "bufVertices", m_GpuVertices);
		waveComputeShader.SetBuffer(0, "bufWaveSourcePositions", m_GpuWaveSourcePositions);
		waveComputeShader.Dispatch(0, (m_Mesh.vertexCount+63)/63, 1, 1);
		// calculate normals
		waveComputeShader.SetBuffer(1, "bufVertices", m_GpuVertices);
		waveComputeShader.Dispatch(1, (m_Mesh.vertexCount+63)/63, 1, 1);
	}

	void UpdateWaveSourcePositions()
	{
		for (var i = 0; i < m_WaveSources.Length; ++i)
			m_WaveSourcePositions[i] = m_WaveSources[i].position;
	}

	[BurstCompile]
	struct WaveJob : IJobParallelFor
	{
		public NativeArray<Vector3> vertices;
		[ReadOnly] [NativeDisableParallelForRestriction] public NativeArray<Vector3> waveSourcePositions;
		public float time;

		public void Execute(int index)
		{
			var p = vertices[index];
			var y = 0.0f;
			for (var i = 0; i < waveSourcePositions.Length; i++)
			{
				var p1 = new Vector2 (p.x, p.z);
				var p2 = new Vector2 (waveSourcePositions[i].x, waveSourcePositions[i].z);
				var dist = Vector2.Distance (p1,p2);
				y += Mathf.Sin (dist * 12.0f - time) / (dist*20+10);
			}
			p.y = y;
			vertices[index] = p;
		}
	}

	Mesh CreateMesh()
	{
		Mesh newMesh = new Mesh();
		newMesh.indexFormat = IndexFormat.UInt32;
		newMesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
		m_Vertices = new NativeArray<Vector3>(surfaceWidthPoints * surfaceLengthPoints, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
		var indices = new int[(surfaceWidthPoints - 1) * (surfaceLengthPoints - 1) * 6];
		var index = 0;
		for (var i = 0; i < surfaceWidthPoints; i++)
		{		
			for (var j = 0; j < surfaceLengthPoints; j++)
			{
				float x = MapValue (i, 0.0f, surfaceWidthPoints-1, -surfaceActualWidth/2.0f, surfaceActualWidth/2.0f);
				float z = MapValue (j, 0.0f, surfaceLengthPoints-1, -surfaceActualLength/2.0f, surfaceActualLength/2.0f);
				m_Vertices[index++] = new Vector3(x, 0f, z);
			}
		}

		index = 0;
		for (var i = 0; i < surfaceWidthPoints-1; i++)
		{		
			for (var j = 0; j < surfaceLengthPoints-1; j++)
			{
				var baseIndex = i * surfaceLengthPoints + j;
				indices[index++] = baseIndex;
				indices[index++] = baseIndex + 1;
				indices[index++] = baseIndex + surfaceLengthPoints + 1;
				indices[index++] = baseIndex; 
				indices[index++] = baseIndex + surfaceLengthPoints + 1;
				indices[index++] = baseIndex + surfaceLengthPoints;
			}
		}
		
		newMesh.SetVertices(m_Vertices);
		newMesh.triangles = indices;
		newMesh.RecalculateNormals();
		GetComponent<MeshFilter>().mesh = newMesh;

		return newMesh;
	}

	static float MapValue(float refValue, float refMin, float refMax, float targetMin, float targetMax)
	{
		return targetMin + (refValue - refMin) * (targetMax - targetMin) / (refMax - refMin);
	}
	
	public void OnGUI()
	{
		m_UIOptions ??= new[]
		{
			new GUIContent("C# 1 thread"),
			new GUIContent("Burst 1 thread"),
			new GUIContent("Burst threaded"),
			new GUIContent("GPU compute"),
		};
		GUI.matrix = Matrix4x4.Scale(Vector3.one * 2);
		GUILayout.BeginArea(new Rect(5,25,420,80), "Options", GUI.skin.window);
		mode = (Mode)GUILayout.Toolbar((int)mode, m_UIOptions);
		GUILayout.Label($"Water: {surfaceWidthPoints}x{surfaceLengthPoints}, {m_WaveSources.Length} wave sources");
		GUILayout.EndArea();
	}	
}
