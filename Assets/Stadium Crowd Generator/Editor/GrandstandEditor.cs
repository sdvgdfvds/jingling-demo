using UnityEngine;
using UnityEditor;


[CustomEditor(typeof(GrandstandCreator))]
public class GrandstandEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        EditorGUILayout.Space();
        GrandstandCreator gc = target as GrandstandCreator;
        EditorGUILayout.HelpBox("Seating Capacity : " + gc.seatingCapacity.ToString(), MessageType.None);
    }
}

public class InstantiateGrandstand : EditorWindow
{
    [MenuItem("Grandstand/VAT Grandstand")]
    public static void GenerateVATGrandstand()
    {
        GameObject prefab = (GameObject)AssetDatabase.LoadAssetAtPath("Assets/Stadium Crowd Generator/Prefabs/VATGrandstand.prefab", typeof(GameObject));
        GameObject instantiatedprefab = Instantiate(prefab, Vector3.zero, Quaternion.identity);
    }
    [MenuItem("Grandstand/SkinnedMesh Grandstand")]
    public static void GenerateSMGrandstand()
    {
        GameObject prefab = (GameObject)AssetDatabase.LoadAssetAtPath("Assets/Stadium Crowd Generator/Prefabs/SkinnedMesh Grandstand.prefab", typeof(GameObject));
        GameObject instantiatedprefab = Instantiate(prefab, Vector3.zero, Quaternion.identity);
    }
}
