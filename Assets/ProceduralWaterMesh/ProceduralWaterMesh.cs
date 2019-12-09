using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Simple water wave procedural mesh based on http://www.konsfik.com/procedural-water-surface-made-in-unity3d/ - written by Kostas Sfikas, March 2017.
//
// "Classic API" with 200x200 vertex mesh, 4 wave sources, on 2018 MacBookPro (Core i9 2.9GHz):
// - 42.6ms, 0.7MB GC alloc
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class ProceduralWaterMesh : MonoBehaviour
{
	public float surfaceActualWidth = 10;
	public float surfaceActualLength = 10;
	public int surfaceWidthPoints = 100;
	public int surfaceLengthPoints = 100;
	List<Transform> waveSources;
	float localTime;
	float localTimeScale;

	void Awake ()
	{
		localTime = 0.0f;
		CreateMesh ();
		waveSources = transform.Cast<Transform>().Where(t => t.gameObject.activeInHierarchy).ToList();
	}

	// Update is called once per frame
	void Update () {
		localTime += Time.deltaTime * 2.0f;
		UpdateWaterMesh();
	}

	void UpdateWaterMesh()
	{
		Mesh waterMesh = GetComponent<MeshFilter>().mesh;
		var verts = waterMesh.vertices;
		for (int i = 0; i < verts.Length; i++)
		{
			float x = verts [i].x;
			float y = RecalculatePointY(verts[i]);
			float z = verts [i].z;
			Vector3 p = new Vector3(x,y,z);
			verts[i] = p;
		}
		waterMesh.vertices = verts;
		waterMesh.RecalculateNormals();
	}

	float RecalculatePointY(Vector3 point)
	{
		var y = 0.0f;
		for (var i = 0; i < waveSources.Count; i++)
		{
			var p1 = new Vector2 (point.x, point.z);
			var p2 = new Vector2 (waveSources[i].position.x, waveSources[i].position.z);
			var dist = Vector2.Distance (p1,p2);
			y += Mathf.Sin (dist * 12.0f - localTime) / (dist*20+10);
		}
		return y;
	}

	void CreateMesh()
	{
		Mesh newMesh = new Mesh();
		List<Vector3> verticeList = new List<Vector3>();
		List<int> triList = new List<int>();
		for (int i = 0; i < surfaceWidthPoints; i++)
		{		
			for (int j = 0; j < surfaceLengthPoints; j++)
			{
				float x = MapValue (i, 0.0f, surfaceWidthPoints, -surfaceActualWidth/2.0f, surfaceActualWidth/2.0f);
				float z = MapValue (j, 0.0f, surfaceLengthPoints, -surfaceActualLength/2.0f, surfaceActualLength/2.0f);
				verticeList.Add(new Vector3(x, 0f, z));
				//Skip if a new square on the plane hasn't been formed
				if (i == 0 || j == 0)
					continue;
				//Adds the index of the three vertices in order to make up each of the two tris
				triList.Add(surfaceLengthPoints * i +j); //Top right
				triList.Add(surfaceLengthPoints * i + j - 1); //Bottom right
				triList.Add(surfaceLengthPoints * (i - 1) + j - 1); //Bottom left - First triangle
				triList.Add(surfaceLengthPoints * (i - 1) + j - 1); //Bottom left 
				triList.Add(surfaceLengthPoints * (i- 1) + j); //Top left
				triList.Add(surfaceLengthPoints * i + j); //Top right - Second triangle
			}
		}
		//creating the mesh with the data generated above
		newMesh.vertices = verticeList.ToArray();	//pass vertices to mesh
		newMesh.triangles = triList.ToArray();		//pass triabgles to mesh
		newMesh.RecalculateNormals();				//recalculate mesh normals
		GetComponent<MeshFilter>().mesh = newMesh;	//pass the created mesh to the mesh filter
	}

	float MapValue(float refValue, float refMin, float refMax, float targetMin, float targetMax)
	{
		/* This function converts the value of a variable (reference value) from one range (reference range) to another (target range)
		in this example it is used to convert the x and z value to the correct range, while creating the mesh, in the CreateMesh() function*/
		return targetMin + (refValue - refMin) * (targetMax - targetMin) / (refMax - refMin);
	}
}
