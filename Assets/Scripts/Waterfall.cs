﻿using UnityEngine;
using System.Collections;
using System.Runtime.InteropServices;
using System.Collections.Generic;

//public class Guideline
//{
//    public struct LineProperty
//    {
//        public int Id;
//        public Vector3 BirthPosition;
//        public Vector3 DeathPosition;
//        public Vector3 Position;
//        public Vector3 InitVelocity;
//        public Vector3 Velocity;
//    }

//    public LineProperty Property;
//    private GameObject Line;
//    private LineRenderer GuidelineRenderer;
//    private float gravity;

//    Guideline(int id, Vector3 birthPos, Vector3 deathPos, Vector3 position, Vector3 initVelocity, Vector3 velocity)
//    {
//        Line = new GameObject();

//        Property.Id = id;
//        Property.BirthPosition = birthPos;
//        Property.DeathPosition = deathPos;
//        Property.Position = position;
//        Property.InitVelocity = initVelocity;
//        Property.Velocity = velocity;
        
//        GuidelineRenderer = Line.AddComponent<LineRenderer>();
//        GuidelineRenderer.material.shader = Shader.Find("Unlit/Color");
//        GuidelineRenderer.SetPosition(0, Property.BirthPosition);
//        GuidelineRenderer.SetPosition(1, Property.DeathPosition);
//    }

//    Vector3[] GetParabolaPoints(int numDivision)
//    {
//        List<Vector3> result = new List<Vector3>();
//        float a = (Property.DeathPosition.y - Property.BirthPosition.y) / Mathf.Pow((Property.DeathPosition.z - Property.BirthPosition.z), 2);

//        for (int i = 0; i < numDivision; i++)
//        {
//            float t = i / (float)numDivision;
//            float x = t * Property.DeathPosition.z - Property.BirthPosition.z;
//            float y = a * (x - Property.BirthPosition.z) * (x - Property.BirthPosition.z) + Property.BirthPosition.y;
//            result.Add(new Vector3(0, y, x));
//        }
//        return result.ToArray();
//    }
//}

public struct StreamLine
{
    public int Id;
    public Vector3 BirthPosition;
    public Vector3 DeathPosition;
    public Vector3 Position;
    public Vector3 InitVelocity;
    public Vector3 Velocity;
}

public struct Drop
{
    public uint StreamId;
    public float DropSize;
    public Vector3 Position;
    public Vector3 Velocity;
}

public class Waterfall : MonoBehaviour {

    #region Global parameters

    public Vector3 AreaSize = new Vector3(1.0f, 15.0f, 1.0f);
    public Texture2D DropTexture;
    public bool showStreamLines = true;
    public Camera BillboardCam;

    #endregion

    #region Emitter parameters

    public Vector3 EmitterSize = new Vector3(0, 20, 0);
    public Vector3 EliminatorSize = new Vector3(0, 0, -3);

    [Range(0.0005f, 2.0f)]
    public float DropSize = 0.001f;

    [Range(0.01f, 10.0f)]
    public float g = 4.0f;

    [Range(0.1f, 1.0f)]
    public float Jet = 1.0f;

    const int maxDropsCount = 3000000;
    const int streamLinesCount = 64;
    const int maxEmitQuantity = 128 * streamLinesCount;
    const int numThreadX = 8;
    const int numThreadY = 1;
    const int numThreadZ = 1;
    private int perlinT = 0;

    #endregion

    #region Stream lines

    public ComputeShader StreamsCS;
    public ComputeBuffer StreamLinesBuff;
    public Shader StreamLinesRenderShader;
    public Material StreamLinesMaterial;

    public GameObject[] Lines;

    #endregion

    #region Drop

    public ComputeShader DropsCS;

    public ComputeBuffer DropsBuff;
    public ComputeBuffer DeadBuff1;
    public ComputeBuffer DeadBuff2;
    public ComputeBuffer AliveBuff1;
    public ComputeBuffer AliveBuff2;

    public Shader DropsRenderShader;
    public Material DropsMaterial;

