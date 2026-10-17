using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Builds scenery explicitly and initializes gameplay separately when Play starts.</summary>
public sealed class FarFieldBootstrap : MonoBehaviour
{
    private const int TerrainGridCells = 144;
    private const float TerrainHalfExtentMeters = 600f;
    private const int RoadSegments = 44;
    private const float RoadHalfWidthMeters = 3.8f;
    private const int StarCount = 320;
    private const float StarFieldRadiusMeters = 900f;
    private const float RemoteArrayRoadHalfWidthMeters = 3.6f;
    private const float MainDishRadiusMeters = 15.5f;
    private static readonly Vector2[] RemoteDishSiteCoordinates =
    {
        new Vector2(-285f, -200f),
        new Vector2(275f, -235f),
        new Vector2(300f, 245f),
        new Vector2(-290f, 255f)
    };
    private static readonly float[] RemoteDishRadiiMeters = { 13f, 14f, 12f, 15f };
    private static readonly string[] RemoteDishStationNames = { "Southwest Ridge", "Southeast Relay", "East Crown", "Northwest Crown" };
    private const float InitialPlayerHeightMeters = 0.08f;

    [SerializeField] private bool buildOnStart = true;

    [SerializeField] private FarFieldGeneratedWorld savedWorld;
    public FarFieldGeneratedWorld SavedWorld => savedWorld;
    private readonly System.Collections.Generic.List<Object> runtimeResources = new System.Collections.Generic.List<Object>();
#if UNITY_EDITOR
    private readonly System.Collections.Generic.List<Object> editorResources = new System.Collections.Generic.List<Object>();

    // Explicit build completion/failure only; never called by editor refresh callbacks.
    public void FinishEditorGeneration()
    {
        foreach (Object resource in editorResources)
            if (resource != null && !UnityEditor.EditorUtility.IsPersistent(resource)) DestroyImmediate(resource);
        editorResources.Clear();
    }
#endif

    private void TrackResource(Object resource)
    {
        if (Application.isPlaying) runtimeResources.Add(resource);
#if UNITY_EDITOR
        else editorResources.Add(resource);
#endif
    }

    private Transform generatedRoot;
    private bool worldBuilt;
    private Material terrainMaterial;
    private Material roadMaterial;
    private Material concreteMaterial;
    private Material concreteEdgeMaterial;
    private Material buildingMaterial;
    private Material roofMaterial;
    private Material metalMaterial;
    private Material darkMetalMaterial;
    private Material panelMaterial;
    private Material glassMaterial;
    private Material warningMaterial;
    private Material vegetationMaterial;
    private Material dishMaterial;
    private Material dishBackMaterial;
    private Material emissiveAmberMaterial;
    private Material emissiveCyanMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureBootstrapInLoadedScene()
    {
        if (FindAnyObjectByType<FarFieldBootstrap>() != null)
        {
            return;
        }

        GameObject bootstrapObject = new GameObject("FARFIELD Bootstrap");
        bootstrapObject.AddComponent<FarFieldBootstrap>();
    }

    private void Start()
    {
        if (buildOnStart)
        {
            BuildWorld();
        }
    }

    /// <summary>Reuses saved scenery (or builds a fallback) and starts gameplay once, in Play mode only.</summary>
    public void BuildWorld()
    {
        if (!Application.isPlaying || worldBuilt) return;
        worldBuilt = true;
        if (savedWorld == null) savedWorld = CreateScenery();
        generatedRoot = savedWorld.transform;
        DisableStarterCamerasAndDirectionalLights();
        BuildPlayerAndSky();
    }

