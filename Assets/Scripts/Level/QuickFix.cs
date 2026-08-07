using UnityEngine;
using Unity;

public class QuickFix : MonoBehaviour
{
    public GameObject gameObject;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        gameObject.SetActive(true);
    }
}
