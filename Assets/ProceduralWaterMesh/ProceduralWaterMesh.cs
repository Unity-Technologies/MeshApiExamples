using System.Linq;
using UnityEngine;

// Simple water wave procedural mesh based on http://www.konsfik.com/procedural-water-surface-made-in-unity3d/ - written by Kostas Sfikas, March 2017.
//
// "Classic API" with 250x250 vertex mesh, 4 wave sources, on 2018 MacBookPro (Core i9 2.9GHz):
// - 38.0ms, no GC allocations
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class ProceduralWaterMesh : MonoBehaviour
{
	public float surfaceActualWidth = 10;
	public float surfaceActualLength = 10;
	public int surfaceWidthPoints = 100;
	public int surfaceLengthPoints = 100;
	Transform[] waveSources;
	Vector3[] vertices;
	float localTime;
	float localTimeScale;

	void Awake ()
	{
		localTime = 0.0f;
		CreateMesh ();
		waveSources = transform.Cast<Transform>().Where(t => t.gameObject.activeInHierarchy).ToArray();
	}

	// Update is called once per frame
	void Update () {
		localTime += Time.deltaTime * 2.0f;
		UpdateWaterMesh();
	}

	void UpdateWaterMesh()
	{
		Mesh waterMesh = GetComponent<MeshFilter>().mesh;
		var verts = vertices;
		for (int i = 0; i < verts.Length; i++)
			verts[i] = RecalculatePointY(verts[i]);
		waterMesh.vertices = verts;
		waterMesh.RecalculateNormals();
	}

	Vector3 RecalculatePointY(Vector3 p)
	{
		var y = 0.0f;
		for (var i = 0; i < waveSources.Length; i++)
		{
			var p1 = new Vector2 (p.x, p.z);
			var p2 = new Vector2 (waveSources[i].position.x, waveSources[i].position.z);
			var dist = Vector2.Distance (p1,p2);
			y += Mathf.Sin (dist * 12.0f - localTime) / (dist*20+10);
		}
		p.y = y;
		return p;
	}

	void CreateMesh()
	{
		Mesh newMesh = new Mesh();
		vertices = new Vector3[surfaceWidthPoints * surfaceLengthPoints];
		var indices = new int[(surfaceWidthPoints - 1) * (surfaceLengthPoints - 1) * 6];
		var index = 0;
		for (var i = 0; i < surfaceWidthPoints; i++)
		{		
			for (var j = 0; j < surfaceLengthPoints; j++)
			{
				float x = MapValue (i, 0.0f, surfaceWidthPoints-1, -surfaceActualWidth/2.0f, surfaceActualWidth/2.0f);
				float z = MapValue (j, 0.0f, surfaceLengthPoints-1, -surfaceActualLength/2.0f, surfaceActualLength/2.0f);
				vertices[index++] = new Vector3(x, 0f, z);
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
		
		newMesh.vertices = vertices;
		newMesh.triangles = indices;
		newMesh.RecalculateNormals();
		GetComponent<MeshFilter>().mesh = newMesh;
	}

	float MapValue(float refValue, float refMin, float refMax, float targetMin, float targetMax)
	{
		/* This function converts the value of a variable (reference value) from one range (reference range) to another (target range)
		in this example it is used to convert the x and z value to the correct range, while creating the mesh, in the CreateMesh() function*/
		return targetMin + (refValue - refMin) * (targetMax - targetMin) / (refMax - refMin);
	}
}
