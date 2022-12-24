using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

public class TerrainTile : MonoBehaviour {
    
    public int posx;
    public int posy;
    public int id;

    [NonSerialized]
    public Terrain TerrainComponent;
    [NonSerialized]
    public MeshFilter TreesComponent;
    [NonSerialized]
    public MeshFilter RocksComponent;
    [NonSerialized]
    public Material TerrainMaterial;
    [NonSerialized]
    public Material TreeMaterial;
    [NonSerialized]
    public bool HasTerrain = false;

    void Start() {
        GameObject terrain = new GameObject("Terrain");
        terrain.transform.parent = transform;
        terrain.transform.localPosition = Vector3.zero;

        TerrainComponent = terrain.AddComponent<Terrain>();
        TerrainComponent.terrainData = new TerrainData();
        TerrainComponent.materialTemplate = TerrainMaterial;
        TerrainComponent.heightmapPixelError = 3;

        TerrainCollider collider = terrain.AddComponent<TerrainCollider>();
        collider.terrainData = TerrainComponent.terrainData;

        GameObject trees = new GameObject("Trees");
        trees.transform.parent = transform;
        trees.transform.position = Vector3.zero;

        GameObject rocks = new GameObject("Rocks");
        rocks.transform.parent = transform;
        rocks.transform.position = Vector3.zero;

        MeshRenderer treeMeshRenderer = trees.AddComponent<MeshRenderer>();
        treeMeshRenderer.material = TreeMaterial;

        TreesComponent = trees.AddComponent<MeshFilter>();
        Mesh treeMesh = new Mesh();
        TreesComponent.mesh = treeMesh;
        treeMesh.indexFormat = IndexFormat.UInt32;
        treeMesh.MarkDynamic();

        MeshRenderer rockMeshRenderer = rocks.AddComponent<MeshRenderer>();
        rockMeshRenderer.material = TreeMaterial;

        RocksComponent = rocks.AddComponent<MeshFilter>();
        Mesh rockMesh = new Mesh();
        RocksComponent.mesh = rockMesh;
        rockMesh.indexFormat = IndexFormat.UInt32;
        rockMesh.MarkDynamic();

    }

    public void LoadTerrain(Texture2D texture2D)
	{
		TerrainComponent.terrainData.heightmapResolution = texture2D.width;
		Vector3 size = TerrainComponent.terrainData.size;
		size.y = TerrainManager.Instance.TileHeight;
		size.x = (float)TerrainManager.Instance.TileSize;
		size.z = (float)TerrainManager.Instance.TileSize;
		TerrainComponent.terrainData.size = size;
		int width = texture2D.width;
		float[,] array = new float[width, width];
		Color[] pixels = texture2D.GetPixels();
		
		LoadTerrainJob job = new LoadTerrainJob();
		job.InputData = pixels;
		job.OutputData = array;
		job.Width = width;
		job.TerrainData = TerrainComponent.terrainData;

		Thread thread = new Thread(new ThreadStart(job.Run));
		thread.Start();

        HasTerrain = true;
	}

    public void RecreateTreeMesh(Bounds bounds, Mesh template) {
        TreePos[] Data = TerrainManager.Instance.TreesData;

        //First we need to do our raycasts to assign height
        //We also use this to count the number of trees we need to place
        int numTrees = 0;

        for(int i = 0;i < Data.Length;i ++) {
            Vector3 pos = Data[i].pos;
            if(bounds.Contains(pos)) {
                Data[i].pos = TerrainManager.Instance.Project(pos.ToHorizontal());
                numTrees ++;
            }
        }
        
        CreateTreeMeshJob job = new CreateTreeMeshJob();
		job.Bounds = bounds;
        job.MeshTarget = TreesComponent.mesh;
        job.NumTrees = numTrees;
        job.OldVertices = template.vertices;
        job.OldUVs = template.uv;
        job.OldTriangles = template.triangles;
        job.OldNormals = template.normals;

		Thread thread = new Thread(new ThreadStart(job.Run));
		thread.Start();
    }

    public void RecreateRockMesh(Bounds bounds, Mesh template) {
        RockPos[] Data = TerrainManager.Instance.RocksData;

        //First we need to do our raycasts to assign height and angle
        //We also use this to count the number of rocks we need to place
        int numRocks = 0;

        for(int i = 0;i < Data.Length;i ++) {
            Vector3 pos = Data[i].pos;
            if(bounds.Contains(pos)) {
                RaycastHit? hit = TerrainManager.Instance.Raycast(pos.ToHorizontal());
                if(hit == null) {
                    continue;
                }
                Data[i].pos = hit.Value.point;
                Data[i].normal = hit.Value.normal;
                numRocks ++;
            }
        }
        
        CreateRockMeshJob job = new CreateRockMeshJob();
		job.Bounds = bounds;
        job.MeshTarget = RocksComponent.mesh;
        job.NumRocks = numRocks;
        job.OldVertices = template.vertices;
        job.OldUVs = template.uv;
        job.OldTriangles = template.triangles;
        job.OldNormals = template.normals;

		Thread thread = new Thread(new ThreadStart(job.Run));
		thread.Start();
    }

    public LODLevel GetLODLevel() {
        float sqrDist = (transform.position - Camera.main.transform.position).sqrMagnitude;
        if(sqrDist < TerrainManager.LOD1_sqr) {
            return LODLevel.LOD1;
        }
        if(sqrDist < TerrainManager.LOD2_sqr) {
            return LODLevel.LOD2;
        }
        if(sqrDist < TerrainManager.LOD3_sqr) {
            return LODLevel.LOD3;
        }
        return LODLevel.LOD4;
    }
}