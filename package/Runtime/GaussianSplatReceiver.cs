using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using GaussianSplatting.Runtime.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System.Threading.Tasks;

namespace GaussianSplatting.Runtime
{
    public class GaussianSplatReceiver : MonoBehaviour
    {
        private GaussianSplatRenderAsset _mAsset;
        private GaussianSplatRenderer _renderer;
        private CameraVisualizer _cameraVisualizer;
        private const string KCamerasJson = "cameras.json";
        private TcpClient _connectedClient;
        private string _errorMessage;
        private const GaussianSplatRenderAsset.ColorFormat MFormatColor = GaussianSplatRenderAsset.ColorFormat.Float32x4;
        private const GaussianSplatRenderAsset.VectorFormat MFormatPos = GaussianSplatRenderAsset.VectorFormat.Float32;
        private const GaussianSplatRenderAsset.VectorFormat MFormatScale = GaussianSplatRenderAsset.VectorFormat.Float32;
        private const GaussianSplatRenderAsset.SHFormat MFormatSH = GaussianSplatRenderAsset.SHFormat.Float32;
        private long _prevFileSize;
        private string _prevPlyPath;
        private int _prevVertexCount;

        private HttpListener _listener;
        private CancellationTokenSource _cancellationTokenSource;

        private void Start()
        {
            _renderer = GetComponent<GaussianSplatRenderer>();
            _cameraVisualizer = GetComponent<CameraVisualizer>();
            _mAsset = ScriptableObject.CreateInstance<GaussianSplatRenderAsset>();
            _renderer.m_Asset = _mAsset;
            _cameraVisualizer.renderAsset = _mAsset;
            _cancellationTokenSource = new CancellationTokenSource();
            Task.Run(() => StartListening(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }

        private void StartListening(CancellationToken cancellationToken)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:8080/");
            try
            {
                _listener.Start();
                Debug.Log("Listening for HTTP POST requests on http://127.0.0.1:8080/");
                while (!cancellationToken.IsCancellationRequested)
                    if (_listener.IsListening)
                    {
                        var context = _listener.GetContext();
                        Task.Run(() => ProcessRequest(context), cancellationToken);
                    }
                    else
                    {
                        break;
                    }
            }
            catch (Exception ex)
            {
                Debug.LogError($"An error occurred: {ex.Message}");
            }
            finally
            {
                _listener.Close();
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            if (context.Request.HttpMethod == "POST")
            {
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                {
                    var postData = reader.ReadToEnd();
                    var normalizedPath = Path.GetFullPath(postData.Replace("\"", "").Replace("/", Path.DirectorySeparatorChar.ToString()));
                    Debug.Log($"Received POST data: {normalizedPath}");
                    UpdateAssetFromPath(normalizedPath);
                }

                var responseString = "OK";
                var buffer = Encoding.UTF8.GetBytes(responseString);
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            }

            context.Response.OutputStream.Close();
        }

        private void OnDestroy()
        {
            _cancellationTokenSource.Cancel();
            if (_listener != null)
            {
                _listener.Stop();
                _listener.Close();
            }
        }

        private unsafe void UpdateAssetFromPath(string path)
        {
            var cameras = LoadJsonCamerasFile(path, true);
            using var inputSplats = LoadPlySplatFile(path);
            if (inputSplats.Length == 0)
            {
                return;
            }

            float3 boundsMin, boundsMax;
            var boundsJob = new CalcBoundsJob
            {
                m_BoundsMin = &boundsMin,
                m_BoundsMax = &boundsMax,
                m_SplatData = inputSplats
            };
            boundsJob.Schedule().Complete();

            ReorderMorton(inputSplats, boundsMin, boundsMax);

            // cluster SHs
            NativeArray<int> splatSHIndices = default;

            _mAsset.Initialize(inputSplats.Length, MFormatPos, MFormatScale, MFormatColor, MFormatSH, boundsMin,
                boundsMax, cameras);

            var dataHash = new Hash128((uint)_mAsset.splatCount, (uint)_mAsset.formatVersion, 0, 0);

            LinearizeData(inputSplats);

            splatSHIndices.Dispose();


            _mAsset.SetAssetFiles(
               CreatePositionsData(inputSplats, ref dataHash),
               CreateOtherData(inputSplats, ref dataHash, splatSHIndices),
               CreateColorData(inputSplats, ref dataHash),
               CreateSHData(inputSplats, ref dataHash));
            _mAsset.SetDataHash(dataHash);
        }

        private unsafe NativeArray<InputSplatData> LoadPlySplatFile(string plyPath)
        {
            NativeArray<InputSplatData> data = default;
            if (!File.Exists(plyPath))
            {
                Debug.LogError($"Did not find {plyPath} file");
                return data;
            }

            int splatCount;
            int vertexStride;
            NativeArray<byte> verticesRawData;
            try
            {
                PLYFileReader.ReadFile(plyPath, out splatCount, out vertexStride, out _, out verticesRawData);
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.Message);
                return data;
            }

            if (UnsafeUtility.SizeOf<InputSplatData>() != vertexStride)
            {
                Debug.LogError(
                    $"PLY vertex size mismatch, expected {UnsafeUtility.SizeOf<InputSplatData>()} but file has {vertexStride}");
                return data;
            }

            // reorder SHs
            var floatData = verticesRawData.Reinterpret<float>(1);
            ReorderSHs(splatCount, (float*)floatData.GetUnsafePtr());

            return verticesRawData.Reinterpret<InputSplatData>(1);
        }

        [BurstCompile]
        private static unsafe void ReorderSHs(int splatCount, float* data)
        {
            var splatStride = UnsafeUtility.SizeOf<InputSplatData>() / 4;
            int shStartOffset = 9, shCount = 15;
            var tmp = stackalloc float[shCount * 3];
            var idx = shStartOffset;
            for (var i = 0; i < splatCount; ++i)
            {
                for (var j = 0; j < shCount; ++j)
                {
                    tmp[j * 3 + 0] = data[idx + j];
                    tmp[j * 3 + 1] = data[idx + j + shCount];
                    tmp[j * 3 + 2] = data[idx + j + shCount * 2];
                }

                for (var j = 0; j < shCount * 3; ++j) data[idx + j] = tmp[j];

                idx += splatStride;
            }
        }

        private static void ReorderMorton(NativeArray<InputSplatData> splatData, float3 boundsMin, float3 boundsMax)
        {
            var order = new ReorderMortonJob
            {
                m_SplatData = splatData,
                m_BoundsMin = boundsMin,
                m_InvBoundsSize = 1.0f / (boundsMax - boundsMin),
                m_Order = new NativeArray<(ulong, int)>(splatData.Length, Allocator.TempJob)
            };
            order.Schedule(splatData.Length, 4096).Complete();
            order.m_Order.Sort(new OrderComparer());

            NativeArray<InputSplatData> copy = new(order.m_SplatData, Allocator.TempJob);
            for (var i = 0; i < copy.Length; ++i)
                order.m_SplatData[i] = copy[order.m_Order[i].Item2];
            copy.Dispose();

            order.m_Order.Dispose();
        }

        [BurstCompile]
        private static unsafe void GatherSHs(int splatCount, InputSplatData* splatData, float* shData)
        {
            for (var i = 0; i < splatCount; ++i)
            {
                UnsafeUtility.MemCpy(shData, (float*)splatData + 9, 15 * 3 * sizeof(float));
                splatData++;
                shData += 15 * 3;
            }
        }

        private static void LinearizeData(NativeArray<InputSplatData> splatData)
        {
            var job = new LinearizeDataJob
            {
                splatData = splatData
            };
            job.Schedule(splatData.Length, 4096).Complete();
        }

        private static ulong EncodeFloat3ToNorm16(float3 v) // 48 bits: 16.16.16
        {
            return (ulong)(v.x * 65535.5f) | ((ulong)(v.y * 65535.5f) << 16) | ((ulong)(v.z * 65535.5f) << 32);
        }

        private static uint EncodeFloat3ToNorm11(float3 v) // 32 bits: 11.10.11
        {
            return (uint)(v.x * 2047.5f) | ((uint)(v.y * 1023.5f) << 11) | ((uint)(v.z * 2047.5f) << 21);
        }

        private static ushort EncodeFloat3ToNorm655(float3 v) // 16 bits: 6.5.5
        {
            return (ushort)((uint)(v.x * 63.5f) | ((uint)(v.y * 31.5f) << 6) | ((uint)(v.z * 31.5f) << 11));
        }

        private static ushort EncodeFloat3ToNorm565(float3 v) // 16 bits: 5.6.5
        {
            return (ushort)((uint)(v.x * 31.5f) | ((uint)(v.y * 63.5f) << 5) | ((uint)(v.z * 31.5f) << 11));
        }

        private static uint EncodeQuatToNorm10(float4 v) // 32 bits: 10.10.10.2
        {
            return (uint)(v.x * 1023.5f) | ((uint)(v.y * 1023.5f) << 10) | ((uint)(v.z * 1023.5f) << 20) |
                   ((uint)(v.w * 3.5f) << 30);
        }

        private static unsafe void EmitEncodedVector(float3 v, byte* outputPtr, GaussianSplatRenderAsset.VectorFormat format)
        {
            switch (format)
            {
                case GaussianSplatRenderAsset.VectorFormat.Float32:
                {
                    *(float*)outputPtr = v.x;
                    *(float*)(outputPtr + 4) = v.y;
                    *(float*)(outputPtr + 8) = v.z;
                }
                    break;
                case GaussianSplatRenderAsset.VectorFormat.Norm16:
                {
                    var enc = EncodeFloat3ToNorm16(math.saturate(v));
                    *(uint*)outputPtr = (uint)enc;
                    *(ushort*)(outputPtr + 4) = (ushort)(enc >> 32);
                }
                    break;
                case GaussianSplatRenderAsset.VectorFormat.Norm11:
                {
                    var enc = EncodeFloat3ToNorm11(math.saturate(v));
                    *(uint*)outputPtr = enc;
                }
                    break;
                case GaussianSplatRenderAsset.VectorFormat.Norm6:
                {
                    var enc = EncodeFloat3ToNorm655(math.saturate(v));
                    *(ushort*)outputPtr = enc;
                }
                    break;
            }
        }

        private static int NextMultipleOf(int size, int multipleOf)
        {
            return (size + multipleOf - 1) / multipleOf * multipleOf;
        }

        private static byte[] CreatePositionsData(NativeArray<InputSplatData> inputSplats,
            ref Hash128 dataHash)
        {
            var format = GaussianSplatRenderAsset.VectorFormat.Float32;
            var dataLen = inputSplats.Length * GaussianSplatRenderAsset.GetVectorSize(format);
            dataLen = NextMultipleOf(dataLen, 8); // serialized as ulong
            NativeArray<byte> data = new(dataLen, Allocator.TempJob);
            try
            {
                var job = new CreatePositionsDataJob
                {
                    MInput = inputSplats,
                    m_Format = format,
                    m_FormatSize = GaussianSplatRenderAsset.GetVectorSize(format),
                    MOutput = data
                };
                job.Schedule(inputSplats.Length, 8192).Complete();
                dataHash.Append(data);

                // Copy data to a byte array
                byte[] result = new byte[data.Length];
                data.CopyTo(result);
                return result;
            }
            finally
            {
                data.Dispose();
            }
        }

        private static byte[] CreateOtherData(NativeArray<InputSplatData> inputSplats,
            ref Hash128 dataHash, NativeArray<int> splatSHIndices)
        {
            var format = GaussianSplatRenderAsset.VectorFormat.Float32;
            var formatSize = GaussianSplatRenderAsset.GetOtherSizeNoSHIndex(format);
            if (splatSHIndices.IsCreated)
                formatSize += 2;
            var dataLen = inputSplats.Length * formatSize;

            dataLen = NextMultipleOf(dataLen, 8); // serialized as ulong
            NativeArray<byte> data = new(dataLen, Allocator.TempJob);
            try
            {
                var job = new CreateOtherDataJob
                {
                    MInput = inputSplats,
                    MSplatSHIndices = splatSHIndices,
                    MScaleFormat = format,
                    MFormatSize = formatSize,
                    MOutput = data
                };
                job.Schedule(inputSplats.Length, 8192).Complete();
                dataHash.Append(data);

                // Copy data to a byte array
                byte[] result = new byte[data.Length];
                data.CopyTo(result);
                return result;
            }
            finally
            {
                data.Dispose();
            }
        }

        private static byte[] CreateColorData(NativeArray<InputSplatData> inputSplats,
            ref Hash128 dataHash)
        {
            const GaussianSplatRenderAsset.ColorFormat formatColor = GaussianSplatRenderAsset.ColorFormat.Float32x4;
            var (width, height) = GaussianSplatRenderAsset.CalcTextureSize(inputSplats.Length);
            NativeArray<float4> data = new(width * height, Allocator.TempJob);

            try
            {
                var job = new CreateColorDataJob
                {
                    MInput = inputSplats,
                    MOutput = data
                };
                job.Schedule(inputSplats.Length, 8192).Complete();

                dataHash.Append(data);
                dataHash.Append((int)formatColor);

                var gfxFormat = GaussianSplatRenderAsset.ColorFormatToGraphics(formatColor);
                var dstSize = (int)GraphicsFormatUtility.ComputeMipmapSize(width, height, gfxFormat);

                NativeArray<byte> outputData = new(dstSize, Allocator.TempJob);
                try
                {
                    var jobConvert = new ConvertColorJob
                    {
                        width = width,
                        height = height,
                        inputData = data,
                        format = formatColor,
                        outputData = outputData,
                        formatBytesPerPixel = dstSize / width / height
                    };
                    jobConvert.Schedule(height, 1).Complete();

                    // Copy data to a byte array
                    byte[] result = new byte[outputData.Length];
                    outputData.CopyTo(result);
                    return result;
                }
                finally
                {
                    outputData.Dispose();
                }
            }
            finally
            {
                data.Dispose();
            }
        }

        private static byte[] CreateSHData(NativeArray<InputSplatData> inputSplats, ref Hash128 dataHash)
        {
            const GaussianSplatRenderAsset.SHFormat format = GaussianSplatRenderAsset.SHFormat.Float32;
            var dataLen = (int)GaussianSplatRenderAsset.CalcSHDataSize(inputSplats.Length, format);
            NativeArray<byte> data = new(dataLen, Allocator.TempJob);
            try
            {
                var job = new CreateSHDataJob
                {
                    MInput = inputSplats,
                    m_Format = format,
                    MOutput = data
                };
                job.Schedule(inputSplats.Length, 8192).Complete();
                dataHash.Append(data);

                // Copy data to a byte array
                byte[] result = new byte[data.Length];
                data.CopyTo(result);
                return result;
            }
            finally
            {
                data.Dispose();
            }
        }

        private static int SplatIndexToTextureIndex(uint idx)
        {
            var xy = GaussianUtils.DecodeMorton2D_16x16(idx);
            const uint width = GaussianSplatRenderAsset.kTextureWidth / 16;
            idx >>= 8;
            var x = idx % width * 16 + xy.x;
            var y = idx / width * 16 + xy.y;
            return (int)(y * GaussianSplatRenderAsset.kTextureWidth + x);
        }


        private static GaussianSplatRenderAsset.CameraInfo[] LoadJsonCamerasFile(string curPath, bool doImport)
        {
            if (!doImport)
                return null;

            string camerasPath;
            while (true)
            {
                var dir = Path.GetDirectoryName(curPath);
                if (!Directory.Exists(dir))
                    return null;
                camerasPath = $"{dir}/{KCamerasJson}";
                if (File.Exists(camerasPath))
                    break;
                curPath = dir;
            }

            if (!File.Exists(camerasPath))
                return null;

            var json = File.ReadAllText(camerasPath);
            var jsonCameras = json.FromJson<List<JsonCamera>>();
            if (jsonCameras == null || jsonCameras.Count == 0)
                return null;

            var result = new GaussianSplatRenderAsset.CameraInfo[jsonCameras.Count];
            for (var camIndex = 0; camIndex < jsonCameras.Count; camIndex++)
            {
                var jsonCam = jsonCameras[camIndex];
                var pos = new Vector3(jsonCam.position[0], jsonCam.position[1], jsonCam.position[2]);
                // the matrix is a "view matrix", not "camera matrix" lol
                var axisx = new Vector3(jsonCam.rotation[0][0], jsonCam.rotation[1][0], jsonCam.rotation[2][0]);
                var axisy = new Vector3(jsonCam.rotation[0][1], jsonCam.rotation[1][1], jsonCam.rotation[2][1]);
                var axisz = new Vector3(jsonCam.rotation[0][2], jsonCam.rotation[1][2], jsonCam.rotation[2][2]);

                axisy *= -1;
                axisz *= -1;

                var cam = new GaussianSplatRenderAsset.CameraInfo
                {
                    pos = pos,
                    axisX = axisx,
                    axisY = axisy,
                    axisZ = axisz,
                    fov = 25 //@TODO
                };
                result[camIndex] = cam;
            }

            return result;
        }

        [BurstCompile]
        private unsafe struct CalcBoundsJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public float3* m_BoundsMin;
            [NativeDisableUnsafePtrRestriction] public float3* m_BoundsMax;
            [ReadOnly] public NativeArray<InputSplatData> m_SplatData;

            public void Execute()
            {
                float3 boundsMin = float.PositiveInfinity;
                float3 boundsMax = float.NegativeInfinity;

                for (var i = 0; i < m_SplatData.Length; ++i)
                {
                    float3 pos = m_SplatData[i].pos;
                    boundsMin = math.min(boundsMin, pos);
                    boundsMax = math.max(boundsMax, pos);
                }

                *m_BoundsMin = boundsMin;
                *m_BoundsMax = boundsMax;
            }
        }

