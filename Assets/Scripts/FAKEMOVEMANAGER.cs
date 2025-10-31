using UnityEngine;
using System.Collections.Generic;

public class FAKEMOVEMANAGER : MonoBehaviour
{
    [Header("Jump Parameters")]
    public float stepHeight;
    public float maxRotation;
    public AnimationCurve ptLerp;
    public AnimationCurve yLerp;

    // Jump points for the piece to move between 
    [Header("Path Points")]  
    public GameObject path_point_1;
    public GameObject path_point_2;
    public GameObject path_point_3;
    public GameObject path_point_4;
    public GameObject path_point_5;

    // Public variables
    [Header("General")]
    public GameObject camera;
    public Vector3Int enemy;
    private tokenMovement tokenMovement;


    //PRi
    // List to hold the path points
    private List<Vector3Int> path_buffer = new List<Vector3Int>();



    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Initialize the path points based on the assigned GameObjects
        path_buffer.Add(Vector3Int.RoundToInt(path_point_1.transform.position));
        // path_buffer.Add(Vector3Int.RoundToInt(path_point_2.transform.position));
        // path_buffer.Add(Vector3Int.RoundToInt(path_point_3.transform.position));
        // path_buffer.Add(Vector3Int.RoundToInt(path_point_4.transform.position));
        // path_buffer.Add(Vector3Int.RoundToInt(path_point_5.transform.position));

        tokenMovement = new tokenMovement(transform, stepHeight, maxRotation, ptLerp, yLerp);

        tokenMovement.setPathPoints(path_buffer);
        tokenMovement.setMoveToPoint(path_buffer[0]);
        //StartCoroutine(tokenMovement.moveAlongPath(0.5f));
    }

    // Update is called once per frame
    void Update()
    {
        camera.transform.LookAt(new Vector3(transform.position.x, 0, transform.position.z));

        //Debug.Log("Starting Move Along Path Coroutine");
        //StartCoroutine(tokenMovement.moveAlongPath(0.5f));
        // StartCoroutine(tokenMovement.moveToPoint(0.5f));
        //Debug.Log("Starting Look At Coroutine");
        //StartCoroutine(tokenMovement.lookAt(enemy));
    }
}
