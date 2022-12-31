using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

public class TerrainTile : MonoBehaviour {
    
    public int PosX;
    public int PosY;
    public byte IndexX;
    public byte IndexY;
    public int id;
    [NonSerialized]
    public TerrainTileDirtyStates DirtyStates = TerrainTileDirtyStates.TERRAIN;

    [NonSerialized]
    public Terrain TerrainComponent;
    [NonSerialized]
    public MeshFilter TreesComponent;
    [NonSerialized]
    public MeshFilter RocksComponent;
    [NonSerialized]
    public Material TerrainMaterial;
    [NonSerialized]
    public Material ObjectMaterial;
    [NonSerialized]
    public RockPos[] LocalRockData;

    void Start() {
        GameObject terrain = new GameObject("Terrain");
        terrain.transform.parent = transform;
        terrain.transform.localPosition = Vector3.zero;

        TerrainComponent = terrain.AddComponent<Terrain>();
        TerrainComponent.terrainData = new TerrainData();
        TerrainComponent.materialTemplate = TerrainMaterial;
        TerrainComponent.heightmapPixelError = 5;

        TerrainCollider collider = terrain.AddComponent<TerrainCollider>();
        collider.terrainData = TerrainComponent.terrainData;

        GameObject trees = new GameObject("Trees");
        trees.transform.parent = transform;
        trees.transform.position = Vector3.zero;

        GameObject rocks = new GameObject("Rocks");
        rocks.transform.parent = transform;
        rocks.transform.position = Vector3.zero;

        MeshRenderer treeMeshRenderer = trees.AddComponent<MeshRenderer>();
        treeMeshRenderer.material = ObjectMaterial;

        TreesComponent = trees.AddComponent<MeshFilter>();
        Mesh treeMesh = new Mesh();
        TreesComponent.mesh = treeMesh;
        treeMesh.indexFormat = IndexFormat.UInt32;
        treeMesh.MarkDynamic();

        MeshRenderer rockMeshRenderer = rocks.AddComponent<MeshRenderer>();
        rockMeshRenderer.material = ObjectMaterial;

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
	}

    public void RecreateTreeMesh(Bounds bounds, TerrainManager.TreeTypeDescriptor[] descriptors) {
        GridArray<TreePos> Data = TerrainManager.Instance.TreesData;

        //First we need to do our raycasts to assign height
        //We also use this to count the number of trees we need to place
        int[] numTrees = new int[descriptors.Length];
        int totalTrees = 0;

        var enumerator = Data.GetEnumerator(IndexX, IndexY);
        while(enumerator.MoveNext()) {
            TreePos temp = enumerator.Current;
            temp.pos = TerrainManager.Instance.Project(enumerator.Current.pos.ToHorizontal());
            enumerator.CurrentMut = temp;
            numTrees[enumerator.Current.type]++;
            totalTrees++;
        }

        CreateTreeMeshJob job = new CreateTreeMeshJob();

		job.Bounds = bounds;
        job.PosX = IndexX;
        job.PosY = IndexY;
        job.MeshTarget = TreesComponent.mesh;
        job.Descriptors = new CreateTreeMeshJob.TreeTypeDescriptorForJob[descriptors.Length];
        for(int i = 0;i < descriptors.Length;i ++) {
            job.Descriptors[i] = new CreateTreeMeshJob.TreeTypeDescriptorForJob();
            job.Descriptors[i].NumTrees = numTrees[i];

            job.Descriptors[i].SnowMultiplier = descriptors[i].LODSnowMultiplier;

            job.Descriptors[i].OldNormals   = descriptors[i].LODMesh.normals;
            job.Descriptors[i].OldTriangles = descriptors[i].LODMesh.triangles;
            job.Descriptors[i].OldUVs       = descriptors[i].LODMesh.uv;
            job.Descriptors[i].OldVertices  = descriptors[i].LODMesh.vertices;
        }

        job.Initialize();
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
                numRocks ++;
                if(hit == null) {
                    continue;
                }
                Data[i].pos = hit.Value.point;
                Data[i].normal = hit.Value.normal;
            }
        }

        LocalRockData = new RockPos[numRocks];
        for(int i = 0, t = 0;i < Data.Length;i ++) {
            Vector3 pos = Data[i].pos;
            if(bounds.Contains(pos)) {
                LocalRockData[t] = Data[i];
                t++;
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

        job.Initialize();
		Thread thread = new Thread(new ThreadStart(job.Run));
		thread.Start();
    }

    private bool PrevLODLevel = false;
    
    void Update() {
        if(DirtyStates == 0) {
            bool currentLODLevel = GetWithinLOD();
            if(currentLODLevel != PrevLODLevel) {
                TerrainManager.Instance.TreeLODRenderersDirty = true;
                PrevLODLevel = currentLODLevel;
            }
        }
    }

    public void AdjustTreeRendering() {
        bool currentLODLevel = GetWithinLOD();
        TreesComponent.gameObject.SetActive(!currentLODLevel);
    }

    public bool GetWithinLOD() {
        Vector3 centerPos = transform.position;
        centerPos.x += TerrainManager.Instance.TileSize / 2;
        centerPos.y += TerrainManager.Instance.TileHeight / 2;
        centerPos.z += TerrainManager.Instance.TileSize / 2;
        float sqrDist = (centerPos - Camera.main.transform.position).sqrMagnitude;
        return sqrDist < TerrainManager.Instance.LOD_Distance * TerrainManager.Instance.LOD_Distance;
    }

    public enum TerrainTileDirtyStates : uint {
        TERRAIN = 1,
        TREES = 2,
        ROCKS = 4,
    }
}