        [BurstCompile]
        private struct ReorderMortonJob : IJobParallelFor
        {
            private const float kScaler = (1 << 21) - 1;
            public float3 m_BoundsMin;
            public float3 m_InvBoundsSize;
            [ReadOnly] public NativeArray<InputSplatData> m_SplatData;
            public NativeArray<(ulong, int)> m_Order;

            public void Execute(int index)
            {
                var pos = ((float3)m_SplatData[index].pos - m_BoundsMin) * m_InvBoundsSize * kScaler;
                var ipos = (uint3)pos;
                var code = GaussianUtils.MortonEncode3(ipos);
                m_Order[index] = (code, index);
            }
        }

        private struct OrderComparer : IComparer<(ulong, int)>
        {
            public int Compare((ulong, int) a, (ulong, int) b)
            {
                if (a.Item1 < b.Item1) return -1;
                if (a.Item1 > b.Item1) return +1;
                return a.Item2 - b.Item2;
            }
        }

        [BurstCompile]
        private struct ConvertSHClustersJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> MInput;
            public NativeArray<GaussianSplatRenderAsset.SHTableItemFloat16> MOutput;

            public void Execute(int index)
            {
                var addr = index * 15;
                GaussianSplatRenderAsset.SHTableItemFloat16 res;
                res.sh1 = new half3(MInput[addr + 0]);
                res.sh2 = new half3(MInput[addr + 1]);
                res.sh3 = new half3(MInput[addr + 2]);
                res.sh4 = new half3(MInput[addr + 3]);
                res.sh5 = new half3(MInput[addr + 4]);
                res.sh6 = new half3(MInput[addr + 5]);
                res.sh7 = new half3(MInput[addr + 6]);
                res.sh8 = new half3(MInput[addr + 7]);
                res.sh9 = new half3(MInput[addr + 8]);
                res.shA = new half3(MInput[addr + 9]);
                res.shB = new half3(MInput[addr + 10]);
                res.shC = new half3(MInput[addr + 11]);
                res.shD = new half3(MInput[addr + 12]);
                res.shE = new half3(MInput[addr + 13]);
                res.shF = new half3(MInput[addr + 14]);
                res.shPadding = default;
                MOutput[index] = res;
            }
        }

