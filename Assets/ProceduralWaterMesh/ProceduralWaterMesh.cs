using System.Linq;
using UnityEngine;

// Simple water wave procedural mesh based on http://www.konsfik.com/procedural-water-surface-made-in-unity3d/ - written by Kostas Sfikas, March 2017.
//
// "Classic API" with 250x250 vertex mesh, 4 wave sources, on 2018 MacBookPro (Core i9 2.9GHz):
// - 26.0ms, no GC allocations
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
	Vector3[] m_WaveSourcePositions;
	Vector3[] m_Vertices;
	Mesh m_Mesh;
	float m_LocalTime = 0.0f;

	void Awake()
	{
		m_Mesh = CreateMesh();
		m_WaveSources = transform.Cast<Transform>().Where(t => t.gameObject.activeInHierarchy).ToArray();
		m_WaveSourcePositions = new Vector3[m_WaveSources.Length];
	}

	void Update()
	{
		m_LocalTime += Time.deltaTime * 2.0f;
		UpdateWaveSourcePositions();
		if (!useJobs)
			UpdateWaterMeshClassicApi();
		else
			UpdateWaterMeshJobs();
	}

	void UpdateWaveSourcePositions()
	{
		for (var i = 0; i < m_WaveSources.Length; ++i)
		{
			m_WaveSourcePositions[i] = m_WaveSources[i].position;
		}
	}

	void UpdateWaterMeshClassicApi()
	{
		var verts = m_Vertices;
		for (int i = 0; i < verts.Length; i++)
			verts[i] = RecalculatePointY(verts[i]);
		m_Mesh.vertices = verts;
		m_Mesh.RecalculateNormals();
	}

	void UpdateWaterMeshJobs()
	{
		//@TODO
	}

	Vector3 RecalculatePointY(Vector3 p)
	{
		var y = 0.0f;
		for (var i = 0; i < m_WaveSourcePositions.Length; i++)
		{
			var p1 = new Vector2 (p.x, p.z);
			var p2 = new Vector2 (m_WaveSourcePositions[i].x, m_WaveSourcePositions[i].z);
			var dist = Vector2.Distance (p1,p2);
			y += Mathf.Sin (dist * 12.0f - m_LocalTime) / (dist*20+10);
		}
		p.y = y;
		return p;
	}

	Mesh CreateMesh()
	{
		Mesh newMesh = new Mesh();
		m_Vertices = new Vector3[surfaceWidthPoints * surfaceLengthPoints];
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
		
		newMesh.vertices = m_Vertices;
		newMesh.triangles = indices;
		newMesh.RecalculateNormals();
		GetComponent<MeshFilter>().mesh = newMesh;

		return newMesh;
	}

	float MapValue(float refValue, float refMin, float refMax, float targetMin, float targetMax)
	{
		/* This function converts the value of a variable (reference value) from one range (reference range) to another (target range)
		in this example it is used to convert the x and z value to the correct range, while creating the mesh, in the CreateMesh() function*/
		return targetMin + (refValue - refMin) * (targetMax - targetMin) / (refMax - refMin);
	}
}
