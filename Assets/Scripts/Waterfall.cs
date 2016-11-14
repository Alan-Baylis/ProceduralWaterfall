﻿using UnityEngine;
using System.Collections;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine.Assertions;

namespace ProceduralWaterfall
{
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
        public Vector3 PrevPosition;
        public Vector3 Velocity;
        public Vector4 Params;
    }

    public struct DetectedObject
    {
        public bool IsActive;
        public Vector3 Position;
    }

    public class Waterfall : MonoBehaviour
    {

        #region Global parameters

        public GameObject UrgDevice;
        private Urg urg;

        public Vector3 AreaSize = new Vector3(1.0f, 15.0f, 1.0f);
        public Texture2D DropTexture;
        public bool showStreamLines = true;
        public Camera BillboardCam;


        #endregion

        #region Emitter parameters

        public Vector3 EmitterSize = new Vector3(0, 20, 0);
        public Vector3 EliminatorSize = new Vector3(0, 0, -3);

        const int maxDropsCount = 2097152;
        const int streamLinesCount = 128;
        const int maxEmitQuantity = 128 * streamLinesCount;
        const int numThreadX = 128;
        const int numThreadY = 1;
        const int numThreadZ = 1;

        [Range(0.01f, 10.0f)]
        public float g = 4.0f;

        [Range(0.1f, 1.0f)]
        public float Jet = 1.0f;

        [SerializeField, Header("Drop / Splash Params")]
        Vector4 dropParams = new Vector4(1.0f, 1.0f, 1.0f, 0.015f);

        [Range(0.0005f, 0.2f)]
        public float dropSize = 0.001f;

        [SerializeField]
        Vector4 splashParams = new Vector4(0.5f, 0.5f, 0.1f, 0.1f);

        [Range(0.0001f, 0.1f)]
        public float splashSize = 0.001f;

        #endregion

        #region Stream lines

        [Header("Shaders for Stream Lines")]
        public ComputeShader StreamsCS;
        public ComputeBuffer StreamLinesBuff;
        public Shader StreamLinesRenderShader;
        public Material StreamLinesMaterial;

        public GameObject[] Lines;

        #endregion

        #region Drop

        [Header("Shaders for Drop")]
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

        #region Detected Object

        [Header("Detected Objects")]
        public ComputeBuffer DetectedObjectsBuff;
        private DetectedObject[] detectedObjects;
        const int detectionLimit = 10;
        #endregion

        #region Noise

        [Header("Shaders for Noise")]
        public RenderTexture PerlinTexture;
        public Shader PerlinShader;
        public Material PerlinMaterial;

        #endregion

        void InitializeStreamLines()
        {
            StreamLine[] streams = new StreamLine[streamLinesCount];
            for (int i = 0; i < streamLinesCount; i++)
            {
                streams[i].Id = i;
                streams[i].BirthPosition = new Vector3(EmitterSize.x + i * 0.05f,
                                                       Random.Range(EmitterSize.y - 0.1f, EmitterSize.y + 0.1f),
                                                       Random.Range(EmitterSize.z - 0.1f, EmitterSize.z + 0.1f));
                streams[i].DeathPosition = new Vector3(streams[i].BirthPosition.x,
                                                       Random.Range(EliminatorSize.y - 0.1f, EliminatorSize.y + 0.1f),
                                                       Random.Range(EliminatorSize.z - 0.1f, EliminatorSize.z + 0.1f));
                streams[i].Position = streams[i].BirthPosition;

                var dz = streams[i].DeathPosition.z - streams[i].BirthPosition.z;
                var dy = streams[i].DeathPosition.y - streams[i].BirthPosition.y;

                streams[i].InitVelocity = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(-1.0f, 1.0f), -Mathf.Sqrt((g * dz * dz) / (2 * Mathf.Abs(dy))));
                streams[i].Velocity = streams[i].InitVelocity;
            }
            StreamLinesBuff.SetData(streams);
            StreamLinesMaterial = new Material(StreamLinesRenderShader);
            StreamLinesMaterial.hideFlags = HideFlags.HideAndDontSave;

            if (showStreamLines)
            {
                DrawStreamLines(streams);
            }

            streams = null;
        }

        void InitializeComputeBuffers()
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
            DetectedObjectsBuff = new ComputeBuffer(detectionLimit, Marshal.SizeOf(typeof(DetectedObject)));
            BuffArgs = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);
        }

        void InitializeDrops()
        {
            var drops = new Drop[maxDropsCount];
            for (int i = 0; i < maxDropsCount; i++)
            {
                drops[i].StreamId = 0;
                drops[i].DropSize = 0.1f;
                drops[i].Position = Vector3.zero;
                drops[i].PrevPosition = Vector3.zero;
                drops[i].Velocity = Vector3.down;
                drops[i].Params = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);  // InitVhCoef, InitVvCoef, UpdatePosCoef, UpdateVelCoef
            }

            DropsBuff.SetData(drops);
            DropsMaterial = new Material(DropsRenderShader);
            DropsMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        void InitializeDetectedObjects()
        {
            detectedObjects = new DetectedObject[detectionLimit];
            for (int i = 0; i < detectionLimit; i++)
            {
                detectedObjects[i].IsActive = false;
                detectedObjects[i].Position = Vector3.zero;
            }

            DetectedObjectsBuff.SetData(detectedObjects);
        }

        void UpdateDetectedObjects()
        {
            var objectsFromUrg = urg.DetectedObjects;
            for (int i = 0; i < objectsFromUrg.Length; i++)
            {
                detectedObjects[i].IsActive = objectsFromUrg[i] != Vector3.zero;
                detectedObjects[i].Position = objectsFromUrg[i];
            }
            for (int i = detectionLimit - 1; i > objectsFromUrg.Length; i--)
            {
                detectedObjects[i].IsActive = false;
            }
            DetectedObjectsBuff.SetData(detectedObjects);
        }

        void DrawStreamLines(StreamLine[] streams)
        {
            Lines = new GameObject[streamLinesCount];
            for (int i = 0; i < streamLinesCount; i++)
            {
                Lines[i] = new GameObject("Stream Line [" + i + "]");
                var lineRenderer = Lines[i].AddComponent<LineRenderer>();
                lineRenderer.material.shader = Shader.Find("Unlit/Color");
                lineRenderer.SetVertexCount(11);
                lineRenderer.SetWidth(0.01f, 0.01f);
                lineRenderer.SetPositions(GetParabolaPoints(streams[i].BirthPosition, streams[i].DeathPosition, 10));
            }
        }

        void InitializeNoise()
        {
            PerlinTexture = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32);
            PerlinTexture.hideFlags = HideFlags.DontSave;
            PerlinTexture.filterMode = FilterMode.Point;
            PerlinTexture.wrapMode = TextureWrapMode.Repeat;
            PerlinMaterial = new Material(PerlinShader);
            Graphics.Blit(null, PerlinTexture, PerlinMaterial, 0);
        }

        int GetActiveBuffSize(ComputeBuffer cb)
        {
            int[] args = new int[] { 0, 1, 0, 0 };
            BuffArgs.SetData(args);
            ComputeBuffer.CopyCount(cb, BuffArgs, 0);
            BuffArgs.GetData(args);
            return args[0];
        }

        ComputeBuffer GetActiveBuff(ComputeBuffer cb)
        {
            ComputeBuffer.CopyCount(cb, BuffArgs, 0);
            return BuffArgs;
        }

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

        void Start()
        {
            urg = UrgDevice.GetComponent<Urg>();

            InitializeComputeBuffers();
            InitializeStreamLines();
            InitializeDrops();
            InitializeDetectedObjects();
            InitializeNoise();
            

            // Drop | 0 : Init
            int numThreadGroupDrops = maxDropsCount / numThreadX;
            DropsCS.SetBuffer(0, "_DeadBuff1_In", DeadBuff1);
            DropsCS.SetBuffer(0, "_DropsBuff", DropsBuff);
            DropsCS.Dispatch(0, numThreadGroupDrops, 1, 1);
        }

        void Update()
        {

            RenderTexture rt = RenderTexture.GetTemporary(PerlinTexture.width, PerlinTexture.height, 0);
            Graphics.Blit(PerlinTexture, rt, PerlinMaterial, 0);
            Graphics.Blit(rt, PerlinTexture);
            rt.Release();

            UpdateDetectedObjects();
        }


        void OnRenderObject()
        {
            Vector3 mousePos = BillboardCam.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, -BillboardCam.transform.position.z));
            DropsCS.SetVector("_MousePosition", new Vector4(mousePos.x, mousePos.y, mousePos.z, 1));
            Debug.DrawLine(Vector3.zero, mousePos, Color.white);

            //Drops
            DropsCS.SetInt("_StreamsCount", streamLinesCount);
            DropsCS.SetFloat("_DeltaTime", Time.deltaTime);
            DropsCS.SetFloat("_Gravity", g);
            DropsCS.SetFloat("_Jet", Jet);
            DropsCS.SetFloat("_RandSeed", Random.Range(0, 1.0f));
            DropsCS.SetVector("_DropParams", dropParams);
            DropsCS.SetFloat("_DropSize", dropSize);
            DropsCS.SetVector("_SplashParams", splashParams);
            DropsCS.SetFloat("_SplashSize", splashSize);

            // Drop | 1 : Emit
            DropsCS.SetBuffer(1, "_DeadBuff1_Out", DeadBuff1);
            DropsCS.SetBuffer(1, "_AliveBuff2_In", AliveBuff2);
            DropsCS.SetBuffer(1, "_StreamLinesBuff", StreamLinesBuff);
            DropsCS.SetTexture(1, "_PerlinTexture", PerlinTexture);
            var emitCount = GetActiveBuffSize(DeadBuff1) > maxEmitQuantity ? streamLinesCount / numThreadX : 0;
            DropsCS.Dispatch(1, emitCount, 1, 1);

            // Drop | 2 : Update
            DropsCS.SetBuffer(2, "_AliveBuff1_Out", AliveBuff1);
            DropsCS.SetBuffer(2, "_AliveBuff2_In", AliveBuff2);
            DropsCS.SetBuffer(2, "_DeadBuff1_In", DeadBuff1);
            DropsCS.SetBuffer(2, "_StreamLinesBuff", StreamLinesBuff);
            DropsCS.SetBuffer(2, "_DetectedObjBuff", DetectedObjectsBuff);
            DropsCS.Dispatch(2, GetActiveBuffSize(AliveBuff1) / numThreadX, 1, 1);

            // vert / geom / frag shader
            DropsMaterial.SetPass(0);
            var inverseViewMatrix = BillboardCam.worldToCameraMatrix.inverse;
            DropsMaterial.SetMatrix("_InvViewMatrix", inverseViewMatrix);
            DropsMaterial.SetTexture("_DropTexture", DropTexture);
            DropsMaterial.SetFloat("_DropSize", dropSize);
            DropsMaterial.SetBuffer("_DropsBuff", AliveBuff2);
            Graphics.DrawProceduralIndirect(MeshTopology.Points, GetActiveBuff(AliveBuff2));

            // Drop | 3 : Move
            AliveBuff1.SetCounterValue(0);
            DropsCS.SetBuffer(3, "_AliveBuff1_In", AliveBuff1);
            DropsCS.SetBuffer(3, "_AliveBuff2_Out", AliveBuff2);
            DropsCS.Dispatch(3, GetActiveBuffSize(AliveBuff2) / numThreadX, 1, 1);

            AliveBuff2.SetCounterValue(0);
        }

        void OnDisable()
        {
            if (StreamLinesBuff != null) StreamLinesBuff.Release();
            if (DropsBuff != null) DropsBuff.Release();
            if (DeadBuff1 != null) DeadBuff1.Release();
            if (DeadBuff2 != null) DeadBuff2.Release();
            if (AliveBuff1 != null) AliveBuff1.Release();
            if (AliveBuff2 != null) AliveBuff2.Release();
            if (DetectedObjectsBuff != null) DetectedObjectsBuff.Release();
            if (BuffArgs != null) BuffArgs.Release();

            if (StreamLinesMaterial != null) DestroyImmediate(StreamLinesMaterial);
            if (DropsMaterial != null) DestroyImmediate(DropsMaterial);
        }
    }
}