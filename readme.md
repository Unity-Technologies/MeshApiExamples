# Unity 2020.1 Mesh API improvements examples

Unity 2020.1 adds `MeshData` APIs for C# Jobs/Burst compatible way of reading & writing Mesh data; see [overview document](https://docs.google.com/document/d/1QC7NV7JQcvibeelORJvsaTReTyszllOlxdfEsaVL2oA/edit).

This repository contains several small examples of that. Required Unity version is **2020.1 alpha 17** or later.

## Procedural Water/Wave Mesh

A simple example where a dense "water" surface mesh is updated every frame, based on positions on "wave source" objects.

![Water](/Images/Water.png?raw=true "Water")

`Assets/ProceduralWaterMesh` is the sample scene and code. Each vertex of the resulting mesh is completely independent of others, and
only depends on positions of the "wave source" objects. Using C# Jobs / Burst to compute all vertex positions in parallel brings
some nice speedups.

Numbers on 250x250 water mesh, with 10 wave source objects, on 2018 MacBookPro (Core i9 2.9GHz):

- Regular API: 66ms,
- Jobs+Burst: 3.9ms.

Same scene on Windows, AMD ThreadRipper 1950X 3.4GHz w/ 16 threads:

- Regular API: 83ms
- Jobs+Burst: 2.9ms



## Combine Many Input Meshes Into One

A more complex example, where for some hypothetical tooling there's a need to process geometry of many input Meshes, and produce
an output Mesh. Here, all input meshes are transformed into world space, and a giant output mesh is created that is the union of
all input meshes. This is very similar to how Static Batching in Unity works.

![Combine1](/Images/Combine1.png?raw=true "Combine 1")
![Combine2](/Images/Combine2.png?raw=true "Combine 2")

`Assets/CreateMeshFromAllSceneMeshes` is the sample scene and code. The script registers two menu items under `Mesh API Test`
top-level menu; both do the same thing just one uses "traditional" Mesh API and does everything on the main thread, whereas
the other uses 2020.1 new APIs to do it in C# Jobs with Burst.

Numbers for 11466 input objects, total 4.6M vertices, on 2018 MacBookPro (Core i9 2.9GHz):

- Regular API: 760ms (and 23k GC allocations totaling 640MB)
- Jobs+Burst: 60ms (0.3MB GC allocations)

Same scene on Windows, AMD ThreadRipper 1950X 3.4GHz w/ 16 threads:

- Regular API: 920ms
- Jobs+Burst: 70ms
