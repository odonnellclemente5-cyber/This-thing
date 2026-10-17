using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(FarFieldBootstrap))]
public sealed class FarFieldBootstrapEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Build creates saved scenery only. Play initializes the player and sky. " +
            "Rebuilding replaces this builder's scenery, including manual edits. Clear supports Undo; saved assets are retained.", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Build World In Editor")) Build((FarFieldBootstrap)target);
            if (GUILayout.Button("Clear Generated World")) Clear((FarFieldBootstrap)target);
        }
    }

    [MenuItem("FARFIELD/Build World In Editor")]
    private static void BuildActiveScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) return;
        FarFieldBootstrap builder = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            var candidate = root.GetComponentInChildren<FarFieldBootstrap>(true);
            if (candidate == null) continue;
            if (builder != null)
            {
                Debug.LogError("Multiple FARFIELD bootstraps: select the intended builder and use its Inspector.");
                return;
            }
            builder = candidate;
        }
        if (builder == null)
        {
            var go = new GameObject("FARFIELD Bootstrap");
            Undo.RegisterCreatedObjectUndo(go, "Create FARFIELD bootstrap");
            builder = Undo.AddComponent<FarFieldBootstrap>(go);
        }
        Selection.activeGameObject = builder.gameObject;
        Build(builder);
    }

    private static List<FarFieldGeneratedWorld> OwnedWorlds(FarFieldBootstrap builder)
    {
        var worlds = new List<FarFieldGeneratedWorld>();
        foreach (GameObject root in builder.gameObject.scene.GetRootGameObjects())
            foreach (var world in root.GetComponentsInChildren<FarFieldGeneratedWorld>(true))
                if (world.Owner == builder) worlds.Add(world);
        return worlds;
    }

    private static bool CanEdit(FarFieldBootstrap builder) =>
        !EditorApplication.isPlayingOrWillChangePlaymode && !EditorUtility.IsPersistent(builder) &&
        builder.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewScene(builder.gameObject.scene) &&
        PrefabStageUtility.GetPrefabStage(builder.gameObject) == null;

    private static void Build(FarFieldBootstrap builder)
    {
        if (!CanEdit(builder)) return;
        Scene scene = builder.gameObject.scene;
        if (string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("Save your scene first", "Use File > Save As, then build again.", "OK");
            return;
        }
        var previous = OwnedWorlds(builder);
        if (previous.Count > 0 && !EditorUtility.DisplayDialog("Rebuild FARFIELD scenery?",
            "This replaces generated scenery and your edits inside it. A scene backup will be saved first.", "Rebuild", "Cancel")) return;

        // Save a copy of the current in-memory scene, including unsaved manual edits.
        string backup = AssetDatabase.GenerateUniqueAssetPath(
            System.IO.Path.ChangeExtension(scene.path, null) + "_BeforeWorldBuild.unity");
        if (!EditorSceneManager.SaveScene(scene, backup, true)) return;

        FarFieldGeneratedWorld replacement = null;
        string assetPath = null;
        var resources = new HashSet<UnityEngine.Object>();
        try
        {
            replacement = builder.CreateScenery();
            GatherResources(replacement, resources);
            ValidateColliders(replacement);
            if (!AssetDatabase.IsValidFolder("Assets/FarFieldGenerated"))
                AssetDatabase.CreateFolder("Assets", "FarFieldGenerated");
            assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/FarFieldGenerated/WorldResources.asset");
            bool first = true;
            foreach (var resource in resources)
            {
                if (first) { AssetDatabase.CreateAsset(resource, assetPath); first = false; }
                else AssetDatabase.AddObjectToAsset(resource, assetPath);
            }
            AssetDatabase.SaveAssets();
            // Swap only after generation, geometry validation and persistence succeed.
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build FARFIELD world");
            Undo.RegisterCreatedObjectUndo(replacement.gameObject, "Build FARFIELD world");
            Undo.RecordObject(builder, "Connect saved world");
            builder.SetSavedWorld(replacement);
            foreach (var old in previous) Undo.DestroyObjectImmediate(old.gameObject);
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = replacement.gameObject;
            Debug.Log("FARFIELD scenery built. Save the scene (Ctrl+S). Backup: " + backup, builder);
        }
        catch (Exception exception)
        {
            if (replacement != null) DestroyImmediate(replacement.gameObject);
            foreach (var resource in resources)
                if (resource != null && !EditorUtility.IsPersistent(resource)) DestroyImmediate(resource);
            if (assetPath != null) AssetDatabase.DeleteAsset(assetPath);
            Debug.LogException(exception, builder);
        }
        finally
        {
            builder.FinishEditorGeneration();
        }
    }

    private static void Clear(FarFieldBootstrap builder)
    {
        if (!CanEdit(builder)) return;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Clear FARFIELD world");
        Undo.RecordObject(builder, "Clear FARFIELD world");
        foreach (var world in OwnedWorlds(builder)) Undo.DestroyObjectImmediate(world.gameObject);
        builder.SetSavedWorld(null);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
    }

    private static void GatherResources(FarFieldGeneratedWorld world, HashSet<UnityEngine.Object> resources)
    {
        foreach (var filter in world.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh != null && !EditorUtility.IsPersistent(filter.sharedMesh)) resources.Add(filter.sharedMesh);
        foreach (var renderer in world.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material != null && !EditorUtility.IsPersistent(material)) resources.Add(material);
    }

    private static void ValidateColliders(FarFieldGeneratedWorld world)
    {
        foreach (var collider in world.GetComponentsInChildren<MeshCollider>(true))
        {
            Mesh mesh = collider.sharedMesh;
            if (mesh == null) throw new InvalidOperationException("Generated collider has no mesh.");
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                for (int edge = 0; edge < 3; edge++)
                {
                    Vector3 a = collider.transform.TransformPoint(vertices[triangles[i + edge]]);
                    Vector3 b = collider.transform.TransformPoint(vertices[triangles[i + (edge + 1) % 3]]);
                    if (!float.IsFinite(a.x) || !float.IsFinite(a.y) || !float.IsFinite(a.z) ||
                        !float.IsFinite(b.x) || !float.IsFinite(b.y) || !float.IsFinite(b.z) || Vector3.Distance(a, b) > 100f)
                        throw new InvalidOperationException("Unsafe generated collision edge on " + collider.name);
                }
        }
    }
}
