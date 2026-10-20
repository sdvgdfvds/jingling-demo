using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Spectators
{
    public GameObject Spectator;
    public List<Material> Emotions = new List<Material>();
}
public class SentimentHandler : MonoBehaviour
{
    public List<Spectators> spectators = new List<Spectators>();
    public List<GameObject> spectatorGO;
    void Start()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            if(transform.GetChild(i).name.StartsWith("Active Emotions"))
            spectatorGO.Add(transform.GetChild(i).gameObject);
        }
        
    }
    public void Cheering()
    {
        for (int i = 0; i < spectatorGO.Count; i++)
        {
            spectatorGO[i].GetComponent<MeshRenderer>().sharedMaterial = spectators[int.Parse(spectatorGO[i].name[16].ToString())].Emotions[Mathf.FloorToInt(Random.Range(0,1.9f))];
        }
    }
    public void Dissapointed()
    {
        for (int i = 0; i < spectatorGO.Count; i++)
        {
            spectatorGO[i].GetComponent<MeshRenderer>().sharedMaterial = spectators[int.Parse(spectatorGO[i].name[16].ToString())].Emotions[Mathf.FloorToInt(Random.Range(2,3.9f))];

        }
    }
}