    public ComputeBuffer BuffArgs;

    #endregion

    #region Noise

    public RenderTexture PerlinTexture;
    public Shader PerlinShader;
    public Material PerlinMaterial;

    #endregion
    
    Vector3[] GetParabolaPoints(Vector3 birthPos, Vector3 deathPos, int numDivision)
    {
        List<Vector3> result = new List<Vector3>();
        float a = (deathPos.y - birthPos.y) / Mathf.Pow((deathPos.z - birthPos.z), 2);

        for (int i = 0; i <= numDivision; i++)
        {
            float t = i / (float)numDivision;
            float x = t * (deathPos.z - birthPos.z);
            float y = a * (x - birthPos.z) * (x - birthPos.z) + birthPos.y;
            result.Add(new Vector3(birthPos.x, y, x));
        }
        return result.ToArray();
    }

    void Start ()
    {
        StreamLinesBuff = new ComputeBuffer(streamLinesCount, Marshal.SizeOf(typeof(StreamLine)));

        DeadBuff1 = new ComputeBuffer(maxDropsCount, Marshal.SizeOf(typeof(Drop)), ComputeBufferType.Append);
        DeadBuff1.SetCounterValue(0);

        DeadBuff2 = new ComputeBuffer(maxDropsCount, Marshal.SizeOf(typeof(Drop)), ComputeBufferType.Append);
        DeadBuff2.SetCounterValue(0);

        AliveBuff1 = new ComputeBuffer(maxDropsCount, Marshal.SizeOf(typeof(Drop)), ComputeBufferType.Append);
        AliveBuff1.SetCounterValue(0);

        AliveBuff2 = new ComputeBuffer(maxDropsCount, Marshal.SizeOf(typeof(Drop)), ComputeBufferType.Append);
        AliveBuff2.SetCounterValue(0);

        DropsBuff = new ComputeBuffer(maxDropsCount, Marshal.SizeOf(typeof(Drop)));

        BuffArgs = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);

        // Setup stream lines
        StreamLine[] streams = new StreamLine[streamLinesCount];
        for (int i = 0; i < streamLinesCount; i++)
        {
            streams[i].Id = i;
            streams[i].BirthPosition = new Vector3(Random.Range(EmitterSize.x + i * 0.2f, EmitterSize.x + i * 0.21f),
                                                   Random.Range(EmitterSize.y - 0.1f, EmitterSize.y + 0.1f),
                                                   Random.Range(EmitterSize.z - 0.1f, EmitterSize.z + 0.1f));
            streams[i].DeathPosition = new Vector3(streams[i].BirthPosition.x,
                                                   Random.Range(EliminatorSize.y - 0.1f, EliminatorSize.y + 0.1f),
                                                   Random.Range(EliminatorSize.z - 0.1f, EliminatorSize.z + 0.1f));
            streams[i].Position = streams[i].BirthPosition;

            var dz = streams[i].DeathPosition.z - streams[i].BirthPosition.z;
            var dy = streams[i].DeathPosition.y - streams[i].BirthPosition.y;

            streams[i].InitVelocity = new Vector3(0, Random.Range(-1.0f, 1.0f), -Mathf.Sqrt((g * dz * dz) / (2 * Mathf.Abs(dy))));
            streams[i].Velocity = streams[i].InitVelocity;
        }
        StreamLinesBuff.SetData(streams);
        StreamLinesMaterial = new Material(StreamLinesRenderShader);
        StreamLinesMaterial.hideFlags = HideFlags.HideAndDontSave;

        // Draw stream lines
        Lines = new GameObject[streamLinesCount];
        for (int i = 0; i < streamLinesCount; i++)
        {
            //Lines[i] = new GameObject("Stream Line [" + i + "]");
            //var lineRenderer = Lines[i].AddComponent<LineRenderer>();
            //lineRenderer.material.shader = Shader.Find("Unlit/Color");
            //lineRenderer.SetVertexCount(11);
            //lineRenderer.SetWidth(0.01f, 0.01f);
            //lineRenderer.SetPositions(GetParabolaPoints(streams[i].BirthPosition, streams[i].DeathPosition, 10));
        }
        //streams = null;

