using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[System.Serializable]
public class Geometry
{
    public GameObject beam;
    public GameObject beamSet;
    public GameObject plank;
    public GameObject step;
    public GameObject seat;
    public GameObject rail;
    public List<GameObject> spectator = new List<GameObject>();

}
[System.Serializable]
public class Optimization
{
    public bool GPUInstancedMaterials;
    [Tooltip("Works only in PlayMode. Avoids GameObject overload by offloading all meshes directly to the GPU.")]
    public bool GPUInstancedMeshes;

}
[System.Serializable]
public class GrandstandCreator : MonoBehaviour
{
    public Geometry geometry;
    [Range(0, 32)]
    public int rows;
    [Range(0, 33)]
    public int columns;
    [Range(0.95f, 1.05f)]
    public float height;
    public enum Distribution
    {
        Standard,
        Easy,
        Compact

    }
    public Distribution distribution;
    public enum Construction
    {
        SteelIBeam,
        Court

    }
    public Construction construction;
    GameObject beamGO, beamSetGO, plankGO, stepGO, seatGO, railGO, spectatorGO;
    [HideInInspector]
    public int seatingCapacity;
    public Optimization optimization;
    bool flag;
    Vector3 positionBeforeChanges;
    Quaternion rotationBeforeChanges;
    #if UNITY_EDITOR
    void OnValidate()
    {
        if (GUI.changed)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (optimization.GPUInstancedMeshes != flag)
                {
                    //Store Variables at the start
                    if(optimization.GPUInstancedMeshes)
                    print("Please check this option only when the grandstand is completely configured. Please read the documentation on the usage of this feature.");
                }
                else
                {
                    // Clear All
                    while (transform.childCount > 0)
                    {
                        DestroyImmediate(transform.GetChild(0).gameObject);
                    }
                }

                if (!optimization.GPUInstancedMeshes)
                {
                    positionBeforeChanges = transform.position;
                    rotationBeforeChanges = transform.rotation;
                    transform.position = Vector3.zero;
                    transform.rotation = Quaternion.identity;

                    //Beams
                    switch (construction)
                    {
                        case Construction.SteelIBeam:
                            for (int i = 0; i < columns; i++)
                            {
                                for (int j = 0; j < rows + 1; j++)
                                {
                                    //For last beam
                                    if (j == rows)
                                    {
                                        beamGO = GameObject.Instantiate(geometry.beam, new Vector3(i, 0, j), Quaternion.identity, transform);
                                        beamGO.transform.localScale = new Vector3(1, height * j + 0.2f, 1);
                                    }
                                    else
                                    {
                                        beamGO = GameObject.Instantiate(geometry.beam, new Vector3(i, 0, j), Quaternion.identity, transform);
                                        beamGO.transform.localScale = new Vector3(1, height * (j + 1), 1);
                                    }
                                }
                            }
                            break;
                        case Construction.Court:
                            for (int i = 0; i < columns - 1; i++)
                            {
                                beamSetGO = GameObject.Instantiate(geometry.beamSet, new Vector3(i, 0.1f, 0), Quaternion.identity, transform);
                                beamSetGO.transform.localScale = new Vector3(1, rows, rows);
                            }
                            break;
                    }

                    //Planks
                    switch (construction)
                    {
                        case Construction.SteelIBeam:
                            for (int i = 0; i < rows; i++)
                            {
                                if (columns % 2 == 0)
                                {
                                    plankGO = GameObject.Instantiate(geometry.plank, new Vector3(columns / 2 - 0.5f, height * (0.5f + (float)i / (float)2), i - 0.05f), Quaternion.identity, transform);
                                    plankGO.transform.localScale = new Vector3(0.1f + (columns - 1), 0.5f, 1);
                                }
                                else
                                {
                                    plankGO = GameObject.Instantiate(geometry.plank, new Vector3(columns / 2, height * (0.5f + (float)i / (float)2), i - 0.05f), Quaternion.identity, transform);
                                    plankGO.transform.localScale = new Vector3(0.1f + (columns - 1), 0.5f, 1);
                                }
                            }
                            break;
                        case Construction.Court:
                            for (int i = 0; i < rows; i++)
                            {
                                if (columns % 2 == 0)
                                {
                                    plankGO = GameObject.Instantiate(geometry.plank, new Vector3(columns / 2 - 0.5f, height * (0.1f + (float)i / (float)2), i - 0.05f), Quaternion.identity, transform);
                                    plankGO.transform.localScale = new Vector3(0.1f + (columns - 1), 2.5f, 1);
                                }
                                else
                                {
                                    plankGO = GameObject.Instantiate(geometry.plank, new Vector3(columns / 2, height * (0.1f + (float)i / (float)2), i - 0.05f), Quaternion.identity, transform);
                                    plankGO.transform.localScale = new Vector3(0.1f + (columns - 1), 2.5f, 1);
                                }
                            }
                            break;
                    }

                    //Steps
                    if (geometry.step != null)
                        for (int i = 0; i < rows; i++)
                        {
                            stepGO = GameObject.Instantiate(geometry.step, new Vector3(0.55f, height * (0.6f + (float)i / (float)2), i + 0.45f), Quaternion.identity, transform);
                            stepGO = GameObject.Instantiate(geometry.step, new Vector3(columns - 1 - 0.55f, height * (0.6f + (float)i / (float)2), i + 0.45f), Quaternion.identity, transform);
                        }

                    //Seats and Spectators
                    seatingCapacity = 0;
                    switch (distribution)
                    {
                        case Distribution.Standard:
                            for (int i = 0; i < columns - 3; i++)
                            {
                                for (int j = 0; j < rows; j++)
                                {
                                    seatingCapacity++;
                                    seatGO = GameObject.Instantiate(geometry.seat, new Vector3(i + 1.5f, height * (0.6f + (float)j / (float)2), j), Quaternion.identity, transform);
                                    if (geometry.spectator.Count != 0)
                                    {
                                        spectatorGO = GameObject.Instantiate(geometry.spectator[seatingCapacity % geometry.spectator.Count], new Vector3(i + 1.5f, height * ((float)j / (float)2) - 0.2f, j + 0.3f), Quaternion.Euler(0, 180, 0), transform);
                                    }
                                }
                            }
                            break;
                        case Distribution.Easy:
                            for (int i = 0; i < (columns - 3) / 1.5f; i++)
                            {
                                for (int j = 0; j < rows; j++)
                                {
                                    seatingCapacity++;
                                    seatGO = GameObject.Instantiate(geometry.seat, new Vector3(i * 1.5f + 1.5f, height * (0.6f + (float)j / (float)2), j), Quaternion.identity, transform);
                                    if (geometry.spectator.Count != 0)
                                    {
                                        spectatorGO = GameObject.Instantiate(geometry.spectator[seatingCapacity % geometry.spectator.Count], new Vector3(i * 1.5f + 1.5f, height * ((float)j / (float)2) - 0.2f, j + 0.3f), Quaternion.Euler(0, 180, 0), transform);
                                    }
                                }
                            }
                            break;
                        case Distribution.Compact:
                            for (int i = 0; i < (columns - 4) * 1.5f; i++)
                            {
                                for (int j = 0; j < rows; j++)
                                {
                                    seatingCapacity++;
                                    seatGO = GameObject.Instantiate(geometry.seat, new Vector3(i / 1.5f + 1.5f, height * (0.6f + (float)j / (float)2), j), Quaternion.identity, transform);
                                    if (geometry.spectator.Count != 0)
                                    {
                                        spectatorGO = GameObject.Instantiate(geometry.spectator[seatingCapacity % geometry.spectator.Count], new Vector3(i / 1.5f + 1.5f, height * ((float)j / (float)2) - 0.2f, j + 0.3f), Quaternion.Euler(0, 180, 0), transform);
                                    }
                                }
                            }
                            break;
                    }

                    //Rails
                    for (int i = 0; i < rows; i++)
                    {
                        railGO = GameObject.Instantiate(geometry.rail, new Vector3(0, height * (0.6f + (float)i / (float)2), i + 0.45f), Quaternion.identity, transform);
                        railGO = GameObject.Instantiate(geometry.rail, new Vector3(columns - 1, height * (0.6f + (float)i / (float)2), i + 0.45f), Quaternion.identity, transform);
                    }
                    for (int i = 0; i < columns - 1; i++)
                    {
                        railGO = GameObject.Instantiate(geometry.rail, new Vector3(i + 0.5f, height * (0.6f + (float)(rows - 1) / (float)2), rows), Quaternion.Euler(0, 90, 0), transform);
                    }

                    //Material Optimization
                    if (optimization.GPUInstancedMaterials)
                    {
                        geometry.beam.GetComponent<MeshRenderer>().sharedMaterial.enableInstancing = true;
                        geometry.plank.GetComponent<MeshRenderer>().sharedMaterial.enableInstancing = true;
                        geometry.rail.GetComponent<MeshRenderer>().sharedMaterial.enableInstancing = true;
                        geometry.seat.GetComponent<MeshRenderer>().sharedMaterial.enableInstancing = true;
                    }
                    else
                    {
                        geometry.beam.GetComponent<MeshRenderer>().sharedMaterial.enableInstancing = false;
                        geometry.plank.GetComponent<MeshRenderer>().sharedMaterial.enableInstancing = false;
                        geometry.rail.GetComponent<MeshRenderer>().sharedMaterial.enableInstancing = false;
                        geometry.seat.GetComponent<MeshRenderer>().sharedMaterial.enableInstancing = false;
                    }
                    // Revert Changes to position
                    transform.position = positionBeforeChanges;
                    transform.rotation = rotationBeforeChanges;
                }

                flag = optimization.GPUInstancedMeshes;

            };
        }
    }
    #endif

    // GPU instanced meshes. Experimental
    List<string> distinct = new List<string>();
    Mesh[] geometryMeshes = new Mesh[10];
    Material[] geometryMaterial = new Material[10];
    Matrix4x4[][] geometryMatrices = new Matrix4x4[10][];
    int[] geometryCounts = new int[10];

    //Spectators
    List<string> spectatorID = new List<string>();
    Mesh[] spectatorMesh = new Mesh[10];
    Material[] spectatorMat = new Material[10];
    Matrix4x4[][] spectatorMatrices = new Matrix4x4[10][];
    int[] spectatorCount = new int[10];



    void Start()
    {
        if (optimization.GPUInstancedMeshes)
        {

            for (int i = 0; i < transform.childCount; i++)
            {
                if (!transform.GetChild(i).name.StartsWith("Spectator Variation"))
                    distinct.Add(transform.GetChild(i).gameObject.name);
            }
            distinct = distinct.Distinct().ToList();
            
            
            //Initialize GeometryMeshes, materials, matrices
            bool flag;
            int index = 0;
            for (int i = 0; i < distinct.Count; i++)
            {
                flag = true;
                while (flag)
                {
                    if(transform.GetChild(index).name == distinct[i])
                    {
                        geometryMeshes[i] = transform.GetChild(index).gameObject.GetComponent<MeshFilter>().sharedMesh;
                        geometryMaterial[i] = transform.GetChild(index).gameObject.GetComponent<MeshRenderer>().sharedMaterial;
                        geometryMatrices[i] = new Matrix4x4[1023];
                        flag = false;
                    }
                    else
                    index++;
                }
            }

            //Set up Matrices and Counts
            for (int i = 0; i < distinct.Count; i++)
                for (int j = 0; j < transform.childCount; j++)
                {
                    if(transform.GetChild(j).name == distinct[i])
                    {
                        geometryMatrices[i][geometryCounts[i]] = Matrix4x4.TRS(transform.GetChild(j).transform.position, transform.GetChild(j).transform.rotation, transform.GetChild(j).transform.localScale);
                        geometryCounts[i]++;
                    }
                }

            //Spectator Count
            for (int i = 0; i < transform.childCount; i++)
            {
                if (transform.GetChild(i).name.StartsWith("Spectator Variation"))
                    spectatorID.Add(transform.GetChild(i).gameObject.name);
            }
            spectatorID = spectatorID.Distinct().ToList();

            for (int i = 0; i < geometry.spectator.Count; i++)
            {
                spectatorMesh[i] = geometry.spectator[i].GetComponent<MeshFilter>().sharedMesh;
                spectatorMat[i] = geometry.spectator[i].GetComponent<MeshRenderer>().sharedMaterial;
                spectatorMatrices[i] = new Matrix4x4[1023];
                for (int j = 0; j < transform.childCount; j++)
                {
                    if (transform.GetChild(j).name == spectatorID[i])
                    {
                        spectatorMatrices[i][spectatorCount[i]] = Matrix4x4.TRS(transform.GetChild(j).transform.position, transform.GetChild(j).transform.rotation, transform.GetChild(j).transform.localScale);
                        spectatorCount[i]++;
                    }
                }
            }

            // Clear All
            while (transform.childCount > 0)
            {
                DestroyImmediate(transform.GetChild(0).gameObject);
            }
        }

    }

    void Update()
    {
        if (optimization.GPUInstancedMeshes)
        {
            //Graphics Pass for Geometry
            for (int i = 0; i < distinct.Count; i++)
            {
                Graphics.DrawMeshInstanced(geometryMeshes[i],0,geometryMaterial[i],geometryMatrices[i],geometryCounts[i]);
            }
            //Graphics Pass for Spectator Variations
            for (int i = 0; i < geometry.spectator.Count; i++)
            {
                Graphics.DrawMeshInstanced(spectatorMesh[i], 0, spectatorMat[i], spectatorMatrices[i], spectatorCount[i]);
            }


        }

    }

}