        [BurstCompile]
        private struct LinearizeDataJob : IJobParallelFor
        {
            public NativeArray<InputSplatData> splatData;

            public void Execute(int index)
            {
                var splat = splatData[index];

                // rot
                var q = splat.rot;
                var qq = GaussianUtils.NormalizeSwizzleRotation(new float4(q.x, q.y, q.z, q.w));
                qq = GaussianUtils.PackSmallest3Rotation(qq);
                splat.rot = new Quaternion(qq.x, qq.y, qq.z, qq.w);

                // scale
                splat.scale = GaussianUtils.LinearScale(splat.scale);

                // color
                splat.dc0 = GaussianUtils.SH0ToColor(splat.dc0);
                splat.opacity = GaussianUtils.Sigmoid(splat.opacity);

                splatData[index] = splat;
            }
        }

        // input file splat data is expected to be in this format
        private struct InputSplatData
        {
            public Vector3 pos;
            public Vector3 nor;
            public Vector3 dc0;
            public Vector3 sh1, sh2, sh3, sh4, sh5, sh6, sh7, sh8, sh9, shA, shB, shC, shD, shE, shF;
            public float opacity;
            public Vector3 scale;
            public Quaternion rot;
        }

        [BurstCompile]
        private unsafe struct ConvertColorJob : IJobParallelFor
        {
            public int width, height;
            [ReadOnly] public NativeArray<float4> inputData;
            [NativeDisableParallelForRestriction] public NativeArray<byte> outputData;
            public GaussianSplatRenderAsset.ColorFormat format;
            public int formatBytesPerPixel;