    // Called only by an explicit editor command or the runtime fallback.
    public FarFieldGeneratedWorld CreateScenery()
    {
        GameObject rootObject = new GameObject("FARFIELD Generated World");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rootObject, gameObject.scene);
        generatedRoot = rootObject.transform;
        // Keep metre-based geometry independent of the bootstrap's transform/scale.
        FarFieldGeneratedWorld world = rootObject.AddComponent<FarFieldGeneratedWorld>();
        world.Owner = this;
        try
        {
            CreateMaterials();
            BuildTerrainAndServiceRoad();
            BuildFacilityPadsAndMarkings();
            BuildControlRoom();
            BuildAuxiliaryStructures();
            BuildFacilityOperationsYard();
            DishController dishController = BuildRadioDish(new Vector3(5f, TerrainHeight(5f, 14f), 14f), MainDishRadiusMeters, "Primary Array", 190f, 31f, true);
            BuildRemoteDishArrays();
            BuildConsole(dishController);
            BuildExteriorLighting();
            return world;
        }
        catch
        {
            if (Application.isPlaying) Destroy(rootObject);
            else DestroyImmediate(rootObject);
#if UNITY_EDITOR
            if (!Application.isPlaying) FinishEditorGeneration();
#endif
            throw;
        }
    }

    public void SetSavedWorld(FarFieldGeneratedWorld world) => savedWorld = world;

    private void OnDestroy()
    {
        foreach (Object resource in runtimeResources)
            if (resource != null) Destroy(resource);
    }

    private void DisableStarterCamerasAndDirectionalLights()
    {
        Camera[] sceneCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < sceneCameras.Length; i++)
        {
            Camera sceneCamera = sceneCameras[i];
            if (sceneCamera == null)
            {
                continue;
            }

            AudioListener listener = sceneCamera.GetComponent<AudioListener>();
            if (listener != null)
            {
                listener.enabled = false;
            }
            sceneCamera.enabled = false;
        }

        Light[] sceneLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < sceneLights.Length; i++)
        {
            Light sceneLight = sceneLights[i];
            if (sceneLight != null && sceneLight.type == LightType.Directional)
            {
                sceneLight.enabled = false;
            }
        }
    }

    private void CreateMaterials()
    {
        terrainMaterial = CreateLitMaterial("FF Terrain Sage", new Color(0.19f, 0.25f, 0.22f), 0f, 0.08f);
        roadMaterial = CreateLitMaterial("FF Service Road", new Color(0.12f, 0.15f, 0.16f), 0f, 0.16f);
        concreteMaterial = CreateLitMaterial("FF Warm Concrete", new Color(0.36f, 0.39f, 0.38f), 0f, 0.24f);
        concreteEdgeMaterial = CreateLitMaterial("FF Concrete Edge", new Color(0.23f, 0.28f, 0.29f), 0f, 0.22f);
        buildingMaterial = CreateLitMaterial("FF Control Room Enamel", new Color(0.075f, 0.12f, 0.16f), 0.24f, 0.45f);
        roofMaterial = CreateLitMaterial("FF Roof Graphite", new Color(0.12f, 0.16f, 0.18f), 0.36f, 0.38f);
        metalMaterial = CreateLitMaterial("FF Brushed Alloy", new Color(0.43f, 0.53f, 0.56f), 0.72f, 0.58f);
        darkMetalMaterial = CreateLitMaterial("FF Dark Steel", new Color(0.075f, 0.095f, 0.105f), 0.68f, 0.42f);
        panelMaterial = CreateLitMaterial("FF Instrument Panels", new Color(0.025f, 0.055f, 0.07f), 0.3f, 0.5f);
        glassMaterial = CreateLitMaterial("FF Luminous Glass", new Color(0.06f, 0.25f, 0.31f), 0.12f, 0.82f, new Color(0.02f, 0.24f, 0.32f));
        warningMaterial = CreateLitMaterial("FF Safety Ochre", new Color(0.9f, 0.45f, 0.12f), 0.08f, 0.35f);
        vegetationMaterial = CreateLitMaterial("FF Dry Brush", new Color(0.27f, 0.29f, 0.18f), 0f, 0.05f);
        dishMaterial = CreateLitMaterial("FF Reflector Ceramic", new Color(0.72f, 0.79f, 0.78f), 0.56f, 0.72f);
        dishBackMaterial = CreateLitMaterial("FF Reflector Rear", new Color(0.14f, 0.19f, 0.2f), 0.72f, 0.48f);
        emissiveAmberMaterial = CreateLitMaterial("FF Amber Lamp", new Color(1f, 0.47f, 0.12f), 0f, 0.3f, new Color(1f, 0.18f, 0.025f));
        emissiveCyanMaterial = CreateLitMaterial("FF Cyan Indicator", new Color(0.12f, 0.82f, 0.95f), 0f, 0.3f, new Color(0.02f, 0.55f, 0.9f));
    }

    private Material CreateLitMaterial(string materialName, Color baseColor, float metallic, float smoothness, Color emission = default)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader)
        {
            name = materialName
        };
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", baseColor);
        }
        else
        {
            material.color = baseColor;
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }
        if (emission.maxColorComponent > 0f && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
        }

        TrackResource(material);
        return material;
    }

    private void BuildTerrainAndServiceRoad()
    {
        Mesh terrainMesh = CreateTerrainMesh();
        GameObject terrain = CreateMeshObject("Open Plateau Terrain", generatedRoot, Vector3.zero, terrainMesh, terrainMaterial, true);
        terrain.isStatic = true;

        Mesh roadMesh = CreateServiceRoadMesh();
        GameObject road = CreateMeshObject("Curving Service Road", generatedRoot, Vector3.zero, roadMesh, roadMaterial, false);
        road.isStatic = true;
        BuildRemoteServiceRoads();

        for (int markerIndex = 0; markerIndex < 10; markerIndex++)
        {
            float z = -34f - markerIndex * 21f;
            float x = Mathf.Sin(markerIndex * 0.13f) * 6f;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 markerPosition = new Vector3(x + side * 4.35f, TerrainHeight(x + side * 4.35f, z) + 0.42f, z);
                CreatePrimitive("Road Edge Marker", PrimitiveType.Cylinder, generatedRoot, markerPosition, new Vector3(0.17f, 0.42f, 0.17f), warningMaterial, false);
                CreatePrimitive("Marker Lamp", PrimitiveType.Sphere, generatedRoot, markerPosition + Vector3.up * 0.42f, new Vector3(0.2f, 0.13f, 0.2f), emissiveAmberMaterial, false);
            }
        }

        for (int brushIndex = 0; brushIndex < 112; brushIndex++)
        {
            float angle = brushIndex * 2.39996f;
            float radius = 96f + (brushIndex % 11) * 29f;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            if ((Mathf.Abs(x) < 42f && z > -45f && z < 48f) || IsNearRemoteDishSite(x, z, 62f))
            {
                continue;
            }

            float groundHeight = TerrainHeight(x, z);
            float height = 0.7f + (brushIndex % 4) * 0.23f;
            float width = 0.5f + (brushIndex % 3) * 0.22f;
            CreatePrimitive("Wind-Shaped Plateau Brush", PrimitiveType.Capsule, generatedRoot, new Vector3(x, groundHeight + height * 0.5f, z), new Vector3(width, height * 0.5f, width), vegetationMaterial, false);
        }

        for (int rockIndex = 0; rockIndex < 58; rockIndex++)
        {
            float angle = rockIndex * 2.39996f + 0.45f;
            float radius = 130f + (rockIndex % 9) * 43f;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            if ((Mathf.Abs(x) < 56f && z > -52f && z < 56f) || IsNearRemoteDishSite(x, z, 69f))
            {
                continue;
            }

            float groundHeight = TerrainHeight(x, z);
            float scale = 1.2f + (rockIndex % 5) * 0.55f;
            GameObject outcrop = CreatePrimitive("Dark Basalt Outcrop", PrimitiveType.Sphere, generatedRoot, new Vector3(x, groundHeight + scale * 0.28f, z), new Vector3(scale * 1.6f, scale * 0.58f, scale), darkMetalMaterial, false);
            outcrop.transform.localRotation = Quaternion.Euler(rockIndex % 13 * 3f, rockIndex * 31f, rockIndex % 9 * 4f);
            outcrop.isStatic = true;
        }
    }

    private void BuildRemoteServiceRoads()
    {
        Vector2 start = new Vector2(0f, -24f);
        for (int siteIndex = 0; siteIndex < RemoteDishSiteCoordinates.Length; siteIndex++)
        {
            Vector2 destination = RemoteDishSiteCoordinates[siteIndex];
            float sideSign = siteIndex % 2 == 0 ? -1f : 1f;
            Vector2 controlOne = Vector2.Lerp(start, destination, 0.34f) + new Vector2(sideSign * 45f, -sideSign * 34f);
            Vector2 controlTwo = Vector2.Lerp(start, destination, 0.72f) + new Vector2(-sideSign * 27f, sideSign * 24f);
            Mesh roadMesh = CreateRemoteRoadMesh(start, controlOne, controlTwo, destination, 64);
            GameObject road = CreateMeshObject(RemoteDishStationNames[siteIndex] + " Access Road", generatedRoot, Vector3.zero, roadMesh, roadMaterial, false);
            road.isStatic = true;

            for (int markerIndex = 1; markerIndex <= 8; markerIndex++)
            {
                float t = markerIndex / 9f;
                Vector2 point = EvaluateRoadCurve(start, controlOne, controlTwo, destination, t);
                Vector2 tangent = (EvaluateRoadCurve(start, controlOne, controlTwo, destination, Mathf.Min(1f, t + 0.01f)) - EvaluateRoadCurve(start, controlOne, controlTwo, destination, Mathf.Max(0f, t - 0.01f))).normalized;
                Vector2 side = new Vector2(tangent.y, -tangent.x);
                for (int roadSide = -1; roadSide <= 1; roadSide += 2)
                {
                    float markerX = point.x + side.x * (RemoteArrayRoadHalfWidthMeters + 0.8f) * roadSide;
                    float markerZ = point.y + side.y * (RemoteArrayRoadHalfWidthMeters + 0.8f) * roadSide;
                    Vector3 markerPosition = new Vector3(markerX, TerrainHeight(markerX, markerZ) + 0.38f, markerZ);
                    CreatePrimitive("Array Route Marker", PrimitiveType.Cylinder, generatedRoot, markerPosition, new Vector3(0.13f, 0.38f, 0.13f), warningMaterial, false);
                    CreatePrimitive("Array Route Marker Lamp", PrimitiveType.Sphere, generatedRoot, markerPosition + Vector3.up * 0.38f, new Vector3(0.17f, 0.12f, 0.17f), emissiveCyanMaterial, false);
                }
            }
        }
    }

    private Mesh CreateRemoteRoadMesh(Vector2 start, Vector2 controlOne, Vector2 controlTwo, Vector2 destination, int segmentCount)
    {
        Vector3[] vertices = new Vector3[(segmentCount + 1) * 2];
        int[] triangles = new int[segmentCount * 6];
        for (int segmentIndex = 0; segmentIndex <= segmentCount; segmentIndex++)
        {
            float t = segmentIndex / (float)segmentCount;
            Vector2 point = EvaluateRoadCurve(start, controlOne, controlTwo, destination, t);
            Vector2 tangent = (EvaluateRoadCurve(start, controlOne, controlTwo, destination, Mathf.Min(1f, t + 0.005f)) - EvaluateRoadCurve(start, controlOne, controlTwo, destination, Mathf.Max(0f, t - 0.005f))).normalized;
            Vector2 side = new Vector2(tangent.y, -tangent.x) * (RemoteArrayRoadHalfWidthMeters * 0.5f);
            float leftX = point.x - side.x;
            float leftZ = point.y - side.y;
            float rightX = point.x + side.x;
            float rightZ = point.y + side.y;
            int vertexIndex = segmentIndex * 2;
            vertices[vertexIndex] = new Vector3(leftX, TerrainHeight(leftX, leftZ) + 0.12f, leftZ);
            vertices[vertexIndex + 1] = new Vector3(rightX, TerrainHeight(rightX, rightZ) + 0.12f, rightZ);
        }

        int triangleIndex = 0;
        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            int leftNear = segmentIndex * 2;
            int rightNear = leftNear + 1;
            int leftFar = leftNear + 2;
            int rightFar = leftNear + 3;
            triangles[triangleIndex++] = leftNear;
            triangles[triangleIndex++] = leftFar;
            triangles[triangleIndex++] = rightNear;
            triangles[triangleIndex++] = rightNear;
            triangles[triangleIndex++] = leftFar;
            triangles[triangleIndex++] = rightFar;
        }

        Mesh mesh = new Mesh { name = "FARFIELD Remote Array Road Mesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector2 EvaluateRoadCurve(Vector2 start, Vector2 controlOne, Vector2 controlTwo, Vector2 destination, float t)
    {
        float inverseT = 1f - t;
        return inverseT * inverseT * inverseT * start
            + 3f * inverseT * inverseT * t * controlOne
            + 3f * inverseT * t * t * controlTwo
            + t * t * t * destination;
    }

    private static bool IsNearRemoteDishSite(float x, float z, float radius)
    {
        Vector2 point = new Vector2(x, z);
        for (int siteIndex = 0; siteIndex < RemoteDishSiteCoordinates.Length; siteIndex++)
        {
            if (Vector2.Distance(point, RemoteDishSiteCoordinates[siteIndex]) < radius)
            {
                return true;
            }
        }

        return false;
    }

    private Mesh CreateTerrainMesh()
    {
        int verticesPerSide = TerrainGridCells + 1;
        Vector3[] vertices = new Vector3[verticesPerSide * verticesPerSide];
        int[] triangles = new int[TerrainGridCells * TerrainGridCells * 6];
        float step = (TerrainHalfExtentMeters * 2f) / TerrainGridCells;
        int triangleIndex = 0;

        for (int zIndex = 0; zIndex < verticesPerSide; zIndex++)
        {
            for (int xIndex = 0; xIndex < verticesPerSide; xIndex++)
            {
                float x = -TerrainHalfExtentMeters + xIndex * step;
                float z = -TerrainHalfExtentMeters + zIndex * step;
                vertices[zIndex * verticesPerSide + xIndex] = new Vector3(x, TerrainHeight(x, z), z);
            }
        }

        for (int zIndex = 0; zIndex < TerrainGridCells; zIndex++)
        {
            for (int xIndex = 0; xIndex < TerrainGridCells; xIndex++)
            {
                int lowerLeft = zIndex * verticesPerSide + xIndex;
                int lowerRight = lowerLeft + 1;
                int upperLeft = lowerLeft + verticesPerSide;
                int upperRight = upperLeft + 1;
                triangles[triangleIndex++] = lowerLeft;
                triangles[triangleIndex++] = upperLeft;
                triangles[triangleIndex++] = lowerRight;
                triangles[triangleIndex++] = lowerRight;
                triangles[triangleIndex++] = upperLeft;
                triangles[triangleIndex++] = upperRight;
            }
        }

        Mesh mesh = new Mesh { name = "FARFIELD Procedural Terrain" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh CreateServiceRoadMesh()
    {
        Vector3[] vertices = new Vector3[(RoadSegments + 1) * 2];
        int[] triangles = new int[RoadSegments * 6];

        for (int segmentIndex = 0; segmentIndex <= RoadSegments; segmentIndex++)
        {
            float distance = segmentIndex * 6f;
            float z = -24f - distance;
            float centerX = Mathf.Sin(distance * 0.012f) * 12f;
            float leftX = centerX - RoadHalfWidthMeters;
            float rightX = centerX + RoadHalfWidthMeters;
            int vertexIndex = segmentIndex * 2;
            vertices[vertexIndex] = new Vector3(leftX, TerrainHeight(leftX, z) + 0.09f, z);
            vertices[vertexIndex + 1] = new Vector3(rightX, TerrainHeight(rightX, z) + 0.09f, z);
        }

        int triangleIndex = 0;
        for (int segmentIndex = 0; segmentIndex < RoadSegments; segmentIndex++)
        {
            int leftNear = segmentIndex * 2;
            int rightNear = leftNear + 1;
            int leftFar = leftNear + 2;
            int rightFar = leftNear + 3;
            triangles[triangleIndex++] = leftNear;
            triangles[triangleIndex++] = rightNear;
            triangles[triangleIndex++] = leftFar;
            triangles[triangleIndex++] = rightNear;
            triangles[triangleIndex++] = rightFar;
            triangles[triangleIndex++] = leftFar;
        }

        Mesh mesh = new Mesh { name = "FARFIELD Curving Service Road" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float TerrainHeight(float x, float z)
    {
        float rawHeight = BaseTerrainHeight(x, z);
        float flattening = 0f;
        float flattenedHeight = 0f;
        AccumulateTerrainFlattening(x, z, 0f, 0f, 88f, ref flattening, ref flattenedHeight);
        for (int siteIndex = 0; siteIndex < RemoteDishSiteCoordinates.Length; siteIndex++)
        {
            Vector2 site = RemoteDishSiteCoordinates[siteIndex];
            AccumulateTerrainFlattening(x, z, site.x, site.y, 48f, ref flattening, ref flattenedHeight);
        }

        if (flattening > 0f)
        {
            float blend = Mathf.Clamp01(flattening);
            return Mathf.Lerp(rawHeight, flattenedHeight / flattening, blend);
        }

        return rawHeight;
    }

    private static void AccumulateTerrainFlattening(float x, float z, float centerX, float centerZ, float radius, ref float weightTotal, ref float heightTotal)
    {
        float distance = Vector2.Distance(new Vector2(x, z), new Vector2(centerX, centerZ));
        float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius, radius + 42f, distance));
        if (weight <= 0f)
        {
            return;
        }

        weightTotal += weight;
        heightTotal += (centerX == 0f && centerZ == 0f ? 0f : BaseTerrainHeight(centerX, centerZ)) * weight;
    }

    private static float BaseTerrainHeight(float x, float z)
    {
        float broadHills = Mathf.Sin(x * 0.0048f + 0.8f) * Mathf.Cos(z * 0.0054f - 0.5f) * 9.5f;
        float rollingRidges = Mathf.Sin((x + z) * 0.009f + 0.6f) * 3.6f;
        float fineTerrain = Mathf.Sin(x * 0.016f - z * 0.012f) * Mathf.Cos(z * 0.018f + x * 0.006f) * 1.5f;
        float distantHills = GaussianHill(x, z, -420f, -80f, 28f, 105f)
            + GaussianHill(x, z, 425f, -100f, 34f, 120f)
            + GaussianHill(x, z, 30f, 420f, 30f, 120f)
            + GaussianHill(x, z, -30f, -465f, 23f, 108f)
            + GaussianHill(x, z, 430f, 420f, 20f, 95f);
        float edgeRise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(410f, 590f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)))) * 12f;
        return broadHills + rollingRidges + fineTerrain + distantHills + edgeRise;
    }

    private static float GaussianHill(float x, float z, float centerX, float centerZ, float height, float radius)
    {
        float distanceSquared = (x - centerX) * (x - centerX) + (z - centerZ) * (z - centerZ);
        return height * Mathf.Exp(-distanceSquared / (2f * radius * radius));
    }

    private void BuildFacilityPadsAndMarkings()
    {
        CreatePrimitive("Main Observatory Apron", PrimitiveType.Cube, generatedRoot, new Vector3(0f, -0.06f, 9f), new Vector3(58f, 0.42f, 44f), concreteMaterial, true);
        CreatePrimitive("Dish Work Pad", PrimitiveType.Cube, generatedRoot, new Vector3(5f, 0.2f, 14f), new Vector3(43f, 0.16f, 43f), concreteEdgeMaterial, true);
        CreatePrimitive("Dish Inner Pad", PrimitiveType.Cube, generatedRoot, new Vector3(5f, 0.3f, 14f), new Vector3(38f, 0.08f, 38f), concreteMaterial, true);
        CreatePrimitive("Control Room Foundation", PrimitiveType.Cube, generatedRoot, new Vector3(-23f, 0.08f, 3f), new Vector3(20f, 0.28f, 15f), concreteEdgeMaterial, true);
        CreatePrimitive("Control Room Floor", PrimitiveType.Cube, generatedRoot, new Vector3(-23f, 0.24f, 3f), new Vector3(18f, 0.12f, 13f), concreteMaterial, true);
        CreatePrimitive("Console Walkway", PrimitiveType.Cube, generatedRoot, new Vector3(-23f, 0.02f, -6.9f), new Vector3(3.4f, 0.13f, 4.6f), concreteMaterial, true);

        for (int stripeIndex = -2; stripeIndex <= 2; stripeIndex++)
        {
            float x = 5f + stripeIndex * 2.4f;
            CreatePrimitive("Pad Safety Stripe", PrimitiveType.Cube, generatedRoot, new Vector3(x, 0.36f, 3f), new Vector3(0.12f, 0.018f, 2.5f), warningMaterial, false);
        }

        CreatePrimitive("Pad Edge Inset", PrimitiveType.Cube, generatedRoot, new Vector3(0f, 0.16f, -10.2f), new Vector3(35f, 0.025f, 0.16f), emissiveCyanMaterial, false);
    }

    private void BuildControlRoom()
    {
        const float roomX = -23f;
        const float roomZ = 3f;
        const float roomWidth = 18f;
        const float roomDepth = 13f;
        const float wallHeight = 4.4f;
        const float wallCenterY = 2.45f;
        const float doorWidth = 2.4f;
        const float frontWallZ = roomZ - roomDepth * 0.5f + 0.15f;

        CreatePrimitive("Control Room Rear Wall", PrimitiveType.Cube, generatedRoot, new Vector3(roomX, wallCenterY, roomZ + roomDepth * 0.5f - 0.15f), new Vector3(roomWidth, wallHeight, 0.3f), buildingMaterial, true);
        CreatePrimitive("Control Room West Wall", PrimitiveType.Cube, generatedRoot, new Vector3(roomX - roomWidth * 0.5f + 0.15f, wallCenterY, roomZ), new Vector3(0.3f, wallHeight, roomDepth), buildingMaterial, true);
        CreatePrimitive("Control Room East Wall", PrimitiveType.Cube, generatedRoot, new Vector3(roomX + roomWidth * 0.5f - 0.15f, wallCenterY, roomZ), new Vector3(0.3f, wallHeight, roomDepth), buildingMaterial, true);

        float frontSegmentWidth = (roomWidth - doorWidth) * 0.5f;
        CreatePrimitive("Control Room Front West", PrimitiveType.Cube, generatedRoot, new Vector3(roomX - (doorWidth + frontSegmentWidth) * 0.5f, wallCenterY, frontWallZ), new Vector3(frontSegmentWidth, wallHeight, 0.3f), buildingMaterial, true);
        CreatePrimitive("Control Room Front East", PrimitiveType.Cube, generatedRoot, new Vector3(roomX + (doorWidth + frontSegmentWidth) * 0.5f, wallCenterY, frontWallZ), new Vector3(frontSegmentWidth, wallHeight, 0.3f), buildingMaterial, true);
        CreatePrimitive("Control Room Door Header", PrimitiveType.Cube, generatedRoot, new Vector3(roomX, 3.65f, frontWallZ), new Vector3(doorWidth, 2f, 0.3f), buildingMaterial, true);
        CreatePrimitive("Control Room Roof", PrimitiveType.Cube, generatedRoot, new Vector3(roomX, 4.86f, roomZ), new Vector3(19f, 0.42f, 14f), roofMaterial, true);
        CreatePrimitive("Roof Service Hatch", PrimitiveType.Cube, generatedRoot, new Vector3(roomX + 4.8f, 5.13f, roomZ + 3.7f), new Vector3(2.2f, 0.18f, 2.2f), metalMaterial, false);

        for (int side = -1; side <= 1; side += 2)
        {
            float windowX = roomX + side * 5.1f;
            CreatePrimitive("Exterior Window Frame", PrimitiveType.Cube, generatedRoot, new Vector3(windowX, 3.1f, frontWallZ - 0.18f), new Vector3(2.6f, 1.05f, 0.08f), darkMetalMaterial, false);
            CreatePrimitive("Exterior Window Glass", PrimitiveType.Cube, generatedRoot, new Vector3(windowX, 3.1f, frontWallZ - 0.23f), new Vector3(2.34f, 0.79f, 0.035f), glassMaterial, false);
            CreatePrimitive("Interior Window Glow", PrimitiveType.Cube, generatedRoot, new Vector3(windowX, 3.1f, frontWallZ + 0.19f), new Vector3(2.15f, 0.72f, 0.035f), glassMaterial, false);
        }

        CreatePrimitive("Entrance Canopy", PrimitiveType.Cube, generatedRoot, new Vector3(roomX, 3.2f, frontWallZ - 1.1f), new Vector3(4.2f, 0.22f, 2.1f), roofMaterial, true);
        CreatePrimitive("Entrance Sign", PrimitiveType.Cube, generatedRoot, new Vector3(roomX, 3.7f, frontWallZ - 2.18f), new Vector3(3.1f, 0.28f, 0.08f), panelMaterial, false);
        CreatePrimitive("Sign Indicator", PrimitiveType.Cube, generatedRoot, new Vector3(roomX, 3.71f, frontWallZ - 2.24f), new Vector3(2.5f, 0.035f, 0.025f), emissiveCyanMaterial, false);

        GameObject roomLightObject = new GameObject("Control Room Interior Light");
        roomLightObject.transform.SetParent(generatedRoot, false);
        roomLightObject.transform.localPosition = new Vector3(roomX, 3.9f, roomZ + 0.2f);
        Light roomLight = roomLightObject.AddComponent<Light>();
        roomLight.type = LightType.Point;
        roomLight.color = new Color(0.35f, 0.78f, 0.96f);
        roomLight.intensity = 2.6f;
        roomLight.range = 13f;
        roomLight.shadows = LightShadows.None;
    }

    private void BuildAuxiliaryStructures()
    {
        CreatePrimitive("Generator Foundation", PrimitiveType.Cube, generatedRoot, new Vector3(30f, 0.14f, -3f), new Vector3(12f, 0.3f, 9f), concreteEdgeMaterial, true);
        CreatePrimitive("Backup Generator Enclosure", PrimitiveType.Cube, generatedRoot, new Vector3(30f, 2.2f, -3f), new Vector3(9f, 4f, 6f), buildingMaterial, true);
        CreatePrimitive("Generator Roof", PrimitiveType.Cube, generatedRoot, new Vector3(30f, 4.27f, -3f), new Vector3(9.6f, 0.18f, 6.6f), roofMaterial, true);
        CreatePrimitive("Generator Door", PrimitiveType.Cube, generatedRoot, new Vector3(30f, 1.45f, -6.08f), new Vector3(2f, 2.6f, 0.08f), darkMetalMaterial, false);
        CreatePrimitive("Generator Status Lamp", PrimitiveType.Sphere, generatedRoot, new Vector3(31.35f, 2.8f, -6.15f), new Vector3(0.22f, 0.22f, 0.12f), emissiveAmberMaterial, false);

        for (int ventIndex = 0; ventIndex < 5; ventIndex++)
        {
            CreatePrimitive("Generator Vent", PrimitiveType.Cube, generatedRoot, new Vector3(26.8f + ventIndex * 1.15f, 3.1f, -6.08f), new Vector3(0.72f, 0.12f, 0.08f), metalMaterial, false);
        }

        for (int tankIndex = 0; tankIndex < 2; tankIndex++)
        {
            float x = -40f + tankIndex * 5.8f;
            float z = 17f;
            CreatePrimitive("Water Tank Footing", PrimitiveType.Cylinder, generatedRoot, new Vector3(x, 0.4f, z), new Vector3(2.8f, 0.22f, 2.8f), concreteEdgeMaterial, true);
            CreatePrimitive("Auxiliary Storage Tank", PrimitiveType.Cylinder, generatedRoot, new Vector3(x, 2.5f, z), new Vector3(2.1f, 2.1f, 2.1f), metalMaterial, true);
            CreatePrimitive("Tank Cap", PrimitiveType.Cylinder, generatedRoot, new Vector3(x, 4.66f, z), new Vector3(2.2f, 0.12f, 2.2f), darkMetalMaterial, false);
            CreatePrimitive("Tank Indicator", PrimitiveType.Cube, generatedRoot, new Vector3(x, 1.95f, z - 1.06f), new Vector3(0.16f, 0.8f, 0.07f), emissiveCyanMaterial, false);
        }

        CreatePrimitive("Instrument Cabinet Plinth", PrimitiveType.Cube, generatedRoot, new Vector3(18.5f, 0.55f, 26f), new Vector3(5.2f, 1.1f, 2.4f), darkMetalMaterial, true);
        for (int cabinetIndex = 0; cabinetIndex < 3; cabinetIndex++)
        {
            float x = 16.8f + cabinetIndex * 1.7f;
            CreatePrimitive("Outdoor Instrument Cabinet", PrimitiveType.Cube, generatedRoot, new Vector3(x, 1.8f, 26f), new Vector3(1.35f, 2.5f, 1.9f), buildingMaterial, true);
            CreatePrimitive("Cabinet Display", PrimitiveType.Cube, generatedRoot, new Vector3(x, 2.3f, 24.99f), new Vector3(0.78f, 0.5f, 0.055f), glassMaterial, false);
        }
    }

    private void BuildFacilityOperationsYard()
    {
        const float yardX = 41f;
        const float yardZ = -17f;
        CreatePrimitive("Maintenance Yard Apron", PrimitiveType.Cube, generatedRoot, new Vector3(yardX, 0.12f, yardZ), new Vector3(24f, 0.24f, 18f), concreteEdgeMaterial, true);
        CreatePrimitive("Maintenance Garage", PrimitiveType.Cube, generatedRoot, new Vector3(yardX, 2.1f, yardZ + 1.2f), new Vector3(17f, 3.8f, 10f), buildingMaterial, true);
        CreatePrimitive("Maintenance Garage Roof", PrimitiveType.Cube, generatedRoot, new Vector3(yardX, 4.12f, yardZ + 1.2f), new Vector3(17.8f, 0.22f, 10.8f), roofMaterial, true);
        CreatePrimitive("Garage Roll-Up Door", PrimitiveType.Cube, generatedRoot, new Vector3(yardX - 3.6f, 1.65f, yardZ - 3.86f), new Vector3(5.4f, 3.1f, 0.08f), darkMetalMaterial, false);
        CreatePrimitive("Garage Door Header Light", PrimitiveType.Cube, generatedRoot, new Vector3(yardX - 3.6f, 3.35f, yardZ - 3.94f), new Vector3(4.5f, 0.12f, 0.06f), emissiveAmberMaterial, false);
        for (int containerIndex = 0; containerIndex < 2; containerIndex++)
        {
            float x = yardX + 13f + containerIndex * 3.2f;
            CreatePrimitive("Field Logistics Container", PrimitiveType.Cube, generatedRoot, new Vector3(x, 1.25f, yardZ + 2f), new Vector3(5.6f, 2.5f, 7f), darkMetalMaterial, true);
            CreatePrimitive("Container Access Stripe", PrimitiveType.Cube, generatedRoot, new Vector3(x, 1.28f, yardZ - 1.55f), new Vector3(0.14f, 2.2f, 0.05f), warningMaterial, false);
        }

        CreateBeam("Raised Utility Conduit", generatedRoot, new Vector3(30f, 1.1f, -7f), new Vector3(34f, 1.1f, -12f), 0.12f, darkMetalMaterial);
        CreateBeam("Raised Utility Conduit", generatedRoot, new Vector3(34f, 1.1f, -12f), new Vector3(38f, 1.1f, -12f), 0.12f, darkMetalMaterial);
    }

    private void BuildRemoteDishArrays()
    {
        for (int siteIndex = 0; siteIndex < RemoteDishSiteCoordinates.Length; siteIndex++)
        {
            Vector2 coordinates = RemoteDishSiteCoordinates[siteIndex];
            Vector3 stationCenter = new Vector3(coordinates.x, TerrainHeight(coordinates.x, coordinates.y), coordinates.y);
            string stationName = RemoteDishStationNames[siteIndex];
            float dishRadius = RemoteDishRadiiMeters[siteIndex];
            BuildRemoteArrayFacility(stationCenter, stationName);
            DishController remoteDish = BuildRadioDish(stationCenter, dishRadius, stationName + " Radio Telescope", 35f + siteIndex * 83f, 24f + siteIndex * 5f, false);
            remoteDish.ScanOnStart = true;
        }
    }

    private void BuildRemoteArrayFacility(Vector3 center, string stationName)
    {
        CreatePrimitive(stationName + " Array Apron", PrimitiveType.Cube, generatedRoot, center + new Vector3(0f, 0.12f, 0f), new Vector3(66f, 0.24f, 62f), concreteEdgeMaterial, true);
        CreatePrimitive(stationName + " Inner Work Pad", PrimitiveType.Cube, generatedRoot, center + new Vector3(0f, 0.27f, 0f), new Vector3(58f, 0.08f, 54f), concreteMaterial, true);

        Vector3 shedCenter = center + new Vector3(25f, 2.1f, -11f);
        CreatePrimitive(stationName + " Receiver Hall", PrimitiveType.Cube, generatedRoot, shedCenter, new Vector3(15f, 3.8f, 9f), buildingMaterial, true);
        CreatePrimitive(stationName + " Receiver Hall Roof", PrimitiveType.Cube, generatedRoot, shedCenter + Vector3.up * 2.02f, new Vector3(15.8f, 0.22f, 9.8f), roofMaterial, true);
        CreatePrimitive(stationName + " Service Door", PrimitiveType.Cube, generatedRoot, center + new Vector3(25f, 1.35f, -15.56f), new Vector3(2.4f, 2.5f, 0.08f), darkMetalMaterial, false);
        CreatePrimitive(stationName + " Hall Window", PrimitiveType.Cube, generatedRoot, center + new Vector3(25f, 2.8f, -15.61f), new Vector3(4.4f, 0.55f, 0.06f), glassMaterial, false);

        for (int cabinetIndex = 0; cabinetIndex < 3; cabinetIndex++)
        {
            float x = center.x + 14f + cabinetIndex * 2.2f;
            float z = center.z - 21f;
            CreatePrimitive(stationName + " Relay Cabinet", PrimitiveType.Cube, generatedRoot, new Vector3(x, center.y + 1.6f, z), new Vector3(1.6f, 3.1f, 1.3f), darkMetalMaterial, false);
            CreatePrimitive(stationName + " Relay Indicator", PrimitiveType.Sphere, generatedRoot, new Vector3(x, center.y + 2.4f, z - 0.69f), new Vector3(0.18f, 0.18f, 0.12f), emissiveCyanMaterial, false);
        }

        for (int mastIndex = 0; mastIndex < 2; mastIndex++)
        {
            float x = center.x + (mastIndex == 0 ? -27f : 29f);
            float z = center.z + (mastIndex == 0 ? 22f : 20f);
            CreatePrimitive(stationName + " Telemetry Mast", PrimitiveType.Cylinder, generatedRoot, new Vector3(x, center.y + 5.6f, z), new Vector3(0.11f, 5.6f, 0.11f), metalMaterial, false);
            CreatePrimitive(stationName + " Mast Beacon", PrimitiveType.Sphere, generatedRoot, new Vector3(x, center.y + 11.3f, z), new Vector3(0.3f, 0.3f, 0.3f), emissiveAmberMaterial, false);
            CreatePrimitive(stationName + " Mast Footing", PrimitiveType.Cylinder, generatedRoot, new Vector3(x, center.y + 0.35f, z), new Vector3(0.9f, 0.35f, 0.9f), concreteEdgeMaterial, true);
        }

        CreateBeam(stationName + " Buried Feed Trunk", generatedRoot, center + new Vector3(9f, 0.48f, 0f), center + new Vector3(17f, 0.48f, -10f), 0.16f, darkMetalMaterial);
        for (int lightIndex = 0; lightIndex < 4; lightIndex++)
        {
            float sideX = lightIndex < 2 ? -28f : 28f;
            float sideZ = lightIndex % 2 == 0 ? -25f : 25f;
            Vector3 lightPosition = center + new Vector3(sideX, 0f, sideZ);
            CreatePrimitive(stationName + " Perimeter Flood", PrimitiveType.Cylinder, generatedRoot, lightPosition + Vector3.up * 3f, new Vector3(0.13f, 3f, 0.13f), darkMetalMaterial, false);
            CreatePrimitive(stationName + " Perimeter Lamp", PrimitiveType.Sphere, generatedRoot, lightPosition + Vector3.up * 5.9f, new Vector3(0.34f, 0.2f, 0.34f), emissiveAmberMaterial, false);
        }

        GameObject areaLightObject = new GameObject(stationName + " Yard Light");
        areaLightObject.transform.SetParent(generatedRoot, false);
        areaLightObject.transform.localPosition = center + new Vector3(25f, 5.5f, -8f);
        Light areaLight = areaLightObject.AddComponent<Light>();
        areaLight.type = LightType.Point;
        areaLight.color = new Color(0.52f, 0.76f, 1f);
        areaLight.intensity = 1.3f;
        areaLight.range = 32f;
        areaLight.shadows = LightShadows.None;
    }

    private DishController BuildRadioDish(Vector3 dishCenter, float dishRadius, string dishName, float initialAzimuth, float initialElevation, bool primaryDish)
    {
        float supportHeight = dishRadius * 0.82f;
        float pivotHeight = dishCenter.y + supportHeight;
        float foundationWidth = dishRadius * 1.25f;
        CreatePrimitive(dishName + " Turntable Foundation", PrimitiveType.Cylinder, generatedRoot, dishCenter + Vector3.up * 0.43f, new Vector3(foundationWidth, 0.25f, foundationWidth), darkMetalMaterial, true);
        CreatePrimitive(dishName + " Turntable Bearing", PrimitiveType.Cylinder, generatedRoot, dishCenter + Vector3.up * 0.68f, new Vector3(dishRadius * 0.92f, 0.18f, dishRadius * 0.92f), metalMaterial, false);
        float pedestalHeight = (supportHeight - 0.75f) * 0.5f;
        CreatePrimitive(dishName + " Central Pedestal", PrimitiveType.Cylinder, generatedRoot, dishCenter + Vector3.up * (0.75f + pedestalHeight), new Vector3(dishRadius * 0.22f, pedestalHeight, dishRadius * 0.22f), darkMetalMaterial, false);

        float footOffset = dishRadius * 0.22f;
        Vector3[] towerFootPositions =
        {
            new Vector3(-footOffset, 0.62f, -footOffset),
            new Vector3(footOffset, 0.62f, -footOffset),
            new Vector3(-footOffset, 0.62f, footOffset),
            new Vector3(footOffset, 0.62f, footOffset)
        };
        for (int legIndex = 0; legIndex < towerFootPositions.Length; legIndex++)
        {
            Vector3 localFoot = towerFootPositions[legIndex];
            Vector3 basePoint = dishCenter + localFoot;
            Vector3 topPoint = new Vector3(dishCenter.x + localFoot.x * 0.2f, pivotHeight - 0.15f, dishCenter.z + localFoot.z * 0.2f);
            CreateBeam(dishName + " Tower Leg", generatedRoot, basePoint, topPoint, dishRadius * 0.018f, metalMaterial);
        }

        float braceOffset = footOffset * 0.96f;
        CreateBeam(dishName + " Tower Cross Brace", generatedRoot, new Vector3(dishCenter.x - braceOffset, dishCenter.y + 1.85f, dishCenter.z - braceOffset), new Vector3(dishCenter.x + braceOffset, pivotHeight - dishRadius * 0.22f, dishCenter.z + braceOffset), dishRadius * 0.006f, darkMetalMaterial);
        CreateBeam(dishName + " Tower Cross Brace", generatedRoot, new Vector3(dishCenter.x + braceOffset, dishCenter.y + 1.85f, dishCenter.z - braceOffset), new Vector3(dishCenter.x - braceOffset, pivotHeight - dishRadius * 0.22f, dishCenter.z + braceOffset), dishRadius * 0.006f, darkMetalMaterial);

        GameObject azimuthObject = new GameObject(dishName + " Azimuth Drive");
        azimuthObject.transform.SetParent(generatedRoot, false);
        azimuthObject.transform.localPosition = new Vector3(dishCenter.x, pivotHeight, dishCenter.z);
        Transform azimuthPivot = azimuthObject.transform;
        CreatePrimitive(dishName + " Azimuth Head Motor", PrimitiveType.Cube, azimuthPivot, new Vector3(0f, 0.15f, 0f), new Vector3(dishRadius * 0.38f, 0.55f, dishRadius * 0.33f), darkMetalMaterial, false);
        CreatePrimitive(dishName + " Rear Yoke", PrimitiveType.Cube, azimuthPivot, new Vector3(0f, dishRadius * 0.12f, 0f), new Vector3(dishRadius * 0.08f, dishRadius * 0.18f, dishRadius * 0.08f), metalMaterial, false);

        GameObject elevationObject = new GameObject(dishName + " Elevation Trunnion");
        elevationObject.transform.SetParent(azimuthPivot, false);
        elevationObject.transform.localPosition = new Vector3(0f, dishRadius * 0.18f, 0f);
        Transform elevationPivot = elevationObject.transform;

        float reflectorDepth = dishRadius * 0.235f;
        Mesh reflectorMesh = CreateParabolicReflectorMesh(dishRadius, reflectorDepth, 24, 64);
        CreateMeshObject(dishName + " Parabolic Reflector", elevationPivot, Vector3.zero, reflectorMesh, dishMaterial, false);
        Mesh rimMesh = CreateTorusMesh(dishRadius, dishRadius * 0.013f, 12, 80);
        CreateMeshObject(dishName + " Reflector Rim", elevationPivot, Vector3.zero, rimMesh, metalMaterial, false);

        for (int spokeIndex = 0; spokeIndex < 8; spokeIndex++)
        {
            float angle = spokeIndex * Mathf.PI * 2f / 8f;
            Vector3 rimPoint = new Vector3(Mathf.Cos(angle) * dishRadius, Mathf.Sin(angle) * dishRadius, -reflectorDepth * 0.18f);
            CreateBeam(dishName + " Reflector Rear Spoke", elevationPivot, new Vector3(0f, 0f, -reflectorDepth), rimPoint, dishRadius * 0.012f, dishBackMaterial);
        }

        float feedDistance = dishRadius * 0.49f;
        CreateBeam(dishName + " Feed Boom", elevationPivot, new Vector3(0f, 0f, -reflectorDepth), new Vector3(0f, 0f, feedDistance), dishRadius * 0.018f, metalMaterial);
        CreateBeam(dishName + " Feed Brace Left", elevationPivot, new Vector3(-dishRadius * 0.12f, -dishRadius * 0.03f, feedDistance * 0.72f), new Vector3(0f, 0f, feedDistance * 0.95f), dishRadius * 0.009f, metalMaterial);
        CreateBeam(dishName + " Feed Brace Right", elevationPivot, new Vector3(dishRadius * 0.12f, -dishRadius * 0.03f, feedDistance * 0.72f), new Vector3(0f, 0f, feedDistance * 0.95f), dishRadius * 0.009f, metalMaterial);
        CreatePrimitive(dishName + " Feed Receiver", PrimitiveType.Sphere, elevationPivot, new Vector3(0f, 0f, feedDistance), Vector3.one * dishRadius * 0.075f, emissiveCyanMaterial, false);
        CreatePrimitive(dishName + " Rear Equipment Canister", PrimitiveType.Cylinder, elevationPivot, new Vector3(0f, 0f, -reflectorDepth * 1.25f), new Vector3(dishRadius * 0.13f, dishRadius * 0.085f, dishRadius * 0.13f), darkMetalMaterial, false);
        CreatePrimitive(dishName + " Azimuth Beacon", PrimitiveType.Sphere, azimuthPivot, new Vector3(0f, dishRadius * 0.2f, 0f), Vector3.one * dishRadius * 0.055f, emissiveAmberMaterial, false);

        DishController dishController = azimuthObject.AddComponent<DishController>();
        dishController.Initialize(azimuthPivot, elevationPivot, initialAzimuth, initialElevation);
        if (primaryDish)
        {
            CreatePrimitive("Primary Dish Warning Beacon", PrimitiveType.Sphere, generatedRoot, dishCenter + Vector3.up * (supportHeight + dishRadius * 0.35f), Vector3.one * 0.46f, emissiveAmberMaterial, false);
        }

        return dishController;
    }

    private void BuildConsole(DishController dishController)
    {
        const float buildingX = -23f;
        const float buildingZ = 3f;
        CreatePrimitive("Console Pedestal", PrimitiveType.Cube, generatedRoot, new Vector3(buildingX, 0.69f, buildingZ + 1.5f), new Vector3(2.55f, 0.9f, 1.45f), darkMetalMaterial, true);
        CreatePrimitive("Console Work Surface", PrimitiveType.Cube, generatedRoot, new Vector3(buildingX, 1.18f, buildingZ + 1.5f), new Vector3(3.15f, 0.18f, 1.8f), metalMaterial, true);
        CreatePrimitive("Console Keyboard", PrimitiveType.Cube, generatedRoot, new Vector3(buildingX, 1.31f, buildingZ + 1.05f), new Vector3(1.65f, 0.08f, 0.48f), panelMaterial, false);

        GameObject screenObject = CreatePrimitive("Array Control Console", PrimitiveType.Cube, generatedRoot, new Vector3(buildingX, 1.88f, buildingZ + 0.78f), new Vector3(2.25f, 1.1f, 0.18f), panelMaterial, true);
        screenObject.transform.localRotation = Quaternion.Euler(11f, 0f, 0f);
        BoxCollider screenCollider = screenObject.GetComponent<BoxCollider>();
        if (screenCollider != null)
        {
            screenCollider.size = Vector3.one;
        }

        CreatePrimitive("Console Bezel Top", PrimitiveType.Cube, generatedRoot, new Vector3(buildingX, 2.37f, buildingZ + 0.665f), new Vector3(2.03f, 0.08f, 0.035f), darkMetalMaterial, false);
        CreatePrimitive("Console Bezel Bottom", PrimitiveType.Cube, generatedRoot, new Vector3(buildingX, 1.39f, buildingZ + 0.665f), new Vector3(2.03f, 0.08f, 0.035f), darkMetalMaterial, false);
        CreatePrimitive("Console Bezel Left", PrimitiveType.Cube, generatedRoot, new Vector3(buildingX - 1f, 1.88f, buildingZ + 0.665f), new Vector3(0.08f, 0.9f, 0.035f), darkMetalMaterial, false);
        CreatePrimitive("Console Bezel Right", PrimitiveType.Cube, generatedRoot, new Vector3(buildingX + 1f, 1.88f, buildingZ + 0.665f), new Vector3(0.08f, 0.9f, 0.035f), darkMetalMaterial, false);
        CreatePrimitive("Console Status Lamp", PrimitiveType.Sphere, generatedRoot, new Vector3(buildingX + 1.02f, 1.4f, buildingZ + 0.89f), new Vector3(0.14f, 0.14f, 0.14f), emissiveAmberMaterial, false);

        ConsoleOperator consoleOperator = screenObject.AddComponent<ConsoleOperator>();
        consoleOperator.ConnectDish(dishController);
    }

    private void BuildExteriorLighting()
    {
        Vector3[] polePositions =
        {
            new Vector3(-12f, 0f, -8f),
            new Vector3(17f, 0f, -7f),
            new Vector3(-18f, 0f, 19f),
            new Vector3(25f, 0f, 19f),
            new Vector3(-33f, 0f, 5f)
        };

        for (int poleIndex = 0; poleIndex < polePositions.Length; poleIndex++)
        {
            Vector3 polePosition = polePositions[poleIndex];
            polePosition.y = 0.1f;
            CreatePrimitive("Apron Light Pole", PrimitiveType.Cylinder, generatedRoot, polePosition + Vector3.up * 3.15f, new Vector3(0.18f, 3.15f, 0.18f), darkMetalMaterial, false);
            CreatePrimitive("Apron Lamp Housing", PrimitiveType.Cube, generatedRoot, polePosition + new Vector3(0f, 6.1f, 0f), new Vector3(0.85f, 0.3f, 0.65f), metalMaterial, false);
            CreatePrimitive("Apron Lamp Lens", PrimitiveType.Sphere, generatedRoot, polePosition + new Vector3(0f, 5.9f, 0f), new Vector3(0.48f, 0.16f, 0.42f), emissiveAmberMaterial, false);

            GameObject pointLightObject = new GameObject("Apron Pool Light");
            pointLightObject.transform.SetParent(generatedRoot, false);
            pointLightObject.transform.localPosition = polePosition + new Vector3(0f, 5.75f, 0f);
            Light pointLight = pointLightObject.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = new Color(1f, 0.58f, 0.32f);
            pointLight.intensity = 2.4f;
            pointLight.range = 16f;
            pointLight.shadows = LightShadows.None;
        }

        CreatePrimitive("Dish Warning Beacon", PrimitiveType.Sphere, generatedRoot, new Vector3(5f, 7.2f, 14f), new Vector3(0.38f, 0.38f, 0.38f), emissiveAmberMaterial, false);
    }

    private void BuildPlayerAndSky()
    {
        GameObject playerObject = new GameObject("FARFIELD Player");
        playerObject.transform.SetParent(generatedRoot, false);
        playerObject.transform.localPosition = new Vector3(0f, InitialPlayerHeightMeters, -21f);
        playerObject.transform.localRotation = Quaternion.identity;

        CharacterController characterController = playerObject.AddComponent<CharacterController>();
        characterController.height = 1.8f;
        characterController.radius = 0.34f;
        characterController.center = new Vector3(0f, 0.9f, 0f);
        characterController.slopeLimit = 48f;
        characterController.stepOffset = 0.34f;
        characterController.skinWidth = 0.045f;

        GameObject cameraObject = new GameObject("First Person Camera");
        cameraObject.transform.SetParent(playerObject.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.62f, 0f);
        cameraObject.transform.localRotation = Quaternion.identity;
        Camera playerCamera = cameraObject.AddComponent<Camera>();
        playerCamera.fieldOfView = 76f;
        playerCamera.nearClipPlane = 0.06f;
        playerCamera.farClipPlane = 1050f;
        playerCamera.allowHDR = true;
        playerCamera.allowMSAA = true;
        cameraObject.AddComponent<AudioListener>();

        FirstPersonController playerController = playerObject.AddComponent<FirstPersonController>();
        playerController.Initialize(playerCamera);

        Light sunLight = CreateDirectionalLight("FARFIELD Sun", new Color(1f, 0.88f, 0.72f), 1.15f, true);
        Light moonLight = CreateDirectionalLight("FARFIELD Moon", new Color(0.46f, 0.58f, 1f), 0.2f, false);
        Renderer starRenderer = CreateStarField();
        SkyCycle skyCycle = generatedRoot.gameObject.AddComponent<SkyCycle>();
        skyCycle.Initialize(playerCamera, sunLight, moonLight, starRenderer, 22.1f, 0.035f);
    }

    private Light CreateDirectionalLight(string lightName, Color color, float intensity, bool castShadows)
    {
        GameObject lightObject = new GameObject(lightName);
        lightObject.transform.SetParent(generatedRoot, false);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
        light.shadowStrength = 0.65f;
        return light;
    }

    private Renderer CreateStarField()
    {
        Mesh starMesh = CreateStarMesh(StarCount, StarFieldRadiusMeters);
        GameObject starObject = CreateMeshObject("Night Sky Star Field", generatedRoot, Vector3.zero, starMesh, CreateStarMaterial(), false);
        MeshRenderer renderer = starObject.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        return renderer;
    }

    private Material CreateStarMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        Material material = new Material(shader)
        {
            name = "FF Procedural Stars"
        };
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }
        else
        {
            material.color = Color.white;
        }
        if (material.HasProperty("_Cull"))
        {
            material.SetInt("_Cull", (int)CullMode.Off);
        }
        TrackResource(material);
        return material;
    }

    private static Mesh CreateStarMesh(int starCount, float radius)
    {
        Vector3[] vertices = new Vector3[starCount * 4];
        Vector2[] uv = new Vector2[starCount * 4];
        int[] triangles = new int[starCount * 6];

        for (int starIndex = 0; starIndex < starCount; starIndex++)
        {
            float verticalSample = 0.045f + (starIndex * 37 % 951) / 1000f;
            float azimuth = (starIndex * 137.50776f) * Mathf.Deg2Rad;
            float horizontalRadius = Mathf.Sqrt(1f - verticalSample * verticalSample);
            Vector3 direction = new Vector3(Mathf.Cos(azimuth) * horizontalRadius, verticalSample, Mathf.Sin(azimuth) * horizontalRadius);
            Vector3 referenceUp = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.92f ? Vector3.right : Vector3.up;
            Vector3 right = Vector3.Cross(referenceUp, direction).normalized;
            Vector3 up = Vector3.Cross(direction, right).normalized;
            Vector3 center = direction * radius;
            float size = 0.18f + (starIndex % 5) * 0.055f;
            int vertexIndex = starIndex * 4;

            vertices[vertexIndex] = center - right * size - up * size;
            vertices[vertexIndex + 1] = center - right * size + up * size;
            vertices[vertexIndex + 2] = center + right * size + up * size;
            vertices[vertexIndex + 3] = center + right * size - up * size;
            uv[vertexIndex] = new Vector2(0f, 0f);
            uv[vertexIndex + 1] = new Vector2(0f, 1f);
            uv[vertexIndex + 2] = new Vector2(1f, 1f);
            uv[vertexIndex + 3] = new Vector2(1f, 0f);

            int triangleIndex = starIndex * 6;
            triangles[triangleIndex] = vertexIndex;
            triangles[triangleIndex + 1] = vertexIndex + 2;
            triangles[triangleIndex + 2] = vertexIndex + 1;
            triangles[triangleIndex + 3] = vertexIndex;
            triangles[triangleIndex + 4] = vertexIndex + 3;
            triangles[triangleIndex + 5] = vertexIndex + 2;
        }

        Mesh mesh = new Mesh { name = "FARFIELD Star Dome Mesh" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private GameObject CreatePrimitive(string objectName, PrimitiveType primitiveType, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, bool keepCollider)
    {
        GameObject primitive = GameObject.CreatePrimitive(primitiveType);
        primitive.name = objectName;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = localPosition;
        primitive.transform.localRotation = Quaternion.identity;
        primitive.transform.localScale = localScale;

        Renderer renderer = primitive.GetComponent<Renderer>();
        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
        }

        if (!keepCollider)
        {
            Collider primitiveCollider = primitive.GetComponent<Collider>();
            if (primitiveCollider != null)
            {
                if (Application.isPlaying) Destroy(primitiveCollider);
                else DestroyImmediate(primitiveCollider);
            }
        }

        return primitive;
    }

    private GameObject CreateMeshObject(string objectName, Transform parent, Vector3 localPosition, Mesh mesh, Material material, bool addMeshCollider)
    {
        TrackResource(mesh);
        GameObject meshObject = new GameObject(objectName);
        meshObject.transform.SetParent(parent, false);
        meshObject.transform.localPosition = localPosition;
        MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;
        MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        if (addMeshCollider)
        {
            MeshCollider meshCollider = meshObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
        }

        return meshObject;
    }

    private void CreateBeam(string objectName, Transform parent, Vector3 start, Vector3 end, float radius, Material material)
    {
        Vector3 offset = end - start;
        float length = offset.magnitude;
        if (length < 0.001f)
        {
            return;
        }

        GameObject beam = CreatePrimitive(objectName, PrimitiveType.Cylinder, parent, (start + end) * 0.5f, new Vector3(radius * 2f, length * 0.5f, radius * 2f), material, false);
        beam.transform.localRotation = Quaternion.FromToRotation(Vector3.up, offset / length);
    }

    private static Mesh CreateParabolicReflectorMesh(float radius, float depth, int radialSegments, int angularSegments)
    {
        Vector3[] vertices = new Vector3[1 + radialSegments * angularSegments];
        int[] triangles = new int[angularSegments * 3 + (radialSegments - 1) * angularSegments * 6];
        vertices[0] = new Vector3(0f, 0f, -depth);

        for (int ring = 1; ring <= radialSegments; ring++)
        {
            float radialFraction = ring / (float)radialSegments;
            float ringRadius = radius * radialFraction;
            float z = -depth * (1f - radialFraction * radialFraction);
            for (int segment = 0; segment < angularSegments; segment++)
            {
                float angle = segment * Mathf.PI * 2f / angularSegments;
                int vertexIndex = 1 + (ring - 1) * angularSegments + segment;
                vertices[vertexIndex] = new Vector3(Mathf.Cos(angle) * ringRadius, Mathf.Sin(angle) * ringRadius, z);
            }
        }

        int triangleIndex = 0;
        for (int segment = 0; segment < angularSegments; segment++)
        {
            int current = 1 + segment;
            int next = 1 + (segment + 1) % angularSegments;
            triangles[triangleIndex++] = 0;
            triangles[triangleIndex++] = current;
            triangles[triangleIndex++] = next;
        }

        for (int ring = 1; ring < radialSegments; ring++)
        {
            int innerStart = 1 + (ring - 1) * angularSegments;
            int outerStart = 1 + ring * angularSegments;
            for (int segment = 0; segment < angularSegments; segment++)
            {
                int nextSegment = (segment + 1) % angularSegments;
                int innerCurrent = innerStart + segment;
                int innerNext = innerStart + nextSegment;
                int outerCurrent = outerStart + segment;
                int outerNext = outerStart + nextSegment;
                triangles[triangleIndex++] = innerCurrent;
                triangles[triangleIndex++] = outerCurrent;
                triangles[triangleIndex++] = outerNext;
                triangles[triangleIndex++] = innerCurrent;
                triangles[triangleIndex++] = outerNext;
                triangles[triangleIndex++] = innerNext;
            }
        }

        Mesh mesh = new Mesh { name = "FARFIELD Parabolic Reflector Mesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateTorusMesh(float majorRadius, float tubeRadius, int tubeSegments, int ringSegments)
    {
        Vector3[] vertices = new Vector3[tubeSegments * ringSegments];
        int[] triangles = new int[tubeSegments * ringSegments * 6];

        for (int ringIndex = 0; ringIndex < ringSegments; ringIndex++)
        {
            float ringAngle = ringIndex * Mathf.PI * 2f / ringSegments;
            Vector3 ringCenter = new Vector3(Mathf.Cos(ringAngle) * majorRadius, Mathf.Sin(ringAngle) * majorRadius, 0f);
            Vector3 outward = new Vector3(Mathf.Cos(ringAngle), Mathf.Sin(ringAngle), 0f);
            for (int tubeIndex = 0; tubeIndex < tubeSegments; tubeIndex++)
            {
                float tubeAngle = tubeIndex * Mathf.PI * 2f / tubeSegments;
                Vector3 offset = outward * (Mathf.Cos(tubeAngle) * tubeRadius) + Vector3.forward * (Mathf.Sin(tubeAngle) * tubeRadius);
                vertices[ringIndex * tubeSegments + tubeIndex] = ringCenter + offset;
            }
        }

        int triangleIndex = 0;
        for (int ringIndex = 0; ringIndex < ringSegments; ringIndex++)
        {
            int nextRing = (ringIndex + 1) % ringSegments;
            for (int tubeIndex = 0; tubeIndex < tubeSegments; tubeIndex++)
            {
                int nextTube = (tubeIndex + 1) % tubeSegments;
                int a = ringIndex * tubeSegments + tubeIndex;
                int b = nextRing * tubeSegments + tubeIndex;
                int c = nextRing * tubeSegments + nextTube;
                int d = ringIndex * tubeSegments + nextTube;
                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = b;
                triangles[triangleIndex++] = c;
                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = c;
                triangles[triangleIndex++] = d;
            }
        }

        Mesh mesh = new Mesh { name = "FARFIELD Reflector Rim Mesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