        // Setup drops
        var drops = new Drop[maxDropsCount];
        for (int i = 0; i < maxDropsCount; i++)
        {
            drops[i].StreamId = 0;
            drops[i].DropSize = 0.1f;
            drops[i].Position = new Vector3(0, 0, 0);

            //new Vector3(Random.Range(EmitterSize.x + drops[i].Id * 0.2f, EmitterSize.x + drops[i].Id * 0.21f),
            //                            Random.Range(EmitterSize.y - 0.1f, EmitterSize.y + 0.1f),
            //                            Random.Range(EmitterSize.z - 0.1f, EmitterSize.z + 0.1f));
            drops[i].Velocity = Vector3.down;
        }
        
        DropsBuff.SetData(drops);
        DropsMaterial = new Material(DropsRenderShader);
        DropsMaterial.hideFlags = HideFlags.HideAndDontSave;

        PerlinTexture = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32);
        PerlinTexture.hideFlags = HideFlags.DontSave;
        PerlinTexture.filterMode = FilterMode.Point;
        PerlinTexture.wrapMode = TextureWrapMode.Repeat;
        PerlinMaterial = new Material(PerlinShader);
        Graphics.Blit(null, PerlinTexture, PerlinMaterial, 0);

        // Drops
        int numThreadGroupDrops = maxDropsCount / numThreadX;

        // 0 : Init
        DropsCS.SetBuffer(0, "_DeadBuff1_In", DeadBuff1);
        DropsCS.SetBuffer(0, "_DropsBuff", DropsBuff);
        DropsCS.Dispatch(0, numThreadGroupDrops, 1, 1);
    }

    int GetActiveBuffSize(ComputeBuffer cb)
    {
        int[] args = new int[] { 0, 1, 0, 0 };
        BuffArgs.SetData(args);
        ComputeBuffer.CopyCount(cb, BuffArgs, 0);
        BuffArgs.GetData(args);
        return args[0];
    }

    //int oldCount;
    void OnRenderObject()
    {
        // Stream Lines
        StreamsCS.SetFloat("_DeltaTime", Time.deltaTime);
        StreamsCS.SetFloat("_Gravity", g);

        int numThreadGroupStreamLines = streamLinesCount / numThreadX;
        StreamsCS.SetBuffer(0, "_StreamLinesBuffer", StreamLinesBuff);
        StreamsCS.Dispatch(0, numThreadGroupStreamLines, 1, 1);

        //Drops
        int numThreadGroupDrops = maxDropsCount / numThreadX;
        DropsCS.SetInt("_StreamsCount", streamLinesCount);
        DropsCS.SetFloat("_DeltaTime", Time.deltaTime);
        DropsCS.SetFloat("_Gravity", g);
        DropsCS.SetFloat("_Jet", Jet);

        perlinT++;
        perlinT = perlinT % 64;
        DropsCS.SetInt("_PerlinT", perlinT);

        // 1 : Emit
        DropsCS.SetBuffer(1, "_DeadBuff1_Out", DeadBuff1);
        DropsCS.SetBuffer(1, "_AliveBuff2_In", AliveBuff2);
        DropsCS.SetBuffer(1, "_StreamLinesBuffer", StreamLinesBuff);
        DropsCS.SetTexture(1, "_PerlinTexture", PerlinTexture);
        
        var emitCount = GetActiveBuffSize(DeadBuff1) > maxEmitQuantity ? numThreadGroupStreamLines : 0;
        //Debug.Log("1 : Emit");
        //Debug.Log("[Dead1] Before Emit : " + GetActiveBuffSize(DeadBuff1));
        //Debug.Log("[Alive1] Before Emit : " + GetActiveBuffSize(AliveBuff1));
        //Debug.Log("[Alive2] Before Emit : " + GetActiveBuffSize(AliveBuff2));

        DropsCS.Dispatch(1, emitCount, 1, 1);

        //Debug.Log("[Dead1] After Emit : " + GetActiveBuffSize(DeadBuff1));
        //Debug.Log("[Alive1] After Emit : " + GetActiveBuffSize(AliveBuff1));
        //Debug.Log("[Alive2] After Emit : " + GetActiveBuffSize(AliveBuff2));
        //Debug.Log("--------------------------------------------------------");

        // 2 : Update
        DropsCS.SetBuffer(2, "_AliveBuff1_Out", AliveBuff1);
        DropsCS.SetBuffer(2, "_AliveBuff2_In", AliveBuff2);
        DropsCS.SetBuffer(2, "_DeadBuff1_In", DeadBuff1);
        DropsCS.SetBuffer(2, "_StreamLinesBuffer", StreamLinesBuff);
        //var updateGroups = GetActiveBuffSize(AliveBuff1) > maxEmitQuantity ? GetActiveBuffSize(AliveBuff1) : 0;
        //Debug.Log("2 : Update");

        DropsCS.Dispatch(2, GetActiveBuffSize(AliveBuff1) / numThreadX, 1, 1);

        //Debug.Log("[Dead1] After Update : " + GetActiveBuffSize(DeadBuff1));
        //Debug.Log("[Alive1] After Update : " + GetActiveBuffSize(AliveBuff1));
        //Debug.Log("[Alive2] After Update : " + GetActiveBuffSize(AliveBuff2));
        //Debug.Log("--------------------------------------------------------");
        
        // vert / geom / frag shader
        var inverseViewMatrix = BillboardCam.worldToCameraMatrix.inverse;

        StreamLinesMaterial.SetPass(0);
        StreamLinesMaterial.SetMatrix("_InvViewMatrix", inverseViewMatrix);
        StreamLinesMaterial.SetTexture("_DropTexture", DropTexture);
        StreamLinesMaterial.SetFloat("_DropSize", DropSize);
        StreamLinesMaterial.SetBuffer("_StreamLinesBuffer", StreamLinesBuff);
        Graphics.DrawProcedural(MeshTopology.Points, streamLinesCount);

        DropsMaterial.SetPass(0);
        DropsMaterial.SetMatrix("_InvViewMatrix", inverseViewMatrix);
        DropsMaterial.SetTexture("_DropTexture", DropTexture);
        DropsMaterial.SetFloat("_DropSize", DropSize);
        DropsMaterial.SetBuffer("_DropsBuff", AliveBuff2);
        Graphics.DrawProcedural(MeshTopology.Points, maxDropsCount);

        //Debug.Log("Dead1 : " + GetActiveBuffSize(DeadBuff1));
        //Debug.Log("Alive2 : " + GetActiveBuffSize(AliveBuff2));

        // 3 : Copy
        AliveBuff1.SetCounterValue(0);
        DropsCS.SetBuffer(3, "_AliveBuff1_In", AliveBuff1);
        DropsCS.SetBuffer(3, "_AliveBuff2_Out", AliveBuff2);
        DropsCS.Dispatch(3, GetActiveBuffSize(AliveBuff2) / numThreadX, 1, 1);

        //Debug.Log("Out : " + GetActiveBuffSize(AliveBuff2) + " In : " + GetActiveBuffSize(AliveBuff1));
        //AliveBuff1 = AliveBuff2;
        //AliveBuff2.SetCounterValue(0);
    }

    void OnDisable()
    {
        if (StreamLinesBuff != null) StreamLinesBuff.Release();
        if (DropsBuff != null)  DropsBuff.Release();
        if (DeadBuff1 != null)  DeadBuff1.Release();
        if (DeadBuff2 != null)  DeadBuff2.Release();
        if (AliveBuff1 != null) AliveBuff1.Release();
        if (AliveBuff2 != null) AliveBuff2.Release();
        if (BuffArgs != null)   BuffArgs.Release();

        if (StreamLinesMaterial != null)    DestroyImmediate(StreamLinesMaterial);
        if (DropsMaterial != null)          DestroyImmediate(DropsMaterial);
    }
}