            public void Execute(int y)
            {
                var srcIdx = y * width;
                var dstPtr = (byte*)outputData.GetUnsafePtr() + y * width * formatBytesPerPixel;
                for (var x = 0; x < width; ++x)
                {
                    var pix = inputData[srcIdx];

                    switch (format)
                    {
                        case GaussianSplatRenderAsset.ColorFormat.Float32x4:
                        {
                            *(float4*)dstPtr = pix;
                        }
                            break;
                        case GaussianSplatRenderAsset.ColorFormat.Float16x4:
                        {
                            var enc = new half4(pix);
                            *(half4*)dstPtr = enc;
                        }
                            break;
                        case GaussianSplatRenderAsset.ColorFormat.Norm8x4:
                        {
                            pix = math.saturate(pix);
                            var enc = (uint)(pix.x * 255.5f) | ((uint)(pix.y * 255.5f) << 8) |
                                      ((uint)(pix.z * 255.5f) << 16) | ((uint)(pix.w * 255.5f) << 24);
                            *(uint*)dstPtr = enc;
                        }
                            break;
                    }

                    srcIdx++;
                    dstPtr += formatBytesPerPixel;
                }
            }
        }

        [BurstCompile]
        private unsafe struct CreatePositionsDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<InputSplatData> MInput;
            public GaussianSplatRenderAsset.VectorFormat m_Format;
            public int m_FormatSize;
            [NativeDisableParallelForRestriction] public NativeArray<byte> MOutput;

