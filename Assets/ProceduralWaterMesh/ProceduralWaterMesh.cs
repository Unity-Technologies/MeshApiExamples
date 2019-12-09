using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

// Simple water wave procedural mesh based on http://www.konsfik.com/procedural-water-surface-made-in-unity3d/ - written by Kostas Sfikas, March 2017.
//
// Tests on 250x250 vertex mesh, 10 wave sources, on 2018 MacBookPro (Core i9 2.9GHz):
// "Classic API":
// - 66.0ms, no GC allocations
//
// Jobs without Burst:
// - 11.2ms (9.6ms job, 1.4ms RecalcNormals, some unaccounted)
// Jobs with Burst:
// - 3.9ms (2.4ms job, 1.4ms RecalcNormals, some unaccounted)
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class ProceduralWaterMesh : MonoBehaviour
{
	public bool useJobs = false;
	public float surfaceActualWidth = 10;
	public float surfaceActualLength = 10;
	public int surfaceWidthPoints = 100;
	public int surfaceLengthPoints = 100;
	Transform[] m_WaveSources;
	NativeArray<Vector3> m_WaveSourcePositions;
	NativeArray<Vector3> m_Vertices;
	Mesh m_Mesh;
	float m_LocalTime = 0.0f;

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
	}

	void Update()
	{
		m_LocalTime += Time.deltaTime * 2.0f;
		UpdateWaveSourcePositions();
		var job = new WaveJob { vertices = m_Vertices, waveSourcePositions = m_WaveSourcePositions, time = m_LocalTime };
		if (!useJobs)
		{
			for (int i = 0; i < m_Vertices.Length; i++)
				job.Execute(i);
		}
		else
		{
			job.Schedule(m_Vertices.Length, 16).Complete();
		}
		m_Mesh.SetVertices(m_Vertices);
		m_Mesh.RecalculateNormals();
	}

	void UpdateWaveSourcePositions()
	{
		for (var i = 0; i < m_WaveSources.Length; ++i)
		{
			m_WaveSourcePositions[i] = m_WaveSources[i].position;
		}
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
}