            public void Execute(int index)
            {
                var outputPtr = (byte*)MOutput.GetUnsafePtr() + index * m_FormatSize;
                EmitEncodedVector(MInput[index].pos, outputPtr, m_Format);
            }
        }

        [BurstCompile]
        private unsafe struct CreateOtherDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<InputSplatData> MInput;

            [NativeDisableContainerSafetyRestriction] [ReadOnly]
            public NativeArray<int> MSplatSHIndices;

            public GaussianSplatRenderAsset.VectorFormat MScaleFormat;
            public int MFormatSize;
            [NativeDisableParallelForRestriction] public NativeArray<byte> MOutput;

            public void Execute(int index)
            {
                var outputPtr = (byte*)MOutput.GetUnsafePtr() + index * MFormatSize;

                // rotation: 4 bytes
                {
                    var rotQ = MInput[index].rot;
                    var rot = new float4(rotQ.x, rotQ.y, rotQ.z, rotQ.w);
                    var enc = EncodeQuatToNorm10(rot);
                    *(uint*)outputPtr = enc;
                    outputPtr += 4;
                }

                // scale: 6, 4 or 2 bytes
                EmitEncodedVector(MInput[index].scale, outputPtr, MScaleFormat);
                outputPtr += GaussianSplatRenderAsset.GetVectorSize(MScaleFormat);

                // SH index
                if (MSplatSHIndices.IsCreated)
                    *(ushort*)outputPtr = (ushort)MSplatSHIndices[index];
            }
        }

        [BurstCompile]
        private struct CreateColorDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<InputSplatData> MInput;
            [NativeDisableParallelForRestriction] public NativeArray<float4> MOutput;

            public void Execute(int index)
            {
                var splat = MInput[index];
                var i = SplatIndexToTextureIndex((uint)index);
                MOutput[i] = new float4(splat.dc0.x, splat.dc0.y, splat.dc0.z, splat.opacity);
            }
        }

        [BurstCompile]
        private unsafe struct CreateSHDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<InputSplatData> MInput;
            public GaussianSplatRenderAsset.SHFormat m_Format;
            public NativeArray<byte> MOutput;

            public void Execute(int index)
            {
                var splat = MInput[index];

                switch (m_Format)
                {
                    case GaussianSplatRenderAsset.SHFormat.Float32:
                    {
                        GaussianSplatRenderAsset.SHTableItemFloat32 res;
                        res.sh1 = splat.sh1;
                        res.sh2 = splat.sh2;
                        res.sh3 = splat.sh3;
                        res.sh4 = splat.sh4;
                        res.sh5 = splat.sh5;
                        res.sh6 = splat.sh6;
                        res.sh7 = splat.sh7;
                        res.sh8 = splat.sh8;
                        res.sh9 = splat.sh9;
                        res.shA = splat.shA;
                        res.shB = splat.shB;
                        res.shC = splat.shC;
                        res.shD = splat.shD;
                        res.shE = splat.shE;
                        res.shF = splat.shF;
                        res.shPadding = default;
                        ((GaussianSplatRenderAsset.SHTableItemFloat32*)MOutput.GetUnsafePtr())[index] = res;
                    }
                        break;
                    case GaussianSplatRenderAsset.SHFormat.Float16:
                    {
                        GaussianSplatRenderAsset.SHTableItemFloat16 res;
                        res.sh1 = new half3(splat.sh1);
                        res.sh2 = new half3(splat.sh2);
                        res.sh3 = new half3(splat.sh3);
                        res.sh4 = new half3(splat.sh4);
                        res.sh5 = new half3(splat.sh5);
                        res.sh6 = new half3(splat.sh6);
                        res.sh7 = new half3(splat.sh7);
                        res.sh8 = new half3(splat.sh8);
                        res.sh9 = new half3(splat.sh9);
                        res.shA = new half3(splat.shA);
                        res.shB = new half3(splat.shB);
                        res.shC = new half3(splat.shC);
                        res.shD = new half3(splat.shD);
                        res.shE = new half3(splat.shE);
                        res.shF = new half3(splat.shF);
                        res.shPadding = default;
                        ((GaussianSplatRenderAsset.SHTableItemFloat16*)MOutput.GetUnsafePtr())[index] = res;
                    }
                        break;
                    case GaussianSplatRenderAsset.SHFormat.Norm11:
                    {
                        GaussianSplatRenderAsset.SHTableItemNorm11 res;
                        res.sh1 = EncodeFloat3ToNorm11(splat.sh1);
                        res.sh2 = EncodeFloat3ToNorm11(splat.sh2);
                        res.sh3 = EncodeFloat3ToNorm11(splat.sh3);
                        res.sh4 = EncodeFloat3ToNorm11(splat.sh4);
                        res.sh5 = EncodeFloat3ToNorm11(splat.sh5);
                        res.sh6 = EncodeFloat3ToNorm11(splat.sh6);
                        res.sh7 = EncodeFloat3ToNorm11(splat.sh7);
                        res.sh8 = EncodeFloat3ToNorm11(splat.sh8);
                        res.sh9 = EncodeFloat3ToNorm11(splat.sh9);
                        res.shA = EncodeFloat3ToNorm11(splat.shA);
                        res.shB = EncodeFloat3ToNorm11(splat.shB);
                        res.shC = EncodeFloat3ToNorm11(splat.shC);
                        res.shD = EncodeFloat3ToNorm11(splat.shD);
                        res.shE = EncodeFloat3ToNorm11(splat.shE);
                        res.shF = EncodeFloat3ToNorm11(splat.shF);
                        ((GaussianSplatRenderAsset.SHTableItemNorm11*)MOutput.GetUnsafePtr())[index] = res;
                    }
                        break;
                    case GaussianSplatRenderAsset.SHFormat.Norm6:
                    {
                        GaussianSplatRenderAsset.SHTableItemNorm6 res;
                        res.sh1 = EncodeFloat3ToNorm565(splat.sh1);
                        res.sh2 = EncodeFloat3ToNorm565(splat.sh2);
                        res.sh3 = EncodeFloat3ToNorm565(splat.sh3);
                        res.sh4 = EncodeFloat3ToNorm565(splat.sh4);
                        res.sh5 = EncodeFloat3ToNorm565(splat.sh5);
                        res.sh6 = EncodeFloat3ToNorm565(splat.sh6);
                        res.sh7 = EncodeFloat3ToNorm565(splat.sh7);
                        res.sh8 = EncodeFloat3ToNorm565(splat.sh8);
                        res.sh9 = EncodeFloat3ToNorm565(splat.sh9);
                        res.shA = EncodeFloat3ToNorm565(splat.shA);
                        res.shB = EncodeFloat3ToNorm565(splat.shB);
                        res.shC = EncodeFloat3ToNorm565(splat.shC);
                        res.shD = EncodeFloat3ToNorm565(splat.shD);
                        res.shE = EncodeFloat3ToNorm565(splat.shE);
                        res.shF = EncodeFloat3ToNorm565(splat.shF);
                        res.shPadding = default;
                        ((GaussianSplatRenderAsset.SHTableItemNorm6*)MOutput.GetUnsafePtr())[index] = res;
                    }
                        break;
                }
            }
        }

        [Serializable]
        public class JsonCamera
        {
            public int id;
            public string img_name;
            public int width;
            public int height;
            public float[] position;
            public float fx;
            public float fy;
            public float[][] rotation;
        }
    }
}